using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmkyCLI;

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
    /// 加载应用配置（smkyconfig.json）：缺失或 APIKey 仍为占位符时视为未初始化，
    /// 就地引导用户交互式填写并回写文件；无论配置来自文件还是引导，最终都统一执行有效性校验，
    /// 解析失败或任一字段无效则返回 null，调用方应退出并等待用户修好配置后重新运行。
    /// </summary>
    public static SmkyConfig? LoadOrCreateAppConfig(string path)
    {
        SmkyConfig? config;

        if (!File.Exists(path))
        {
            config = new SmkyConfig
            {
                BaseURL = "https://cn-beijing.maas.qianwenaiapi.com/compatible-mode/v1",
                APIKey = ApiKeyPlaceholder,
                Model = "qwen-flash-character"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(config, IndentedOptions.GetTypeInfo(typeof(SmkyConfig))));
            Tool.PrintColorLine($"smkyconfig配置文件不存在，已从默认模板创建，位置为：{Path.GetFullPath(path)}", Tool.Info);

            // 缺失即视为未初始化：就地引导用户交互式填写，成功后覆盖模板
            if (!GuideInitConfig(config)) return null;
            File.WriteAllText(path, JsonSerializer.Serialize(config, IndentedOptions.GetTypeInfo(typeof(SmkyConfig))));
        }
        else
        {
            try
            {
                config = SmkyConfig.FromJson(File.ReadAllText(path));
            }
            catch (JsonException ex)
            {
                Tool.PrintColorLine($"smkyconfig解析失败：{ex.Message}", Tool.Error);
                return null;
            }

            if (config is null)
            {
                Tool.PrintColorLine($"smkyconfig内容为空：{Path.GetFullPath(path)}", Tool.Error);
                return null;
            }

            // APIKey 缺省或仍是占位符即视为未初始化：引导用户交互式填写，成功后回写文件
            if (string.IsNullOrWhiteSpace(config.APIKey) || config.APIKey == ApiKeyPlaceholder)
            {
                if (!GuideInitConfig(config)) return null;
                File.WriteAllText(path, JsonSerializer.Serialize(config, IndentedOptions.GetTypeInfo(typeof(SmkyConfig))));
            }
        }

        // 无论来自文件还是引导初始化，都统一执行以下有效性校验
        if (String.IsNullOrEmpty(config.BaseURL))
        {
            Console.WriteLine($"配置文件中的BaseURL似乎无效 base_url：{Path.GetFullPath(path)}");
            if (!GuideInitConfig(config)) return null;
        }
        
        if (String.IsNullOrEmpty(config.Model))
        {
            Console.WriteLine($"配置文件中的模型名称似乎无效 model：{Path.GetFullPath(path)}");
            return null;
        }

        if (config.LogitBias is null)
        {
            config.LogitBias = new Dictionary<string, int>();
            Console.WriteLine($"logit_bias不可为null，已初始化为默认值 logit_bias：{Path.GetFullPath(path)}");
        }

        if (!double.IsFinite(config.Temperature) || config.Temperature < 0 || config.Temperature > 1)
        {
            Console.WriteLine($"temperature的值似乎无效,请输入位于区间[0.0..1.0]且不为NaN或者INF的值 temperature：{Path.GetFullPath(path)}");
            return null;
        }

        // 长期记忆参数校验：缺省字段自动使用默认值，显式填错则拒绝启动，避免 NaN/越界进入衰减计算。
        if (!double.IsFinite(config.MemoryInitialStrength) || config.MemoryInitialStrength <= 0)
        {
            Console.WriteLine($"memory_initial_strength 必须为正数且不能是 NaN/INF：{Path.GetFullPath(path)}");
            return null;
        }

        if (!double.IsFinite(config.MemoryForgettingThreshold) ||
            config.MemoryForgettingThreshold <= 0 || config.MemoryForgettingThreshold >= 1)
        {
            Console.WriteLine($"memory_forgetting_threshold 必须位于区间 (0,1) 且不能是 NaN/INF：{Path.GetFullPath(path)}");
            return null;
        }

        if (!double.IsFinite(config.MemoryReviewRetention) ||
            config.MemoryReviewRetention <= config.MemoryForgettingThreshold || config.MemoryReviewRetention >= 1)
        {
            Console.WriteLine($"memory_review_retention 必须位于区间 (memory_forgetting_threshold,1) 且不能是 NaN/INF：{Path.GetFullPath(path)}");
            return null;
        }

        if (!double.IsFinite(config.MemoryReinforcementFactor) || config.MemoryReinforcementFactor <= 0)
        {
            Console.WriteLine($"memory_reinforcement_factor 必须为正数且不能是 NaN/INF：{Path.GetFullPath(path)}");
            return null;
        }

        if (config.MaxMemoryEntries < 0)
        {
            Console.WriteLine($"max_memory_entries 不能为负数：{Path.GetFullPath(path)}");
            return null;
        }

        if (config.MemoryMaxReviewsNormal < 0 || config.MemoryMaxReviewsPreference < 0 ||
            config.MemoryMaxReviewsCommitment < 0)
        {
            Console.WriteLine($"memory_max_reviews_normal/preference/commitment 不能为负数（0 表示无上限）：{Path.GetFullPath(path)}");
            return null;
        }
        
        return config;
    }

    /// <summary>
    /// 交互式引导用户完成初始化：依次输入 BaseURL / APIKey / Model（空输入会重试），
    /// 再用一次最小请求测试模型是否可用。测试通过返回 true，否则返回 false（调用方应放弃本次初始化）。
    /// </summary>
    private static bool GuideInitConfig(SmkyConfig config)
    {
        Tool.PrintColorLine("smky -> 你好，感谢你的使用，不过配置文件好像还没初始化捏", Tool.Smky);
        Tool.PrintColorLine("smky -> 不过不用担心，我们先来完成基础的初始化吧", Tool.Smky);
        Tool.PrintColorLine("smky -> 现在请你输入模型提供商的OpenAI API兼容的URL", Tool.Smky);
        while (true)
        {
            string? url = Console.ReadLine();
            if (!string.IsNullOrEmpty(url))
            {
                config.BaseURL = url;
                Tool.PrintColorLine("smky -> 真棒，接下来就是你的API Key了哦，这个一定要好好保管", Tool.Smky);
                break;
            }
            Tool.PrintColorLine("smky -> 哎呀，好像输入了滚木哎，我这读不到，要不再试试看？", Tool.Smky);
        }

        while (true)
        {
            string? apiKey = Console.ReadLine();
            if (!string.IsNullOrEmpty(apiKey))
            {
                config.APIKey = apiKey;
                Tool.PrintColorLine("smky -> 好的，最难的部分马上就要完成了，再坚持一下，接下来是模型的名字哦", Tool.Smky);
                break;
            }
            Tool.PrintColorLine("smky -> 哎呀，好像输入了  哎，我这读不到，要不再试试看？", Tool.Smky);
        }

        while (true)
        {
            string? model = Console.ReadLine();
            if (!string.IsNullOrEmpty(model))
            {
                config.Model = model;
                Tool.PrintColorLine("smky -> 真棒，现在让我们来试试看模型能不能用，如果长时间一直卡着的话请关掉smky再打开试试看，通常不会超过30秒哦", Tool.Smky);
                break;
            }
            Tool.PrintColorLine("smky -> 哎呀，好像输入了滚木容哎，我这读不到，要不再试试看？", Tool.Smky);
        }

        // 用最小请求测试用户刚输入的模型配置是否可用
        if (TestModelConfigAsync(config).Result)
        {
            Tool.PrintColorLine("smky -> 太棒啦，模型配置测试通过，初始化完成", Tool.Smky);
            return true;
        }

        Tool.PrintColorLine("smky -> 模型配置暂不可用，请检查URL、APIKey和模型名后重新运行再试试，接下来按下回车smky应该就会关掉了", Tool.Smky);
        Console.ReadLine();
        return false;
    }

    /// <summary>以当前配置发起一次最小的非流式对话请求：HTTP 成功且能取到首条回复文本即视为可用。</summary>
    private static async Task<bool> TestModelConfigAsync(SmkyConfig config)
    {
        ChatRequest request = new()
        {
            Model = config.Model,
            Messages = [ChatMessage.User("你好呀")],
            MaxTokens = 512,
            Temperature = 0.2
        };
        try
        {
            using HttpClient http = new()
            {
                BaseAddress = new Uri(config.BaseURL.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.APIKey);
            using StringContent body = new(request.ToJson(), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync("chat/completions", body);
            string content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                Tool.PrintColorLine($"smky -> 哎呀，失败了，不过不用担心，虽然这条消息是红红的，你可以上网搜索下接下来出现的可能很复杂的内容，也可以去询问下ai\n {(int)response.StatusCode}：{content}", Tool.Error);
                return false;
            }

            ChatResponse? result = ChatResponse.FromJson(content);
            if (result?.FirstText is null)
            {
                Tool.PrintColorLine("smky -> 哎呀，失败了，不过也有可能是模型有点调皮没返回任何内容或者返回结构不太对呢", Tool.Error);
                return false;
            }

            Tool.PrintColorLine("smky -> 太棒了，成功了！", Tool.Smky);
            return true;
        }
        catch (Exception e)
        {
            Tool.PrintColorLine($"smky -> 哎呀，遇到了棘手的问题呢，如果你自己不会处理的话可以把接下来那串看着很复杂的内容发给别人或者AI吗吗，我想AI或者别人可能有办法\nGithub的链接是:https://github.com/Znt6/SmkyCLI\n{e}", Tool.Error);
            return false;
        }
    }

    /// <summary>
    /// 加载角色配置（characterconfig.json）：缺失时生成带默认人设的模板并直接沿用（无需密钥，开箱即用）；
    /// 解析失败返回 null。安全规则内置于代码、不进配置文件，无法被覆盖。
    /// </summary>
    public static CharacterConfig? LoadOrCreateCharacterConfig(string path)
    {
        if (!File.Exists(path))
        {
            CharacterConfig template = new CharacterConfig { Profile = DefaultProfile };
            File.WriteAllText(path, JsonSerializer.Serialize(template, IndentedOptions.GetTypeInfo(typeof(CharacterConfig))));
            Console.WriteLine($"角色配置文件不存在，已生成默认模板：{Path.GetFullPath(path)}，可随时编辑自定义角色");
            GuideInitCharacter(template);
            File.WriteAllText(path, JsonSerializer.Serialize(template, IndentedOptions.GetTypeInfo(typeof(CharacterConfig))));
            return template;
        }

        try
        {
            CharacterConfig? character = CharacterConfig.FromJson(File.ReadAllText(path));
            if (character is null)
            {
                Console.WriteLine($"角色配置文件内容为空：{Path.GetFullPath(path)}");
            }
            return character;
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"角色配置解析失败：{ex.Message}");
            return null;
        }
    }

    private static void GuideInitCharacter(CharacterConfig config)
    {
        Tool.PrintColorLine("smky -> 看来角色也还没定义呢，不过是可不是什么角色，我是smky", Tool.Smky);
        Tool.PrintColorLine("smky -> 好了，告诉我你希望你定义的角色叫什么吧，不想给它名字的话什么都不输入就行了", Tool.Smky);
        
        string? name = Console.ReadLine();
        if (!string.IsNullOrEmpty(name)) { config.Name = name; }
        Tool.PrintColorLine("smky -> 好的，接下来你希望这位角色是什么样的呢，不想写的话直接按下回车就是默认了哦", Tool.Smky);
        string? profile = Console.ReadLine();
        if (!string.IsNullOrEmpty(profile)) { config.Profile = profile; }
        Tool.PrintColorLine("smky -> 可以告诉我要怎么称呼你吗，不想说的话直接按回车就好了", Tool.Smky);
        string? userName = Console.ReadLine();
        if (!string.IsNullOrEmpty(userName))
        {
            config.UserName = userName;
            Tool.PrintColorLine($"smky -> 好的，知道了，{userName}", Tool.Smky);
        }
        Tool.PrintColorLine("smky -> 好的，最基础的已经完成了呢，更多设置可在配置文件中修改", Tool.Smky);
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
        LogitBias = config.LogitBias?.Count > 0 ? config.LogitBias : null
    };

    /// <summary>流式打印回复：增量片段到达即输出（打字机效果）；请求异常时打印错误、不中断调用方循环。</summary>
    public static async Task PrintStreamAsync(SmkyChat chat, string input, CancellationToken cancellationToken = default)
    {
        try
        {
            bool any = false;
            await foreach (string delta in chat.SendMessageToModelStreamAsync(input, cancellationToken))
            {
                any = true;
                Console.Write(delta);
            }
            Console.WriteLine(any ? "" : "(未回复)");
        }
        catch (Exception ex) when(!cancellationToken.IsCancellationRequested)
        {
            Tool.PrintColorLine($"失败的请求\n{ex.Message}", Tool.Error);
        }
    }

    /// <summary>非流式打印回复：等待完整回复后一次性输出；请求异常时打印错误、不中断调用方循环。</summary>
    public static async Task PrintNonStreamAsync(SmkyChat chat, string input, CancellationToken cancellationToken = default)
    {
        try
        {
            string? reply = await chat.SendMessageToModelAsync(input, cancellationToken);
            Console.WriteLine(reply ?? "(未回复)");
        }
        catch (Exception ex) when(!cancellationToken.IsCancellationRequested)
        {
            Tool.PrintColorLine($"失败的请求\n{ex.Message}", Tool.Error);
        }
    }
}