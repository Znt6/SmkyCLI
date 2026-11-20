using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SmkyUi;

// ============================================================================
// 千问角色扮演（Qwen-Character）OpenAI 兼容 Chat Completions API 的请求/响应封装
// 文档: https://platform.qianwenai.com/docs/developer-guides/text-generation/role-playing
// 端点: {base_url}/chat/completions （base_url 形如 https://maas.qianwenaiapi.com/compatible-mode/v1）
// ============================================================================

/// <summary>统一的 JSON 序列化入口：snake_case 命名 + 忽略 null，与兼容模式 API 的字段风格一致。</summary>
public static class CharacterJson
{
    /// <summary>全局序列化配置：属性名转 snake_case（如 MaxTokens → max_tokens），null 字段不输出；读取时跳过 // 注释。
    /// 元数据由 <see cref="SmkyJsonContext"/> 源生成提供，NativeAOT 下无需运行时反射。</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        TypeInfoResolver = SmkyJsonContext.Default
    };

    /// <summary>把任意请求/响应对象序列化为 API 兼容的 JSON 字符串。</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options.GetTypeInfo(typeof(T)));

    /// <summary>把 API 返回的 JSON 字符串反序列化为指定类型。</summary>
    public static T? Deserialize<T>(string json) => (T?)JsonSerializer.Deserialize(json, Options.GetTypeInfo(typeof(T)));
}

/// <summary>
/// System.Text.Json 源生成上下文：登记所有参与序列化的类型及其选项，
/// 使 NativeAOT 发布不依赖反射（否则 AOT 下会因缺少元数据在运行时抛 NotSupportedException）。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(SmkyConfig))]
[JsonSerializable(typeof(CharacterConfig))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(ChatStreamChunk))]
[JsonSerializable(typeof(ChatMessage))]
[JsonSerializable(typeof(ContentPart))]
[JsonSerializable(typeof(List<ChatMessage>))]
[JsonSerializable(typeof(List<ContentPart>))]
public partial class SmkyJsonContext : JsonSerializerContext
{
}

// ---------------------------- 请求侧 ----------------------------

/// <summary>Chat Completions 请求体（POST chat/completions）。</summary>
public sealed class ChatRequest
{
    /// <summary>模型名（必填）。Qwen-Flash-Character 使用 deepseek-flash，其他模型填写各自服务商的模型 ID。</summary>
    public required string Model { get; set; }

    /// <summary>对话消息列表（必填）。第一个元素必须是 system 消息（角色设定）；多轮对话按顺序追加历史。</summary>
    public required List<ChatMessage> Messages { get; set; }

    /// <summary>单次请求生成的回复条数，取值 1–4，默认 1。用于"多样化响应"。</summary>
    public int? N { get; set; }

    /// <summary>随机数种子。换 seed 可"重新生成响应"；建议每次只调整 seed/top_p/temperature 中的一个。</summary>
    public int? Seed { get; set; }

    /// <summary>核采样阈值 (0,1]，控制多样性；值越大回复越随机，默认由服务端决定。</summary>
    public double? TopP { get; set; }

    /// <summary>采样温度 [0,2)，控制随机性；值越大回复越发散，默认由服务端决定。</summary>
    public double? Temperature { get; set; }

    /// <summary>本次回复的最大输出 Token 数。qwen-flash-character-2026-02-26 默认 4096，可调至 32768。</summary>
    public int? MaxTokens { get; set; }

    /// <summary>Token 偏置表：Key 为 Token ID（字符串），Value 为 [-100,100] 的概率调整值。-100 表示完全禁止该 Token（如屏蔽括号动作描写）。</summary>
    public Dictionary<string, int>? LogitBias { get; set; }

    /// <summary>是否启用联网搜索，默认 false。启用后模型基于实时检索数据回复（额外计费）。</summary>
    public bool? EnableSearch { get; set; }

    /// <summary>是否启用流式输出（SSE），默认 false。true 时服务端按 data: 块逐步返回 <see cref="ChatStreamChunk"/>。</summary>
    public bool? Stream { get; set; }

    /// <summary>流式输出选项，仅在 stream=true 时生效。</summary>
    public StreamOptions? StreamOptions { get; set; }

    /// <summary>角色扮演专属选项（非 OpenAI 标准参数）：人设与真实时间感知。</summary>
    public CharacterOptions? CharacterOptions { get; set; }

    /// <summary>序列化为可直接作为 HTTP 请求体的 JSON。</summary>
    public string ToJson() => CharacterJson.Serialize(this);

    /// <summary>从 JSON 还原请求对象（便于回放/调试）。</summary>
    public static ChatRequest? FromJson(string json) => CharacterJson.Deserialize<ChatRequest>(json);
}

/// <summary>stream_options 参数：控制流式输出的附加行为。</summary>
public sealed class StreamOptions
{
    /// <summary>是否在最后一个数据块中附带 Token 用量统计（该块 choices 为空数组），默认 false。</summary>
    public bool? IncludeUsage { get; set; }
}

/// <summary>character_options 参数：角色扮演模型的专属能力开关。</summary>
public sealed class CharacterOptions
{
    /// <summary>角色人设文本。启用真实时间功能时，原 system 消息中的人设内容需迁移到这里。</summary>
    public string? Profile { get; set; }

    /// <summary>是否启用真实时间，默认 false。开启后模型可感知当前日期与时间（不支持 Responses API）。</summary>
    public bool? EnableRealtime { get; set; }
}

/// <summary>
/// 一条对话消息。content 支持两种形态：纯文本字符串，或内容块数组（只有数组形态能携带 cache_control）。
/// 序列化/反序列化由 <see cref="ChatMessageConverter"/> 处理两种形态的互转。
/// </summary>
[JsonConverter(typeof(ChatMessageConverter))]
public sealed class ChatMessage
{
    /// <summary>消息角色：system（角色设定/补充信息）、user（用户或其他群聊成员）、assistant（模型扮演的角色）。</summary>
    public string Role { get; set; } = "";

    /// <summary>纯文本内容。与 <see cref="ContentParts"/> 二选一；普通对话直接用这个即可。</summary>
    public string? Content { get; set; }

    /// <summary>内容块数组形态。需要在消息上打 cache_control 显式缓存标记时使用。</summary>
    public List<ContentPart>? ContentParts { get; set; }

    /// <summary>前缀续写标记。true 表示本条 assistant 消息是未完成的开头（如"角色名："），模型会接着往下写；用于群聊和连续响应。</summary>
    public bool? Partial { get; set; }

    /// <summary>创建 system 消息（角色设定；也可在最后一条 user 消息前插入一次性补充信息以提高缓存命中率）。</summary>
    public static ChatMessage System(string text) => new() { Role = "system", Content = text };

    /// <summary>创建 user 消息（用户输入；群聊场景中其他参与者也是 user，内容需以"名字："开头）。</summary>
    public static ChatMessage User(string text) => new() { Role = "user", Content = text };

    /// <summary>创建 assistant 消息（模型回复；也可用作开场白）。</summary>
    public static ChatMessage Assistant(string text) => new() { Role = "assistant", Content = text };

    /// <summary>创建带 partial=true 的 assistant 前缀消息，引导模型从"角色名："之后继续生成。</summary>
    public static ChatMessage AssistantPrefix(string prefix) => new() { Role = "assistant", Content = prefix, Partial = true };

    /// <summary>返回本消息的副本，并在内容块上打 cache_control=ephemeral 标记：从 messages 开头到该消息的全部内容会形成一个显式缓存块。</summary>
    public ChatMessage WithCacheControl()
    {
        var parts = ContentParts ?? [new ContentPart { Text = Content ?? "" }];
        var copy = parts.Select(p => new ContentPart { Type = p.Type, Text = p.Text, CacheControl = p.CacheControl }).ToList();
        copy[^1].CacheControl = new CacheControl();
        return new ChatMessage { Role = Role, ContentParts = copy, Partial = Partial };
    }
}

/// <summary>消息内容块（content 数组的元素）。文本类型才能携带 cache_control。</summary>
public sealed class ContentPart
{
    /// <summary>内容块类型，目前角色扮演场景固定为 "text"。</summary>
    public string Type { get; set; } = "text";

    /// <summary>本块的文本内容。</summary>
    public string? Text { get; set; }

    /// <summary>显式上下文缓存标记；为 null 时不输出该字段。</summary>
    public CacheControl? CacheControl { get; set; }
}

/// <summary>cache_control 字段：标记缓存块的结束位置。</summary>
public sealed class CacheControl
{
    /// <summary>缓存类型，目前仅支持 "ephemeral"（临时缓存，服务端自动管理过期）。</summary>
    public string Type { get; set; } = "ephemeral";
}

/// <summary>处理 ChatMessage 的 content 双形态：写出时优先数组形态，读入时按 JSON 类型自动区分字符串/数组。</summary>
public sealed class ChatMessageConverter : JsonConverter<ChatMessage>
{
    public override ChatMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var msg = new ChatMessage();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string name = reader.GetString()!;
            reader.Read();
            switch (name)
            {
                case "role":
                    msg.Role = reader.GetString() ?? "";
                    break;
                case "content":
                    if (reader.TokenType == JsonTokenType.String)
                        msg.Content = reader.GetString();
                    else if (reader.TokenType == JsonTokenType.StartArray)
                        msg.ContentParts = JsonSerializer.Deserialize(ref reader,
                            (JsonTypeInfo<List<ContentPart>>)options.GetTypeInfo(typeof(List<ContentPart>)));
                    else
                        reader.Skip();
                    break;
                case "partial":
                    msg.Partial = reader.TokenType == JsonTokenType.True;
                    break;
                default:
                    reader.Skip(); // 忽略未建模的字段（refusal、tool_calls 等）
                    break;
            }
        }
        return msg;
    }

    public override void Write(Utf8JsonWriter writer, ChatMessage value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("role", value.Role);
        if (value.ContentParts is not null)
        {
            writer.WritePropertyName("content");
            JsonSerializer.Serialize(writer, value.ContentParts,
                (JsonTypeInfo<List<ContentPart>>)options.GetTypeInfo(typeof(List<ContentPart>)));
        }
        else
        {
            writer.WriteString("content", value.Content ?? "");
        }
        if (value.Partial is not null) writer.WriteBoolean("partial", value.Partial.Value);
        writer.WriteEndObject();
    }
}

// ---------------------------- 响应侧 ----------------------------

/// <summary>Chat Completions 非流式响应体。</summary>
public sealed class ChatResponse
{
    /// <summary>本次请求的唯一标识，形如 chatcmpl-xxxx，排查问题时提供给平台。</summary>
    public string? Id { get; set; }

    /// <summary>对象类型，固定为 "chat.completion"。</summary>
    public string? Object { get; set; }

    /// <summary>响应生成时间（Unix 秒级时间戳）。</summary>
    public long? Created { get; set; }

    /// <summary>实际使用的模型名。</summary>
    public string? Model { get; set; }

    /// <summary>系统指纹，标识服务端配置版本；可能为 null。</summary>
    public string? SystemFingerprint { get; set; }

    /// <summary>服务层级；可能为 null。</summary>
    public string? ServiceTier { get; set; }

    /// <summary>生成的候选回复列表；设置 n&gt;1 时会有多条。</summary>
    public List<Choice>? Choices { get; set; }

    /// <summary>Token 用量统计（含缓存命中信息）。</summary>
    public UsageInfo? Usage { get; set; }

    /// <summary>第一条回复的文本内容；无回复时为 null。</summary>
    public string? FirstText => Choices is { Count: > 0 } ? Choices[0].Message?.Content : null;

    /// <summary>本次请求命中缓存的输入 Token 数（usage.prompt_tokens_details.cached_tokens）；未命中或字段缺失时为 0。</summary>
    public int CachedTokens => Usage?.PromptTokensDetails?.CachedTokens ?? 0;

    /// <summary>从 API 返回的 JSON 反序列化。</summary>
    public static ChatResponse? FromJson(string json) => CharacterJson.Deserialize<ChatResponse>(json);

    /// <summary>序列化为 JSON（便于日志落盘/调试）。</summary>
    public string ToJson() => CharacterJson.Serialize(this);
}

/// <summary>一个候选回复。</summary>
public sealed class Choice
{
    /// <summary>候选序号，从 0 开始，对应请求参数 n 生成的第几条。</summary>
    public int Index { get; set; }

    /// <summary>模型生成的消息本体。</summary>
    public ResponseMessage? Message { get; set; }

    /// <summary>结束原因：stop（正常结束）、length（达到 max_tokens 上限）、null（流式中间块）等。</summary>
    public string? FinishReason { get; set; }

    /// <summary>Token 对数概率信息；未开启 logprobs 时为 null，原样保留 JSON 不做建模。</summary>
    public JsonElement? Logprobs { get; set; }
}

/// <summary>响应中的消息本体（区别于请求消息：content 只会是字符串，且可能带 refusal 等字段）。</summary>
public sealed class ResponseMessage
{
    /// <summary>消息角色，回复固定为 "assistant"。</summary>
    public string? Role { get; set; }

    /// <summary>回复文本内容，即角色说的话（可能含（动作/神情）描写，可用 logit_bias 屏蔽）。</summary>
    public string? Content { get; set; }

    /// <summary>模型拒绝回复时的说明文本；正常回复为 null。</summary>
    public string? Refusal { get; set; }
}

/// <summary>Token 用量统计。</summary>
public sealed class UsageInfo
{
    /// <summary>输入（提示词）消耗的 Token 数，包含被缓存命中的部分。</summary>
    public int PromptTokens { get; set; }

    /// <summary>模型输出消耗的 Token 数。</summary>
    public int CompletionTokens { get; set; }

    /// <summary>输入 + 输出的总 Token 数。</summary>
    public int TotalTokens { get; set; }

    /// <summary>输入 Token 的明细（缓存命中数在这里）；服务端未返回时为 null。</summary>
    public PromptTokensDetails? PromptTokensDetails { get; set; }

    /// <summary>输出 Token 的明细（如推理 Token）；通常为 null，原样保留 JSON 不做建模。</summary>
    public JsonElement? CompletionTokensDetails { get; set; }
}

/// <summary>输入 Token 明细。</summary>
public sealed class PromptTokensDetails
{
    /// <summary>命中上下文缓存的 Token 数：大于 0 说明缓存生效，这部分按缓存价计费且延迟更低。</summary>
    public int CachedTokens { get; set; }
}

// ---------------------------- 流式响应侧 ----------------------------

/// <summary>
/// Chat Completions 流式响应块（SSE 中一条 data: 负载，object 为 "chat.completion.chunk"）。
/// 流以一行 data: [DONE] 结束；启用 stream_options.include_usage 时，最后一个数据块的 choices 为空数组、只带 usage。
/// </summary>
public sealed class ChatStreamChunk
{
    /// <summary>本次请求的唯一标识，同一次流式请求的所有块共享同一个 id。</summary>
    public string? Id { get; set; }

    /// <summary>对象类型，固定为 "chat.completion.chunk"。</summary>
    public string? Object { get; set; }

    /// <summary>响应生成时间（Unix 秒级时间戳），同一次请求的各块相同。</summary>
    public long? Created { get; set; }

    /// <summary>实际使用的模型名。</summary>
    public string? Model { get; set; }

    /// <summary>系统指纹，标识服务端配置版本；可能为 null。</summary>
    public string? SystemFingerprint { get; set; }

    /// <summary>服务层级；可能为 null。</summary>
    public string? ServiceTier { get; set; }

    /// <summary>本块的增量候选列表；用量统计块中为空数组。</summary>
    public List<StreamChoice>? Choices { get; set; }

    /// <summary>Token 用量统计；仅启用 stream_options.include_usage 时出现在最后一块，其余块为 null。</summary>
    public UsageInfo? Usage { get; set; }

    /// <summary>第一条候选的增量文本；本块无文本（如角色块/结束块/用量块）时为 null。</summary>
    public string? FirstDeltaText => Choices is { Count: > 0 } ? Choices[0].Delta?.Content : null;

    /// <summary>第一条候选的结束原因；仍在生成中时为 null。</summary>
    public string? FirstFinishReason => Choices is { Count: > 0 } ? Choices[0].FinishReason : null;

    /// <summary>从一条 data: 负载的 JSON 反序列化。</summary>
    public static ChatStreamChunk? FromJson(string json) => CharacterJson.Deserialize<ChatStreamChunk>(json);

    /// <summary>序列化为 JSON（便于日志落盘/调试）。</summary>
    public string ToJson() => CharacterJson.Serialize(this);
}

/// <summary>流式块中的一个增量候选。</summary>
public sealed class StreamChoice
{
    /// <summary>候选序号，从 0 开始；同一条回复的所有增量块序号相同，据此拼接 n&gt;1 的多条回复。</summary>
    public int Index { get; set; }

    /// <summary>本块的增量消息内容。</summary>
    public StreamDelta? Delta { get; set; }

    /// <summary>结束原因：stop（正常结束）、length（达到 max_tokens 上限）；生成中的块为 null。</summary>
    public string? FinishReason { get; set; }

    /// <summary>Token 对数概率信息；未开启 logprobs 时为 null，原样保留 JSON 不做建模。</summary>
    public JsonElement? Logprobs { get; set; }
}

/// <summary>流式增量消息：首块通常只带 role，后续块只带 content 片段，按序拼接即得完整回复。</summary>
public sealed class StreamDelta
{
    /// <summary>消息角色，仅首块出现（"assistant"），后续块为 null。</summary>
    public string? Role { get; set; }

    /// <summary>本块新增的文本片段；角色块/结束块中为 null 或空串。</summary>
    public string? Content { get; set; }

    /// <summary>模型拒绝回复时的说明文本增量；正常回复为 null。</summary>
    public string? Refusal { get; set; }
}