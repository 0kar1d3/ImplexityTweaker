using System.IO;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>Аудит автозагрузки: ищет подозрительные записи (TEMP, отсутствующие файлы).</summary>
public static class AutostartAuditService
{
    public sealed record Entry(string Name, string Command, string Source, string Problem);

    private static readonly (RegistryKey Root, string Path, string Source)[] RunKeys =
    [
        (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
        (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
        (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (x86)"),
    ];

    public static IReadOnlyList<Entry> Scan()
    {
        var list = new List<Entry>();

        foreach (var (root, path, source) in RunKeys)
        {
            try
            {
                using var key = root.OpenSubKey(path);
                if (key == null)
                    continue;

                foreach (var valueName in key.GetValueNames())
                {
                    var command = key.GetValue(valueName)?.ToString() ?? string.Empty;
                    list.Add(new Entry(valueName, command, source, AnalyzeCommand(command)));
                }
            }
            catch
            {
                // нет доступа к ветке — пропускаем
            }
        }

        // Папка «Автозагрузка»
        try
        {
            var startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startup))
            {
                foreach (var file in Directory.GetFiles(startup))
                    list.Add(new Entry(Path.GetFileName(file), file, "Папка автозагрузки", AnalyzeFile(file)));
            }
        }
        catch
        {
            // пропускаем
        }

        return list;
    }

    /// <summary>Записи с непустой «проблемой» — потенциально лишние.</summary>
    public static int CountSuspicious(IReadOnlyList<Entry> entries) =>
        entries.Count(e => !string.IsNullOrEmpty(e.Problem));

    private static string AnalyzeCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return "пустая команда";

        var path = ExtractPath(command);
        if (path.Length == 0)
            return string.Empty;

        var problem = AnalyzeFile(path);
        if (problem.Length > 0)
            return problem;

        if (path.Contains(@"\temp\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\tmp\", StringComparison.OrdinalIgnoreCase) ||
            path.Contains(@"\downloads\", StringComparison.OrdinalIgnoreCase))
        {
            return "расположен во временной папке";
        }

        return string.Empty;
    }

    private static string AnalyzeFile(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && !File.Exists(path) && !Directory.Exists(path))
                return "файл не найден";
        }
        catch
        {
            return string.Empty;
        }
        return string.Empty;
    }

    private static string ExtractPath(string command)
    {
        var text = command.Trim();
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? text[1..end] : text.Trim('"');
        }

        var space = text.IndexOf(' ');
        return space > 0 ? text[..space] : text;
    }
}
