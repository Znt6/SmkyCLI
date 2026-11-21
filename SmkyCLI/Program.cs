using System.CommandLine;
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
            Tool.PrintColor("\nuser -> ", Tool.UserPrompt);
            string? input = Console.ReadLine();
            if (input is null) break;

            if (input.Length == 0) continue;

            #region 命令处理

            if (input[0] == '/')
            {
                string[] tokens = input[1..].Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length > 0) root.Parse(tokens).Invoke();
                continue;
            }

            #endregion

            input = input.Trim();
            if (input.Length == 0) continue;

            #region AI输出

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
    }
}