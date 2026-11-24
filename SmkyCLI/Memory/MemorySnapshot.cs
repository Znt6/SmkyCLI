using System.Text.Json;

namespace SmkyCLI;

/// <summary>
/// 长期记忆的版本化快照。启用记忆后由 <see cref="MemoryStore"/> 落盘到可执行文件同目录，
/// 并在启动时只加载记忆；不包含任何聊天原文或密钥。SchemaVersion 用于未来迁移。
/// </summary>
public sealed class MemorySnapshot
{
    /// <summary>当前快照结构版本。</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>快照时刻的逻辑轮次时钟。</summary>
    public int LogicalClock { get; set; }

    /// <summary>快照时刻的墙钟时间（仅用于记录导出时间，不参与记忆年龄计算）。</summary>
    public DateTimeOffset SavedAt { get; set; }

    public List<MemorySnapshotEntry> Entries { get; set; } = new();

    /// <summary>序列化为 JSON（snake_case，与项目其他配置一致）。</summary>
    public string ToJson() => CharacterJson.Serialize(this);

    /// <summary>从 JSON 还原；格式错误或缺少源生成元数据时返回 null，不抛出。</summary>
    public static MemorySnapshot? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return CharacterJson.Deserialize<MemorySnapshot>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>快照中的单条记忆：字段与运行期回忆完全对应，保证无损往返。</summary>
public sealed class MemorySnapshotEntry
{
    public string Id { get; set; } = "";
    public string Summary { get; set; } = "";
    public int CreatedTurn { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int LastReviewTurn { get; set; }
    public DateTimeOffset LastReviewedAt { get; set; }
    public double Strength { get; set; }
    public int ReviewCount { get; set; }
    public MemoryImportance Importance { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>从运行期条目生成快照 DTO。</summary>
    internal static MemorySnapshotEntry FromEntry(MemoryEntry entry) => new()
    {
        Id = entry.Id,
        Summary = entry.Summary,
        CreatedTurn = entry.CreatedTurn,
        CreatedAt = entry.CreatedAt,
        LastReviewTurn = entry.LastReviewTurn,
        LastReviewedAt = entry.LastReviewedAt,
        Strength = entry.Strength,
        ReviewCount = entry.ReviewCount,
        Importance = entry.Importance,
        Metadata = entry.Metadata is { Count: > 0 } ? new Dictionary<string, string>(entry.Metadata) : null
    };
}