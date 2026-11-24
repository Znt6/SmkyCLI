namespace SmkyCLI;

/// <summary>
/// 一条记忆提取结果：摘要正文 + 模型给出的语义分类。
/// 分类只映射到 Normal/Preference/Commitment，Pinned 永远只能显式指定，
/// 避免模型把普通记忆误判为永不淘汰。
/// </summary>
public sealed class MemoryExtraction
{
    public string Summary { get; }
    public MemoryImportance Importance { get; }

    public MemoryExtraction(string summary, MemoryImportance importance)
    {
        Summary = summary;
        Importance = importance;
    }

    /// <summary>
    /// 解析模型输出，期望为 {"memories":[{"category":"normal|preference|commitment","summary":"..."}, ...]}。
    /// 每条记忆独立分类、独立登记，各自按遗忘曲线计龄。
    /// 容忍 Markdown 代码围栏、裸数组与单对象输出；JSON 解析失败时把原文当一条普通摘要回退，避免内容丢失；
    /// 解析成功但无有效条目时返回空列表，由调用方决定如何处理。
    /// </summary>
    public static List<MemoryExtraction> ParseMany(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<MemoryExtraction>();
        string text = StripCodeFence(raw.Trim());

        List<MemoryExtraction> result = new List<MemoryExtraction>();
        try
        {
            if (text.StartsWith("[", StringComparison.Ordinal))
            {
                List<MemoryExtractionDto>? dtos = CharacterJson.Deserialize<List<MemoryExtractionDto>>(text);
                if (dtos is null) return Fallback(text);
                foreach (MemoryExtractionDto dto in dtos) AddIfValid(dto, result);
            }
            else
            {
                MemoryExtractionBatch? batch = CharacterJson.Deserialize<MemoryExtractionBatch>(text);
                if (batch?.Memories is not null)
                {
                    foreach (MemoryExtractionDto dto in batch.Memories) AddIfValid(dto, result);
                }
                else
                {
                    AddIfValid(CharacterJson.Deserialize<MemoryExtractionDto>(text), result);
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return Fallback(text);
        }
        return result;
    }

    private static void AddIfValid(MemoryExtractionDto? dto, List<MemoryExtraction> result)
    {
        if (dto is null || string.IsNullOrWhiteSpace(dto.Summary)) return;
        MemoryImportance importance = dto.Category?.Trim().ToLowerInvariant() switch
        {
            "preference" => MemoryImportance.Preference,
            "commitment" => MemoryImportance.Commitment,
            _ => MemoryImportance.Normal
        };
        result.Add(new MemoryExtraction(dto.Summary, importance));
    }

    private static List<MemoryExtraction> Fallback(string text) =>
        new() { new MemoryExtraction(text, MemoryImportance.Normal) };

    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal)) return text;
        int firstNewLine = text.IndexOf('\n');
        if (firstNewLine < 0) return text;
        text = text[(firstNewLine + 1)..];
        int closing = text.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? text[..closing].Trim() : text.Trim();
    }
}

/// <summary>记忆提取的 JSON 输出 DTO，字段与提示词约定一致。</summary>
public sealed class MemoryExtractionDto
{
    public string? Category { get; set; }
    public string? Summary { get; set; }
}

/// <summary>记忆提取的 JSON 根对象：json_object 模式只保证对象不保证数组，故用 memories 包一层。</summary>
public sealed class MemoryExtractionBatch
{
    public List<MemoryExtractionDto>? Memories { get; set; }
}
