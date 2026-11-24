using System.Drawing;
using System.Runtime.InteropServices;

namespace SmkyCLI;

/// <summary>通用静态工具类：存放与业务无关的小工具函数（控制台彩色打印等）。</summary>
public static class Tool
{
    public static readonly Color AiName = Color.FromArgb(0x66, 0xCC, 0xFF);
    public static readonly Color UserPrompt = Color.FromArgb(0x99, 0x99, 0xFF);
    public static readonly Color Info = Color.FromArgb(0xEE, 0x82, 0xEE);
    public static readonly Color Error = Color.FromArgb(0xEE, 0x00, 0x00);
    public static readonly Color Warning = Color.FromArgb(0xFF, 0x88, 0x00);
    public static readonly Color Success = Color.FromArgb(0x76, 0xC0, 0x43);
    public static readonly Color Muted = Color.FromArgb(0x66, 0x66, 0x66);
    public static readonly Color Smky = Color.FromArgb(0x00, 0xFE, 0xCC);

    /// <summary>打印彩色文字（不换行）：临时切换控制台前景/背景色，输出后无论成败都恢复原色。</summary>
    /// <param name="text">要输出的文字</param>
    /// <param name="foreground">前景色</param>
    /// <param name="background">背景色，null 表示保持当前背景色</param>
    public static void PrintColor(string text, ConsoleColor foreground, ConsoleColor? background = null)
    {
        ConsoleColor oldForeground = Console.ForegroundColor;
        ConsoleColor oldBackground = Console.BackgroundColor;
        try
        {
            Console.ForegroundColor = foreground;
            if (background.HasValue) Console.BackgroundColor = background.Value;
            Console.Write(text);
        }
        finally
        {
            Console.ForegroundColor = oldForeground;
            Console.BackgroundColor = oldBackground;
        }
    }

    /// <summary>打印彩色文字并换行，颜色语义同 <see cref="PrintColor"/>。</summary>
    public static void PrintColorLine(string text, ConsoleColor foreground, ConsoleColor? background = null)
        => PrintColor(text + Environment.NewLine, foreground, background);

    /// <summary>
    /// 打印自定义 RGB 颜色文字（不换行）：通过 ANSI 24 位真彩色转义序列实现，
    /// 输出后以 \x1b[0m 重置所有显示属性。可用 Color.FromArgb(r, g, b) 或 Color 预定义色。
    /// </summary>
    /// <param name="text">要输出的文字</param>
    /// <param name="foreground">前景色（RGB）</param>
    /// <param name="background">背景色（RGB），null 表示保持当前背景色</param>
    public static void PrintColor(string text, Color foreground, Color? background = null)
    {
        EnsureVtEnabled();
        string prefix = $"\x1b[38;2;{foreground.R};{foreground.G};{foreground.B}m";
        if (background.HasValue)
        {
            Color bg = background.Value;
            prefix += $"\x1b[48;2;{bg.R};{bg.G};{bg.B}m";
        }
        Console.Write(prefix + text + "\x1b[0m");
    }

    /// <summary>打印自定义 RGB 颜色文字并换行，颜色语义同 <see cref="PrintColor(string, Color, Color?)"/>。</summary>
    public static void PrintColorLine(string text, Color foreground, Color? background = null)
        => PrintColor(text + Environment.NewLine, foreground, background);

    /// <summary>是否已尝试开启 Windows 控制台的 VT 序列支持（只尝试一次）。</summary>
    private static bool _vtChecked;

    /// <summary>
    /// 旧版 Windows 控制台默认不解析 ANSI 转义序列，首次调用时为 stdout 开启
    /// ENABLE_VIRTUAL_TERMINAL_PROCESSING；非 Windows 平台终端原生支持，无需处理。
    /// </summary>
    private static void EnsureVtEnabled()
    {
        if (_vtChecked || !OperatingSystem.IsWindows()) return;
        _vtChecked = true;
        nint handle = GetStdHandle(-11); // STD_OUTPUT_HANDLE
        if (GetConsoleMode(handle, out uint mode))
            SetConsoleMode(handle, mode | 0x0004); // ENABLE_VIRTUAL_TERMINAL_PROCESSING
    }

    [DllImport("kernel32.dll")] private static extern nint GetStdHandle(int nStdHandle);
    [DllImport("kernel32.dll")] private static extern bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);
    [DllImport("kernel32.dll")] private static extern bool SetConsoleMode(nint hConsoleHandle, uint dwMode);
}