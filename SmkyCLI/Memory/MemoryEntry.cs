namespace SmkyCLI;

/// <summary>
/// 长期记忆的重要性层级。提取记忆时由模型做语义分类（Normal/Preference/Commitment），
/// Pinned 只能显式指定，表示用户/剧情契约类固定记忆，不进入遗忘队列与自动淘汰。
/// </summary>
public enum MemoryImportance
{
    /// <summary>普通摘要，默认级别。</summary>
    Normal = 0,
    /// <summary>用户偏好。</summary>
    Preference = 1,
    /// <summary>关系承诺或未完成事项。</summary>
    Commitment = 2,
    /// <summary>固定记忆，永不自动淘汰。</summary>
    Pinned = 3
}

/// <summary>
/// 一条长期记忆。外部只读，强度/复习状态只能由 <see cref="EbbinghausMemory"/> 修改，
/// 保证复习次数与时钟同步。
/// </summary>
public sealed class MemoryEntry
{
    public string Id { get; }
    public string Summary { get; }
    public int CreatedTurn { get; }
    public DateTimeOffset CreatedAt { get; }
    public int LastReviewTurn { get; private set; }
    public DateTimeOffset LastReviewedAt { get; private set; }

    /// <summary>当前记忆强度 S（单位：逻辑轮）。</summary>
    public double Strength { get; private set; }

    /// <summary>成功复习次数（创建不计入）。</summary>
    public int ReviewCount { get; private set; }

    public MemoryImportance Importance { get; private set; }

    /// <summary>固定记忆不参与遗忘、复习与预算淘汰。</summary>
    public bool IsPinned => Importance == MemoryImportance.Pinned;

    /// <summary>供未来落盘/分类扩展使用的附加信息；首版只写来源与压缩条数。</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; }

    internal MemoryEntry(string id, string summary, int createdTurn, DateTimeOffset createdAt,
        int lastReviewTurn, DateTimeOffset lastReviewedAt, double strength, int reviewCount,
        MemoryImportance importance, Dictionary<string, string>? metadata)
    {
        Id = id;
        Summary = summary;
        CreatedTurn = createdTurn;
        CreatedAt = createdAt;
        LastReviewTurn = lastReviewTurn;
        LastReviewedAt = lastReviewedAt;
        Strength = strength;
        ReviewCount = reviewCount;
        Importance = importance;
        Metadata = metadata is { Count: > 0 } ? new Dictionary<string, string>(metadata) : null;
    }

    /// <summary>应用一次复习：强度已由调用方计算并限制上限，这里只同步时钟与计数。</summary>
    internal void ApplyReview(int turn, DateTimeOffset now, double newStrength)
    {
        if (turn > LastReviewTurn) LastReviewTurn = turn;
        // 非法时间（default）不回写，避免把复习时间倒退到世纪初。
        if (now != default && now >= LastReviewedAt) LastReviewedAt = now;
        if (double.IsFinite(newStrength) && newStrength > 0) Strength = newStrength;
        if (ReviewCount < int.MaxValue) ReviewCount++;
    }

    /// <summary>配置上限变化时收敛强度；非法强度按 0 处理，下一次复习会重新抬高。</summary>
    internal void ClampStrength(double maxStrength)
    {
        if (!double.IsFinite(maxStrength) || maxStrength <= 0) return;
        if (!double.IsFinite(Strength) || Strength <= 0) Strength = 0;
        else if (Strength > maxStrength) Strength = maxStrength;
    }

    internal void SetImportance(MemoryImportance importance) => Importance = importance;
}