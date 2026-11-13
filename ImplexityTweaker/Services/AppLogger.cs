using System;
using System.IO;
using System.Text;

namespace ImplexityTweaker.Services;

/// <summary>
/// Лёгкий потокобезопасный диагностический логгер (%LOCALAPPDATA%\Implexity\app.log)
/// с автоматической ротацией при превышении 1 МБ. Никогда не бросает исключений.
/// </summary>
public static class AppLogger
{
    private const long MaxLogBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string LogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Implexity");

    public static string LogFilePath => Path.Combine(LogFolder, "app.log");

    public static void Info(string area, string message) => Write("INFO", area, message, null);

    public static void Warn(string area, string message, Exception? ex = null) => Write("WARN", area, message, ex);

    public static void Error(string area, string message, Exception? ex = null) => Write("ERR ", area, message, ex);

    private static void Write(string level, string area, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogFolder);
                RotateIfNeeded();

                var sb = new StringBuilder(160);
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ")
                  .Append(level).Append(" [").Append(area).Append("] ").Append(message);

                if (ex != null)
                    sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);

                sb.AppendLine();
                File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Логгер не должен влиять на работу приложения.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath))
            return;

        var info = new FileInfo(LogFilePath);
        if (info.Length < MaxLogBytes)
            return;

        var backup = LogFilePath + ".old";
        if (File.Exists(backup))
            File.Delete(backup);

        File.Move(LogFilePath, backup);
    }
}
