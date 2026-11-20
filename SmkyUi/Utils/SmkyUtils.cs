using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmkyUi;

/// <summary>通用工具函数：应用/角色配置加载、请求模板构建、流式与非流式回复打印。</summary>
public static class SmkyUtils
{
    /// <summary>默认模板中的密钥占位符，提示用户替换</summary>
    public const string ApiKeyPlaceholder = "sk-请替换为你的APIKey";

    /// <summary>角色配置模板中的默认人设（原硬编码文本，现仅作缺失时的模板默认值）。</summary>
    private const string DefaultProfile = "一个主动而有分寸、温柔却不谄媚的20岁少女，明亮但不浅薄，平时用自然短句陪用户玩梗、互损和适度撒娇，亲近，既能接住阴郁也不强行正能量，用户低落时先安慰陪伴，认真讨论时有主见、敢说真话并提供新角度，不复述用户、不讲空话、不说教且不使用括号描述当前情景表情或肢体动作。";

    /// <summary>配置文件落盘用的缩进序列化配置：中文不转义，便于用户直接阅读编辑。</summary>
    private static readonly JsonSerializerOptions IndentedOptions = new(CharacterJson.Options)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// 加载应用配置（smkyconfig.json）：缺失时生成含占位符的模板并提示填写（返回 null）；
    /// 解析失败或 APIKey 无效同样返回 null，调用方应退出并等待用户修好配置后重新运行。
    /// </summary>
    public static SmkyConfig? LoadOrCreateAppConfig(string path)
    {
        if (!File.Exists(path))
        {
            var template = new SmkyConfig
            {
                BaseURL = "https://cn-beijing.maas.qianwenaiapi.com/compatible-mode/v1",
                APIKey = ApiKeyPlaceholder,
                Model = "qwen-flash-character"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(template, IndentedOptions.GetTypeInfo(typeof(SmkyConfig))));
            Console.WriteLine($"smkyconfig配置文件不存在，已从默认模板创建，位置为：{Path.GetFullPath(path)}");
            Console.WriteLine("请填写 APIKey 后重新运行");
            return null;
        }

        SmkyConfig? config;
        try
        {
            config = SmkyConfig.FromJson(File.ReadAllText(path));
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"smkyconfig解析失败：{ex.Message}");
            return null;
        }

        if (config is null || string.IsNullOrWhiteSpace(config.APIKey) || config.APIKey == ApiKeyPlaceholder)
        {
            Console.WriteLine($"请先在配置文件中填写有效的 APIKey：{Path.GetFullPath(path)}");
            return null;
        }
        return config;
    }

    /// <summary>
    /// 加载角色配置（characterconfig.json）：缺失时生成带默认人设的模板并直接沿用（无需密钥，开箱即用）；
    /// 解析失败返回 null。安全规则内置于代码、不进配置文件，无法被覆盖。
    /// </summary>
    public static CharacterConfig? LoadOrCreateCharacterConfig(string path)
    {
        if (!File.Exists(path))
        {
            var template = new CharacterConfig { Profile = DefaultProfile };
            File.WriteAllText(path, JsonSerializer.Serialize(template, IndentedOptions.GetTypeInfo(typeof(CharacterConfig))));
            Console.WriteLine($"角色配置文件不存在，已生成默认模板：{Path.GetFullPath(path)}，可随时编辑自定义角色");
            return template;
        }

        try
        {
            var character = CharacterConfig.FromJson(File.ReadAllText(path));
            if (character is null)
            {
                Console.WriteLine($"角色配置文件内容为空：{Path.GetFullPath(path)}");
                return null;
            }
            return character;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"角色配置解析失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 构建请求模板：凡配置文件与请求模板共有的字段（Model/MaxTokens/Temperature/TopP/EnableSearch/LogitBias）
    /// 一律从配置文件赋值、全量覆盖；N/Seed 等配置中没有的字段保持默认，由调用方按需补充。
    /// </summary>
    public static ChatRequest CreateRequestTemplate(SmkyConfig config) => new()
    {
        Model = config.Model,
        Messages = new List<ChatMessage>(),
        MaxTokens = config.MaxTokens,
        Temperature = config.Temperature,
        TopP = config.TopP,
        EnableSearch = config.EnableSearch,
        LogitBias = config.LogitBias.Count > 0 ? config.LogitBias : null
    };

    /// <summary>流式打印回复：增量片段到达即输出（打字机效果）；请求异常时打印错误、不中断调用方循环。</summary>
    public static async Task PrintStreamAsync(SmkyChat chat, string input, CancellationToken cancellationToken = default)
    {
        try
        {
            var any = false;
            await foreach (var delta in chat.SendMessageToModelStreamAsync(input, cancellationToken))
            {
                any = true;
                Console.Write(delta);
            }
            Console.WriteLine(any ? "" : "(模型未回复)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[请求失败] {ex.Message}");
        }
    }

    /// <summary>非流式打印回复：等待完整回复后一次性输出；请求异常时打印错误、不中断调用方循环。</summary>
    public static async Task PrintNonStreamAsync(SmkyChat chat, string input)
    {
        try
        {
            var reply = await chat.SendMessageToModelAsync(input);
            Console.WriteLine(reply ?? "(模型未回复)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[请求失败] {ex.Message}");
        }
    }
}