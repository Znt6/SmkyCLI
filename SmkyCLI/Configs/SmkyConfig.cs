namespace SmkyCLI;

public class SmkyConfig
{
    /// <summary>兼容OpenAI API的模型提供商</summary>
    public required string BaseURL { get; set; }
    /// <summary>API密钥</summary>
    public required string APIKey { get; set; }
    /// <summary>模型 ID；qwen-flash-character 使用千问角色扩展，其他模型使用标准 OpenAI API 兼容模式</summary>
    public required string Model { get; set; }
    /// <summary>最大生成token数量</summary>
    public int MaxTokens { get; set; } = 1024;
    /// <summary>温度参数</summary>
    public double Temperature { get; set; } = 0.7;
    /// <summary>核采样阈值</summary>
    public double? TopP { get; set; } = null;
    /// <summary>是否启用联网搜索</summary>
    public bool EnableSearch { get; set; } = false;
    /// <summary>上下文缓存达到此消息数后，将最早的一半压缩为摘要；0 表示不携带历史和长期记忆</summary>
    public int MaxHistoryMessages { get; set; } = 40;

    /// <summary>是否启用艾宾浩斯长期记忆：开启后压缩摘要登记为长期记忆（不再插入 _history），
    /// 并自动把记忆快照落盘到可执行文件同目录、启动时只加载记忆；默认关闭，需在配置文件中显式开启</summary>
    public bool EnableEbbinghausMemory { get; set; } = false;
    /// <summary>长期记忆初始强度 S0（单位：逻辑轮）；合法值原样生效，仅登记时按强度上限 S* 截断</summary>
    public double MemoryInitialStrength { get; set; } = 2.0;
    /// <summary>复习触发保持率 r_rev：维护时保持率低于该值且未用尽巩固次数才复习</summary>
    public double MemoryReviewRetention { get; set; } = 0.85;
    /// <summary>巩固系数 alpha：设计公比 m = 1 + alpha * (1 - r_rev)；
    /// 整数轮实际增益按最早可达复习点的保持率校准（见 EbbinghausMemory.ComputeReviewFloorRetention）</summary>
    public double MemoryReinforcementFactor { get; set; } = 6.67;
    /// <summary>遗忘阈值 theta：保持率低于该值硬淘汰</summary>
    public double MemoryForgettingThreshold { get; set; } = 0.10;
    /// <summary>长期记忆条数上限；0 表示由 MaxHistoryMessages 推导（约 M/8，2~32 条）</summary>
    public int MaxMemoryEntries { get; set; } = 0;
    /// <summary>普通记忆的最大巩固次数；0 表示无上限。达到后停止复习，按遗忘曲线自然淘汰</summary>
    public int MemoryMaxReviewsNormal { get; set; } = 3;
    /// <summary>用户偏好的最大巩固次数；0 表示无上限</summary>
    public int MemoryMaxReviewsPreference { get; set; } = 6;
    /// <summary>关系承诺/未完成事项的最大巩固次数；0 表示无上限</summary>
    public int MemoryMaxReviewsCommitment { get; set; } = 12;

    /// <summary>token概率限制</summary>
    public Dictionary<string, int> LogitBias { get; set; } = new Dictionary<string, int>();
    
    /// <summary>序列化为Json</summary>
    public string ToJson() => CharacterJson.Serialize(this);
    /// <summary>从Json反序列化</summary>
    public static SmkyConfig? FromJson(string json) => CharacterJson.Deserialize<SmkyConfig>(json);
}