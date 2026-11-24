namespace SmkyCLI;

/// <summary>
/// 艾宾浩斯长期记忆参数快照。构造后不可变，运行时配置变化通过
/// <see cref="EbbinghausMemory.ApplyOptions"/> 整体替换。
/// </summary>
public sealed class MemoryOptions
{
    /// <summary>保持率量化层数：仅用于稳定分桶、淘汰排序与提示词重建判定，不做存储合并。</summary>
    public const int RetentionTierCount = 4;

    /// <summary>每逻辑轮写入历史的条数：一对 user/assistant。</summary>
    private const int TurnCostPerRound = 2;

    /// <summary>是否启用长期记忆。</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>产生记忆的上下文上限（条），用于推导强度上限与默认记忆条数。</summary>
    public int MaxHistoryMessages { get; init; } = 40;

    /// <summary>长期记忆条数上限；0 表示按 MaxHistoryMessages 推导。</summary>
    public int MaxEntries { get; init; }

    /// <summary>初始强度 S0（逻辑轮）；合法配置原样生效，登记时只按 S* 截断，不再被内部下限抬高。</summary>
    public double InitialStrength { get; init; } = 2.0;

    /// <summary>复习触发保持率 r_rev：维护时 R 低于该值且未用尽巩固次数才复习。</summary>
    public double ReviewRetention { get; init; } = 0.85;

    /// <summary>巩固系数 alpha。</summary>
    public double ReinforcementFactor { get; init; } = 6.67;

    /// <summary>遗忘阈值 theta：R 低于该值硬淘汰。</summary>
    public double ForgettingThreshold { get; init; } = 0.10;

    /// <summary>普通记忆的最大巩固次数；0 表示无上限。达到后停止复习，按曲线自然遗忘。</summary>
    public int MaxReviewsNormal { get; init; } = 3;

    /// <summary>用户偏好的最大巩固次数；0 表示无上限。</summary>
    public int MaxReviewsPreference { get; init; } = 6;

    /// <summary>关系承诺/未完成事项的最大巩固次数；0 表示无上限。</summary>
    public int MaxReviewsCommitment { get; init; } = 12;

    /// <summary>
    /// 强度上限 S* = M / [T(1-θ)]（T 为每轮消息条数）：由稳态占用 U∞ = T·S(1-θ) ≤ M 推出。
    /// θ=0.1 时约为 0.556M；θ 越大上限越高。MaxHistoryMessages 为 0 时不承载记忆。
    /// θ 的防御性回退与 <see cref="Normalize"/> 一致，避免直接使用未归一化的实例时产生 NaN/∞。
    /// </summary>
    public double MaxStrength
    {
        get
        {
            if (MaxHistoryMessages <= 0) return 0;
            double theta = double.IsFinite(ForgettingThreshold) && ForgettingThreshold > 0 && ForgettingThreshold < 1
                ? ForgettingThreshold
                : 0.10;
            return MaxHistoryMessages / (TurnCostPerRound * (1 - theta));
        }
    }

    /// <summary>
    /// 设计公比 m = 1 + alpha * (1 - r_rev)，即连续时间下每次复习的强度倍率。
    /// 整数轮下实际触发保持率低于 r_rev，增益需按 <see cref="EbbinghausMemory.ComputeReviewFloorRetention"/> 重新校准。
    /// </summary>
    public double ReinforcementRatio => 1 + ReinforcementFactor * (1 - ReviewRetention);

    /// <summary>按重要性取巩固次数上限；Pinned 不参与复习，返回 0。</summary>
    public int MaxReviewsFor(MemoryImportance importance) => importance switch
    {
        MemoryImportance.Normal => MaxReviewsNormal,
        MemoryImportance.Preference => MaxReviewsPreference,
        MemoryImportance.Commitment => MaxReviewsCommitment,
        _ => 0
    };

    /// <summary>由应用配置构建参数，并做一次防御性归一化。</summary>
    public static MemoryOptions FromConfig(SmkyConfig config) => new MemoryOptions
    {
        Enabled = config.EnableEbbinghausMemory,
        MaxHistoryMessages = config.MaxHistoryMessages,
        MaxEntries = config.MaxMemoryEntries,
        InitialStrength = config.MemoryInitialStrength,
        ReviewRetention = config.MemoryReviewRetention,
        ReinforcementFactor = config.MemoryReinforcementFactor,
        ForgettingThreshold = config.MemoryForgettingThreshold,
        MaxReviewsNormal = config.MemoryMaxReviewsNormal,
        MaxReviewsPreference = config.MemoryMaxReviewsPreference,
        MaxReviewsCommitment = config.MemoryMaxReviewsCommitment
    }.Normalize();

    /// <summary>
    /// 把非法或越界参数收敛到安全值：非有限浮点、非正强度、阈值关系倒挂、
    /// 负数上限等都不会进入计算路径。合法配置原样保留。
    /// </summary>
    public MemoryOptions Normalize()
    {
        int maxHistory = Math.Max(0, MaxHistoryMessages);

        double threshold = double.IsFinite(ForgettingThreshold) && ForgettingThreshold > 0 && ForgettingThreshold < 1
            ? ForgettingThreshold
            : 0.10;

        double review;
        if (double.IsFinite(ReviewRetention) && ReviewRetention > threshold && ReviewRetention < 1)
        {
            // 合法复习保持率原样保留。
            review = ReviewRetention;
        }
        else
        {
            // 默认 0.85；theta 接近 1 时取 theta 的下一个可表示值，保证 review 严格位于 (theta,1)。
            double upperBound = Math.BitDecrement(1.0);
            review = Math.Max(0.85, Math.BitIncrement(threshold));
            if (!(review > threshold) || !(review < 1))
            {
                // double 精度下 (theta,1) 已没有可表示值时，仅将 theta 下调一个 ULP，避免无解。
                threshold = Math.BitDecrement(upperBound);
                review = upperBound;
            }
        }
        double initial = double.IsFinite(InitialStrength) && InitialStrength > 0 ? InitialStrength : 2.0;
        double alpha = double.IsFinite(ReinforcementFactor) && ReinforcementFactor > 0 ? ReinforcementFactor : 6.67;

        // S0 原样保留：复习已改为每轮注入前触发，且维护先复习后淘汰，
        // 新记忆能活到首次复习点，不再需要用压缩节律推导内部下限。
        // 默认记忆条数约取上下文预算的 1/8，小预算至少 2 条，最大 32 条。
        int derivedEntries = maxHistory > 0 ? Math.Clamp(maxHistory / 8, 2, 32) : 0;
        int maxEntries = MaxEntries > 0 ? MaxEntries : derivedEntries;
        if (maxHistory <= 0) maxEntries = 0;

        return new MemoryOptions
        {
            Enabled = Enabled,
            MaxHistoryMessages = maxHistory,
            MaxEntries = maxEntries,
            InitialStrength = initial,
            ReviewRetention = review,
            ReinforcementFactor = alpha,
            ForgettingThreshold = threshold,
            MaxReviewsNormal = Math.Max(0, MaxReviewsNormal),
            MaxReviewsPreference = Math.Max(0, MaxReviewsPreference),
            MaxReviewsCommitment = Math.Max(0, MaxReviewsCommitment)
        };
    }
}