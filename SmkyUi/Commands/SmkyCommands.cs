using System.CommandLine;
using System.Globalization;

namespace SmkyUi;

/// <summary>REPL 命令运行状态：主循环与命令回调共享（闭包无法捕获 ref 局部变量，改用对象承载）。</summary>
public class CommandState
{
    /// <summary>是否流式输出</summary>
    public bool IsStream { get; set; } = true;
    /// <summary>是否在回复后显示消耗的 token</summary>
    public bool IsShowToken { get; set; }
    /// <summary>退出请求：主循环检测到 true 时结束</summary>
    public bool ExitRequested { get; set; }
}

/// <summary>
/// 基于 System.CommandLine 构建 REPL 命令树：'/' 开头的输入去掉前缀、按空格拆词后交给 root 解析执行。
/// 参数校验（类型、取值范围）与帮助文本均由库自动处理。
/// </summary>
public static class SmkyCommands
{
    public static RootCommand BuildRoot(SmkyConfig config, SmkyChat chat, CommandState state)
    {
        var root = new RootCommand("命令帮助");

        var exit = new Command("exit", "退出程序");
        exit.SetAction(_ => state.ExitRequested = true);
        root.Add(exit);

        // 复用库内置的帮助输出（--help）
        var help = new Command("help", "显示帮助");
        help.SetAction(_ => root.Parse(["--help"]).Invoke());
        root.Add(help);

        // bool 参数默认允许缺省，显式要求必须传 true/false
        var streamArg = new Argument<bool>("value") { Description = "true 或 false", Arity = ArgumentArity.ExactlyOne };
        var setStream = new Command("set-stream", "设置是否流式输出");
        setStream.Add(streamArg);
        setStream.SetAction(r => state.IsStream = r.GetValue(streamArg));
        root.Add(setStream);

        var showTokenArg = new Argument<bool>("value") { Description = "true 或 false", Arity = ArgumentArity.ExactlyOne };
        var setShowToken = new Command("set-show-token", "设置是否显示消耗的token");
        setShowToken.Add(showTokenArg);
        setShowToken.SetAction(r => state.IsShowToken = r.GetValue(showTokenArg));
        root.Add(setShowToken);

        var temperatureArg = new Argument<double>("value") { Description = "0.0-1.0" };
        temperatureArg.Validators.Add(r =>
        {
            // 先 TryParse 再校验范围：类型/缺参错误由库自行报告，避免 GetValueOrDefault 在转换失败时抛异常
            if (r.Tokens.Count > 0 &&
                double.TryParse(r.Tokens[0].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
                v is < 0.0 or > 1.0)
                r.AddError("温度必须在 0.0 到 1.0 之间");
        });
        var setTemperature = new Command("set-temperature", "临时设置模型温度");
        setTemperature.Add(temperatureArg);
        setTemperature.SetAction(r => config.Temperature = r.GetValue(temperatureArg));
        root.Add(setTemperature);

        var clearContext = new Command("clear-context", "清除上下文关系(缓存会失效)");
        clearContext.SetAction(_ => chat.ClearContext());
        root.Add(clearContext);

        var clearConsole = new Command("clear-console", "清空控制台");
        clearConsole.SetAction(_ => Console.Clear());
        root.Add(clearConsole);

        var clear = new Command("clear", "清除缓存和清空控制台");
        clear.SetAction(_ =>
        {
            chat.ClearContext();
            Console.Clear();
        });
        root.Add(clear);

        var maxTokenArg = new Argument<int>("value") { Description = "最大生成token数" };
        maxTokenArg.Validators.Add(r =>
        {
            if (r.Tokens.Count > 0 && int.TryParse(r.Tokens[0].Value, out var v) && v <= 0)
                r.AddError("最大token数必须为正整数");
        });
        var setMaxToken = new Command("set-max-token", "临时设置最大token数");
        setMaxToken.Add(maxTokenArg);
        setMaxToken.SetAction(r => config.MaxTokens = r.GetValue(maxTokenArg));
        root.Add(setMaxToken);

        var maxHistoryArg = new Argument<int>("value") { Description = "最大上下文消息数" };
        maxHistoryArg.Validators.Add(r =>
        {
            if (r.Tokens.Count > 0 && int.TryParse(r.Tokens[0].Value, out var v) && v < 0)
                r.AddError("最大上下文不能为负数");
        });
        var setMaxHistory = new Command("set-max-history-messages", "临时设置最大上下文");
        setMaxHistory.Add(maxHistoryArg);
        setMaxHistory.SetAction(r => config.MaxHistoryMessages = r.GetValue(maxHistoryArg));
        root.Add(setMaxHistory);

        var sixSeven = new Command("six-seven", "6767676767676767");
        sixSeven.SetAction(_ =>
        {
            // TODO: 67 先空着
        });
        root.Add(sixSeven);
        
        var enableSearchArg = new Argument<bool>("value") { Description = "true 或 false", Arity = ArgumentArity.ExactlyOne };
        var setEnableSearch = new Command("set-enable-search", "临时设置是否启用搜索(会产生额外计费");
        setEnableSearch.Add(enableSearchArg);
        setEnableSearch.SetAction(r => config.EnableSearch = r.GetValue(enableSearchArg));
        root.Add(setEnableSearch);
        

        return root;
    }
}