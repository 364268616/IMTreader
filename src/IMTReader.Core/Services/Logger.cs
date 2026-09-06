using System.Text;

namespace IMTReader.Core.Services;

public enum LogLevel
{
    Info,
    Warn,
    Error
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Message)
{
    public string LevelText => Level switch
    {
        LogLevel.Warn => "警告",
        LogLevel.Error => "错误",
        _ => "信息"
    };

    public override string ToString() => $"{Time:yyyy-MM-dd HH:mm:ss} [{LevelText}] {Message}";
}

/// <summary>轻量日志：写入文件并保留最近记录供“日志”面板显示。</summary>
public sealed class Logger
{
    private const int MaxEntries = 2000;
    private readonly object _gate = new();
    private readonly LinkedList<LogEntry> _entries = new();
    private readonly string? _filePath;

    public static Logger Instance { get; set; } = new(null);

    public event Action<LogEntry>? EntryAdded;

    public Logger(string? filePath)
    {
        _filePath = filePath;
    }

    public void Info(string message) => Write(LogLevel.Info, message);

    public void Warn(string message) => Write(LogLevel.Warn, message);

    /// <summary>记录错误。面板只显示简短信息，日志文件另附完整异常与调用栈，便于排查。</summary>
    public void Error(string message, Exception? ex = null)
        => Write(LogLevel.Error, ex == null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}", ex?.ToString());

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate) return _entries.ToList();
    }

    private void Write(LogLevel level, string message, string? detail = null)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        lock (_gate)
        {
            _entries.AddLast(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveFirst();
            if (_filePath != null)
            {
                try
                {
                    var text = entry + Environment.NewLine + (detail != null ? detail + Environment.NewLine : string.Empty);
                    File.AppendAllText(_filePath, text, Encoding.UTF8);
                }
                catch { /* 日志写入失败不影响主流程 */ }
            }
        }
        EntryAdded?.Invoke(entry);
    }
}
