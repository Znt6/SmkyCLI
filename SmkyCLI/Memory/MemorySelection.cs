using System.Text;

namespace SmkyCLI;

/// <summary>
/// 一次检索/注入的结果：条目已按稳定的创建时间顺序排列，并附带由
/// “Id + 重要性 + 量化保持率层”组成的签名。调用方可按签名缓存提示词，
/// 避免每轮因浮点保持率变化而重写历史前缀。
/// </summary>
public sealed class MemorySelection
{
    public static MemorySelection Empty { get; } = new(Array.Empty<MemoryEntry>(),
        "empty", new Dictionary<string, int>());

    public IReadOnlyList<MemoryEntry> Entries { get; }

    /// <summary>仅含离散字段的稳定签名；保持率只以量化层号参与。</summary>
    public string Signature { get; }

    /// <summary>本次注入时各条目的量化保持率层（0 最新，TierCount-1 最旧）。</summary>
    public IReadOnlyDictionary<string, int> RetentionTiers { get; }

    public bool IsEmpty => Entries.Count == 0;

    internal MemorySelection(IReadOnlyList<MemoryEntry> entries, string signature,
        IReadOnlyDictionary<string, int> retentionTiers)
    {
        Entries = entries;
        Signature = signature;
        RetentionTiers = retentionTiers;
    }

    /// <summary>渲染为一条 system 消息正文；顺序固定，不包含任何浮点或每轮变化的数值。</summary>
    public string ToSystemContent()
    {
        if (IsEmpty) return string.Empty;

        StringBuilder builder = new StringBuilder();
        builder.AppendLine("【长期记忆】（按时间顺序）");
        foreach (MemoryEntry entry in Entries)
        {
            builder.Append(entry.IsPinned ? "- （固定）" : "- ");
            builder.AppendLine(entry.Summary.Replace("\r\n", "\n").Trim());
        }
        return builder.ToString().TrimEnd();
    }
}