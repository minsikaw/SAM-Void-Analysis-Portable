using System;
using System.IO;

namespace RFGAN.SampleMapper.Services;

/// <summary>
/// Lightweight application logging facade used by WinForms code.
/// It delegates to LoggerHelper/Serilog so existing logging stays intact while
/// giving forms a consistent source + level API.
/// </summary>
public static class AppLogger
{
    private static readonly object FallbackLock = new();

    public static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

    public static void Initialize(string source = "Application")
    {
        try
        {
            LoggerHelper.EnsureInitialized();
            Info(source, $"Logger initialized. LogDirectory={LogDirectory}");
        }
        catch (Exception ex)
        {
            WriteFallback("ERROR", source, "Logger initialization failed; fallback file logging attempted.", ex);
        }
    }

    public static void Info(string source, string message, params object?[] args)
        => Write("INF", source, message, null, args);

    public static void Warning(string source, string message, params object?[] args)
        => Write("WRN", source, message, null, args);

    public static void Error(string source, string message, params object?[] args)
        => Write("ERR", source, message, null, args);

    public static void Error(string source, Exception exception, string message, params object?[] args)
        => Write("ERR", source, message, exception, args);

    public static void Exception(string source, Exception exception, string message, params object?[] args)
        => Error(source, exception, message, args);

    private static void Write(string level, string source, string message, Exception? exception, params object?[] args)
    {
        try
        {
            LoggerHelper.EnsureInitialized();
            string prefixedMessage = $"{NormalizeSource(source)}: {message}";

            switch (level)
            {
                case "WRN":
                    LoggerHelper.Warning(prefixedMessage, args);
                    break;
                case "ERR" when exception is not null:
                    LoggerHelper.Error(exception, prefixedMessage, args);
                    break;
                case "ERR":
                    LoggerHelper.Error(prefixedMessage, args);
                    break;
                default:
                    LoggerHelper.Information(prefixedMessage, args);
                    break;
            }
        }
        catch (Exception logException)
        {
            WriteFallback(level, source, FormatFallbackMessage(message, args), exception ?? logException);
        }
    }

    private static string NormalizeSource(string? source)
        => string.IsNullOrWhiteSpace(source) ? "Application" : source.Trim();

    private static string FormatFallbackMessage(string message, object?[] args)
    {
        if (args.Length == 0)
            return message;

        try
        {
            return string.Format(message, args);
        }
        catch
        {
            return message + " | Args=" + string.Join(", ", args);
        }
    }

    private static void WriteFallback(string level, string source, string message, Exception? exception)
    {
        try
        {
            lock (FallbackLock)
            {
                Directory.CreateDirectory(LogDirectory);
                string filePath = Path.Combine(LogDirectory, $"app-fallback-{DateTime.Now:yyyyMMdd}.log");
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {NormalizeSource(source)}: {message}";
                if (exception is not null)
                    line += Environment.NewLine + exception;
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the UI.
        }
    }
}
