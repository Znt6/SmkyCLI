using System.CommandLine;
using System.Net.Http.Headers;
using System.Text;
namespace SmkyCLI;

class Program
{
    private const string ConfigPath = "smkyconfig.json";
    private const string CharacterConfigPath = "characterconfig.json";

    static async Task Main(string[] args)
    {
        SmkyConfig? config = SmkyUtils.LoadOrCreateAppConfig(ConfigPath);
        if (config is null) return;

        CharacterConfig? character = SmkyUtils.LoadOrCreateCharacterConfig(CharacterConfigPath);
        if (character is null) return;

        // 请求模板
        ChatRequest requestTemplate = SmkyUtils.CreateRequestTemplate(config);

        using SmkyChat chat = new SmkyChat(config, character, requestTemplate);

        //System.CommandLine解析执行
        CommandState state = new CommandState();
        RootCommand root = SmkyCommands.BuildRoot(config, chat, state);

        Console.WriteLine($"加载配置 -> {Path.GetFullPath(ConfigPath)}（模型：{config.Model}）");
        Console.WriteLine($"加载角色 -> {Path.GetFullPath(CharacterConfigPath)}");
        if (config.Model != SmkyChat.QwenFlashCharacterModel)
        {
            Tool.PrintColorLine("当前为OpenAiApi兼容模式不支持联网搜索和真实时间", Tool.Warning);
        }
        Console.WriteLine("输入/exit退出\n按下Ctrl+C(苹果用户为Cmd+C)退出");

        // 主循环
        while (!state.ExitRequested)
        {
            Tool.PrintColor($"\n{(string.IsNullOrEmpty(character.UserName) ? "user" : character.UserName)} -> ", Tool.UserPrompt);
            string? input = Console.ReadLine();
            if (input is null) break;

            if (input.Length == 0) continue;

            #region 命令处理

            if (input[0] == '/')
            {
                string[] tokens = input[1..].Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0) await root.Parse(tokens).InvokeAsync();
                continue;
            }

            #endregion

            input = input.Trim();
            if (input.Length == 0) continue;

            #region AI输出

            // 取消令牌
            using CancellationTokenSource cts = new();
            ConsoleCancelEventHandler handler = (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += handler;
            
            try
            {
                Tool.PrintColor($"{(String.IsNullOrEmpty(character.Name) ? "ai" : character.Name)}: ",
                    Tool.AiName);
                // ai输出
                if (state.IsStream) await SmkyUtils.PrintStreamAsync(chat, input, cts.Token);
                else await SmkyUtils.PrintNonStreamAsync(chat, input, cts.Token);

                // token用量输出
                if (state.IsShowToken && chat.LastUsage is { } usage)
                    Tool.PrintColorLine($"[总计: {usage.TotalTokens}] >> [输入: {usage.PromptTokens}] >> " +
                                        $"[输出: {usage.CompletionTokens}] >> " +
                                        $"[命中: {usage.PromptTokensDetails?.CachedTokens.ToString() ?? "服务端未返回"}]"
                        , Tool.Muted);
            }
            catch (Exception e) when (cts.IsCancellationRequested)
            {
                Tool.PrintColorLine("被用户取消的单次对话", Tool.Info);
            }
            finally
            {
                Console.CancelKeyPress -= handler;
            }

            #endregion
        }

        // 退出前把复习/淘汰后的最新记忆状态落盘（未启用记忆时为空操作）。
        chat.SaveMemory();
    }

    /// <summary>以当前配置发起一次最小的非流式对话请求：HTTP 成功且能取到首条回复文本即视为可用。</summary>
    private static async Task<bool> TestModelConfigAsync(SmkyConfig config)
    {
        ChatRequest request = new()
        {
            Model = config.Model,
            Messages = [ChatMessage.User("你好呀")],
            MaxTokens = 8,
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
                Tool.PrintColorLine($"smky -> 哎呀，失败了，不过不用担心，虽然这条消息是红红的，你可以上网搜索下接下来出现的可能很负责的内容，也可以去询问下ai\n {(int)response.StatusCode}：{content}", Tool.Error);
                return false;
            }

            ChatResponse? result = ChatResponse.FromJson(content);
            if (result?.FirstText is null)
            {
                Tool.PrintColorLine("smky -> 哎呀，失败了，不过也有可能是模型有点调皮没返回任何内容或者返回结构不太对呢", Tool.Error);
                return false;
            }

            Tool.PrintColorLine($"smky -> 太棒了，成功了！", Tool.Success);
            return true;
        }
        catch (Exception ex)
        {
            Tool.PrintColorLine($"smky -> 哎呀，遇到了棘手的问题呢，可以把接下来那串看着很复杂的内容发给Znt6吗，我想他或者别人可能有办法\nGithub的链接是:https://github.com/Znt6/SmkyCLI", Tool.Error);
            return false;
        }
    }
}