using System.Text;

namespace RFGAN.SampleMapper.Services;

// Minimal compatibility implementation for the unchanged Phase 4.3 engine.
// Intentionally does not pull Serilog or WinForms into the batch workflow.
public static class LoggerHelper
{
    private static readonly object Sync = new();
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RFGAN.BatchQuantifier", "detector.log");

    public static void EnsureInitialized() => Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    public static void Information(string message, params object?[] args) => Write("INFO", message, null, args);
    public static void Warning(string message, params object?[] args) => Write("WARN", message, null, args);
    public static void Error(string message, params object?[] args) => Write("ERROR", message, null, args);
    public static void Error(Exception ex, string message, params object?[] args) => Write("ERROR", message, ex, args);
    private static void Write(string level, string message, Exception? ex, object?[] args)
    {
        try
        {
            EnsureInitialized();
            lock (Sync)
                File.AppendAllText(LogFile, $"{DateTimeOffset.UtcNow:O} [{level}] {message} | {string.Join("; ", args.Select(a => a?.ToString()))}{(ex == null ? "" : Environment.NewLine + ex)}{Environment.NewLine}", Encoding.UTF8);
        }
        catch { /* Logging must not stop analysis. */ }
    }
}
