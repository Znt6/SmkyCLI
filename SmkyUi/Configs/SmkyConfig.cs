namespace SmkyUi;

public class SmkyConfig
{
    /// <summary>兼容OpenAI API的模型提供商</summary>
    public required string BaseURL { get; set; }
    /// <summary>API密钥</summary>
    public required string APIKey { get; set; }
    /// <summary>模型 ID；deepseek-flash 使用千问角色扩展，其他模型使用标准 OpenAI API 兼容模式</summary>
    public required string Model { get; set; }
    /// <summary>最大生成token数量</summary>
    public int MaxTokens { get; set; } = 1024;
    /// <summary>温度参数</summary>
    public double Temperature { get; set; } = 0.7;
    /// <summary>核采样阈值</summary>
    public double? TopP { get; set; } = null;
    /// <summary>是否启用联网搜索</summary>
    public bool EnableSearch { get; set; } = false;
    /// <summary>上下文缓存达到此消息数后，将最早的一半压缩为摘要；0 表示不携带历史</summary>
    public int MaxHistoryMessages { get; set; } = 40;
    /// <summary>token概率限制</summary>
    public Dictionary<string, int> LogitBias { get; set; } = new Dictionary<string, int>();
    
    /// <summary>序列化为Json</summary>
    public string ToJson() => CharacterJson.Serialize(this);
    /// <summary>从Json反序列化</summary>
    public static SmkyConfig? FromJson(string json) => CharacterJson.Deserialize<SmkyConfig>(json);
}