using System.Text.Encodings.Web;
using System.Text.Json;

namespace SmkyCLI;

/// <summary>
/// 长期记忆本地快照存取：文件固定为可执行文件同目录的 smky-memory.json，
/// 只在启用艾宾浩斯记忆时由 <see cref="SmkyChat"/> 读写；只保存记忆快照，
/// 不包含聊天原文与密钥。读写失败仅告警，不阻断对话。
/// </summary>
public static class MemoryStore
{
    public const string FileName = "smky-memory.json";

    /// <summary>落盘路径：与可执行文件同目录（单文件发布时同样指向 exe 所在目录）。</summary>
    public static string FilePath { get; } = Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>落盘 JSON 缩进且中文不转义，便于用户直接阅读。</summary>
    private static readonly JsonSerializerOptions StoreOptions = new(CharacterJson.Options)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>读取本地记忆快照；文件不存在返回 null，读取失败打印警告并返回 null。</summary>
    public static MemorySnapshot? Load()
    {
        if (!File.Exists(FilePath)) return null;
        try
        {
            return MemorySnapshot.FromJson(File.ReadAllText(FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Tool.PrintColorLine($"Warn >> 读取本地记忆失败，已忽略：{ex.Message}", Tool.Warning);
            return null;
        }
    }

    /// <summary>原子写入快照：先写临时文件再替换，避免写入中断损坏已有记忆文件。</summary>
    public static bool Save(MemorySnapshot snapshot)
    {
        string tempPath = FilePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath,
                JsonSerializer.Serialize(snapshot, StoreOptions.GetTypeInfo(typeof(MemorySnapshot))));
            File.Move(tempPath, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Tool.PrintColorLine($"Warn >> 记忆落盘失败：{ex.Message}", Tool.Warning);
            return false;
        }
    }
}
