using System.Text;

namespace SmkyCLI;

/// <summary>
/// 艾宾浩斯长期记忆存储。
/// 保持率 R = exp(-tau/S)，tau = max(0, currentTurn-lastReviewTurn)，只按逻辑轮次计龄；
/// 复习按整数轮可达点到点触发，并受分标签巩固次数上限约束；固定记忆不参与遗忘、复习与自动淘汰。
/// 维护入口先复习后淘汰：即使条目本轮已低于 theta，只要仍有巩固次数，会先获得一次巩固再参与淘汰。
/// 所有公开读写方法都显式接收逻辑轮次，测试无需真实网络或等待墙钟；时间戳仅用于记录与快照。
/// </summary>
public sealed class EbbinghausMemory
{
    /// <summary>
    /// 当前轮次准备复习的记忆
    /// </summary>
    public List<MemoryEntry> ReviewEntries { get; private set; } = new List<MemoryEntry>(16);
    
    private readonly List<MemoryEntry> _entries = new();
    private MemoryOptions _options;
    private int _logicalTurn;
    private DateTimeOffset _lastObservedAt;

    public EbbinghausMemory(MemoryOptions? options = null)
    {
        _options = (options ?? new MemoryOptions()).Normalize();
    }

    public MemoryOptions Options => _options;

    /// <summary>当前已观察到的逻辑轮次（只增不减）。</summary>
    public int LogicalTurn => _logicalTurn;

    /// <summary>当前已观察到的墙钟时间。</summary>
    public DateTimeOffset LastObservedAt => _lastObservedAt;

    public int Count => _entries.Count;

    /// <summary>只读视图；排序按创建时间，与内部存储顺序一致。</summary>
    public IReadOnlyList<MemoryEntry> Entries => _entries.AsReadOnly();

    /// <summary>运行时配置变化后替换参数：收敛强度上限，并在新预算更小时淘汰低保持率条目。</summary>
    public void ApplyOptions(MemoryOptions options)
    {
        _options = options.Normalize();
        foreach (MemoryEntry entry in _entries) entry.ClampStrength(_options.MaxStrength);
        if (_options.MaxEntries > 0) EnforceBudget(_logicalTurn, _lastObservedAt);
    }

    /// <summary>清空全部记忆与时钟（显式维护入口）。</summary>
    public void Clear()
    {
        _entries.Clear();
        _logicalTurn = 0;
        _lastObservedAt = default;
    }

    /// <summary>推进逻辑时钟；时间只前进，非法时间（default）被忽略。</summary>
    public void AdvanceTo(int turn, DateTimeOffset now)
    {
        if (turn > _logicalTurn) _logicalTurn = turn;
        if (now != default && now > _lastObservedAt) _lastObservedAt = now;
    }

    /// <summary>
    /// 登记一条长期记忆。摘要为空、时间非法、Id 重复、达到预算且无淘汰空间时返回 null，
    /// 调用方需回退到旧的摘要插入策略，避免内容丢失。
    /// 淘汰交由调用方紧随其后的 <see cref="ReviewForMaintenance"/> 完成，避免新条目在复习前被误删。
    /// </summary>
    public MemoryEntry? TryAdd(string summary, int turn, DateTimeOffset now,
        MemoryImportance importance = MemoryImportance.Normal, string? id = null,
        Dictionary<string, string>? metadata = null)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0 || _options.MaxStrength <= 0) return null;
        if (now == default) return null;

        string normalized = (summary ?? string.Empty).Trim();
        if (normalized.Length == 0) return null;

        string entryId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
        if (_entries.Any(e => string.Equals(e.Id, entryId, StringComparison.Ordinal))) return null;

        turn = Math.Max(0, turn);
        AdvanceTo(turn, now);

        double strength = Math.Min(_options.InitialStrength, _options.MaxStrength);
        MemoryEntry entry = new MemoryEntry(entryId, normalized, turn, now, turn, now,
            strength, 0, importance, metadata);
        _entries.Add(entry);

        EnforceBudget(turn, now);

        // 预算被固定记忆占满时，新条目可能刚加入就被淘汰，视为登记失败。
        return _entries.Contains(entry) ? entry : null;
    }

    public MemoryEntry? GetEntry(string id) =>
        _entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));

    /// <summary>按 Id 删除一条记忆（显式维护入口）。</summary>
    public bool Remove(string id)
    {
        MemoryEntry? entry = GetEntry(id);
        return entry is not null && _entries.Remove(entry);
    }

    /// <summary>调整重要性层级；Pinned 会使条目退出遗忘队列。</summary>
    public bool SetImportance(string id, MemoryImportance importance)
    {
        MemoryEntry? entry = GetEntry(id);
        if (entry is null) return false;
        entry.SetImportance(importance);
        return true;
    }

    /// <summary>计算年龄 tau（单位：逻辑轮）；负轮差按 0 处理。</summary>
    public double ComputeTau(MemoryEntry entry, int turn) =>
        Math.Max(0, turn - entry.LastReviewTurn);

    /// <summary>保持率 R；固定记忆恒为 1，非法强度按已遗忘处理。</summary>
    public double ComputeRetention(MemoryEntry entry, int turn)
    {
        if (entry.IsPinned) return 1;
        if (!double.IsFinite(entry.Strength) || entry.Strength <= 0) return 0;

        double tau = ComputeTau(entry, turn);
        double retention = Math.Exp(-tau / entry.Strength);
        return double.IsFinite(retention) ? Math.Clamp(retention, 0, 1) : 0;
    }

    /// <summary>
    /// 整数轮下最早可达的复习年龄：R &lt;= r_rev 等价于 tau &gt;= S·ln(1/r_rev)，
    /// 而 tau 只能取整数，故实际触发点是 ceil(S·ln(1/r_rev))。
    /// 该值不保证小于理论死亡年龄 S·ln(1/theta)：S 较小或 r_rev 接近 theta 时，
    /// 首次复习轮可能不早于首次淘汰轮，因此维护必须先复习后淘汰。
    /// </summary>
    public int ComputeEarliestReviewAge(double strength)
    {
        if (!double.IsFinite(strength) || strength <= 0) return 1;
        double age = Math.Ceiling(strength * Math.Log(1 / _options.ReviewRetention));
        return (int)Math.Clamp(age, 1, int.MaxValue);
    }

    /// <summary>
    /// 最早可达复习年龄对应的保持率，即整数轮下复习触发点的保持率上界（恒 &lt;= r_rev）。
    /// 增益以它为基准校准，使最早可达点上的强度倍率恰为设计公比 m；S 越大越接近 r_rev。
    /// </summary>
    public double ComputeReviewFloorRetention(double strength)
    {
        if (!double.IsFinite(strength) || strength <= 0) return 0;
        return Math.Exp(-ComputeEarliestReviewAge(strength) / strength);
    }

    /// <summary>
    /// 把保持率映射到离散层：R=1 为 0 层，R→theta 为最后一层。
    /// 仅用于稳定判定与淘汰排序，不改变每个条目的实际保持率。
    /// </summary>
    public int GetRetentionTier(double retention)
    {
        int lastTier = MemoryOptions.RetentionTierCount - 1;
        if (!double.IsFinite(retention) || retention <= _options.ForgettingThreshold) return lastTier;
        if (retention >= 1) return 0;

        double normalized = -Math.Log(retention) / Math.Log(1.0 / _options.ForgettingThreshold);
        if (!double.IsFinite(normalized) || normalized <= 0) return 0;
        int tier = (int)Math.Floor(normalized * MemoryOptions.RetentionTierCount);
        return Math.Clamp(tier, 0, lastTier);
    }

    /// <summary>
    /// 对到达复习点且未用尽巩固次数的非固定条目执行一次巩固：
    /// S ← min(S * (1 + (m-1) * (1-R) / (1-R_min)), S*)，m 为设计公比，
    /// R_min 为整数轮下最早可达复习点的保持率。逾期越久增益越大，最早可达点上恰为 m。
    /// 同一轮重复维护时 R 已回到 1，不会无条件连续强化。
    /// </summary>
    public int ReviewEligible(int turn, DateTimeOffset now)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0) return 0;
        AdvanceTo(turn, now);
        
        ReviewEntries.Clear();

        int reviewed = 0;
        foreach (MemoryEntry entry in _entries)
        {
            if (entry.IsPinned) { ReviewEntries.Add(entry); continue; }

            int maxReviews = _options.MaxReviewsFor(entry.Importance);
            if (maxReviews > 0 && entry.ReviewCount >= maxReviews) continue;

            double retention = ComputeRetention(entry, turn);
            if (retention > _options.ReviewRetention) continue;

            // 非法强度先回退到 S0 作基准，否则 R_min 会退化为 0 而失去校准意义。
            ReviewEntries.Add(entry);
            double baseStrength = double.IsFinite(entry.Strength) && entry.Strength > 0
                ? entry.Strength
                : _options.InitialStrength;
            double floor = ComputeReviewFloorRetention(baseStrength);
            double gain = 1 + (_options.ReinforcementRatio - 1) * (1 - retention) / (1 - floor);
            double strength = Math.Min(baseStrength * gain, _options.MaxStrength);

            entry.ApplyReview(Math.Max(0, turn), now, strength);
            reviewed++;
        }
        return reviewed;
    }

    /// <summary>硬淘汰：非固定条目 R &lt; theta 即移除，返回移除条数。</summary>
    public int PurgeExpired(int turn, DateTimeOffset now)
    {
        if (_entries.Count == 0) return 0;
        AdvanceTo(turn, now);

        return _entries.RemoveAll(entry =>
            !entry.IsPinned && ComputeRetention(entry, turn) < _options.ForgettingThreshold);
    }

    /// <summary>
    /// 预算超额时按“保持率最低 → 创建更早 → Id”淘汰非固定条目；固定记忆即使超预算也保留。
    /// </summary>
    public int EnforceBudget(int turn, DateTimeOffset now)
    {
        if (_options.MaxEntries <= 0) return 0;
        AdvanceTo(turn, now);

        int removed = 0;
        while (_entries.Count > _options.MaxEntries)
        {
            MemoryEntry? victim = _entries
                .Where(e => !e.IsPinned)
                .OrderBy(e => ComputeRetention(e, turn))
                .ThenBy(e => e.CreatedTurn)
                .ThenBy(e => e.CreatedAt)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            if (victim is null) break; // 剩余全部固定：不违背“固定记忆不淘汰”
            _entries.Remove(victim);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// 维护入口：先对到点条目复习、再硬淘汰、最后收敛预算。每轮注入前与压缩/总结成功后均调用。
    /// 先复习后淘汰保证「还有巩固次数」的条目即使本轮 R 已低于 θ，也能先获得一次巩固再参与淘汰，
    /// 避免高 θ 配置下新记忆在首次复习点前被删除。
    /// </summary>
    public int ReviewForMaintenance(int turn, DateTimeOffset now)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0 || _options.MaxStrength <= 0) return 0;

        AdvanceTo(turn, now);
        int reviewed = ReviewEligible(turn, now);
        PurgeExpired(turn, now);
        EnforceBudget(turn, now);
        return reviewed;
    }

    /// <summary>
    /// 检索并准备注入：先硬淘汰、最后收敛预算，不触发复习。
    /// 返回条目按创建时间稳定排序；签名只含离散字段，供调用方缓存提示词。
    /// </summary>
    public MemorySelection SelectForInjection(int turn, DateTimeOffset now)
    {
        if (!_options.Enabled || _options.MaxEntries <= 0 || _options.MaxStrength <= 0)
            return MemorySelection.Empty;

        AdvanceTo(turn, now);
        PurgeExpired(turn, now);
        EnforceBudget(turn, now);

        List<MemoryEntry> ordered = _entries
            .Where(e => e.IsPinned || ComputeRetention(e, turn) >= _options.ForgettingThreshold)
            .OrderBy(e => e.CreatedTurn)
            .ThenBy(e => e.CreatedAt)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
        if (ordered.Count == 0) return MemorySelection.Empty;

        Dictionary<string, int> tiers = new Dictionary<string, int>(ordered.Count);
        foreach (MemoryEntry entry in ordered)
            tiers[entry.Id] = GetRetentionTier(ComputeRetention(entry, turn));

        return new MemorySelection(ordered, BuildSignature(ordered, tiers), tiers);
    }

    /// <summary>生成版本化快照；不触发复习/淘汰，保证与当前状态一致。</summary>
    public MemorySnapshot CreateSnapshot() => new MemorySnapshot
    {
        SchemaVersion = MemorySnapshot.CurrentSchemaVersion,
        LogicalClock = _logicalTurn,
        SavedAt = _lastObservedAt,
        Entries = _entries.Select(MemorySnapshotEntry.FromEntry).ToList()
    };

    /// <summary>
    /// 从快照恢复。版本不匹配、重复 Id、空摘要等无效数据返回 false 且不修改当前状态；
    /// 合法快照的强度、复习次数、重要性、元数据与逻辑时钟均原样恢复。
    /// </summary>
    public bool TryRestore(MemorySnapshot? snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != MemorySnapshot.CurrentSchemaVersion ||
            snapshot.Entries is null)
            return false;

        List<MemoryEntry> restored = new List<MemoryEntry>(snapshot.Entries.Count);
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        DateTimeOffset fallbackTime = snapshot.SavedAt != default ? snapshot.SavedAt : DateTimeOffset.UtcNow;

        foreach (MemorySnapshotEntry dto in snapshot.Entries)
        {
            if (dto is null) return false;
            string summary = (dto.Summary ?? string.Empty).Trim();
            if (summary.Length == 0) return false;

            string id = string.IsNullOrWhiteSpace(dto.Id) ? Guid.NewGuid().ToString("N") : dto.Id.Trim();
            if (!ids.Add(id)) return false;

            int createdTurn = Math.Max(0, dto.CreatedTurn);
            int reviewTurn = Math.Max(createdTurn, dto.LastReviewTurn);

            DateTimeOffset createdAt = dto.CreatedAt != default ? dto.CreatedAt : fallbackTime;
            DateTimeOffset reviewedAt = dto.LastReviewedAt != default ? dto.LastReviewedAt : createdAt;
            if (reviewedAt < createdAt) reviewedAt = createdAt;

            double strength = double.IsFinite(dto.Strength) && dto.Strength > 0
                ? dto.Strength
                : _options.InitialStrength;
            if (_options.MaxStrength > 0) strength = Math.Min(strength, _options.MaxStrength);

            MemoryImportance importance = dto.Importance is >= MemoryImportance.Normal and <= MemoryImportance.Pinned
                ? dto.Importance
                : MemoryImportance.Normal;

            Dictionary<string, string>? metadata = dto.Metadata is { Count: > 0 }
                ? new Dictionary<string, string>(dto.Metadata)
                : null;

            restored.Add(new MemoryEntry(id, summary, createdTurn, createdAt, reviewTurn, reviewedAt,
                strength, Math.Max(0, dto.ReviewCount), importance, metadata));
        }

        _entries.Clear();
        _entries.AddRange(restored);
        _logicalTurn = Math.Max(0, snapshot.LogicalClock);
        _lastObservedAt = snapshot.SavedAt;
        return true;
    }

    /// <summary>从 JSON 快照恢复；解析失败返回 false。</summary>
    public bool TryRestore(string json)
    {
        MemorySnapshot? snapshot = MemorySnapshot.FromJson(json);
        return snapshot is not null && TryRestore(snapshot);
    }

    private static string BuildSignature(IReadOnlyList<MemoryEntry> entries, IReadOnlyDictionary<string, int> tiers)
    {
        StringBuilder builder = new StringBuilder("v1");
        foreach (MemoryEntry entry in entries)
            builder.Append('|').Append(entry.Id).Append(':').Append((int)entry.Importance)
                .Append(':').Append(tiers[entry.Id]);
        return builder.ToString();
    }
}