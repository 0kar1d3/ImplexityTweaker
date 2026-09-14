using System.IO;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>Твики, привязанные к конкретному процессу (сохраняются в process_tweaks.json).</summary>
public class ProcessTweaks
{
    /// <summary>Параметры запуска CS2.</summary>
    public string LaunchArgs { get; set; } = string.Empty;

    /// <summary>Процесс, которому Windows всегда даёт высокий приоритет (через IFEO).</summary>
    public string HighPriorityProcess { get; set; } = "cs2.exe";
    public bool HighPriorityEnabled { get; set; }

    /// <summary>Процесс, ограниченный физическими ядрами (без Hyper-Threading).</summary>
    public string AffinityProcess { get; set; } = "cs2.exe";
    public bool AffinityEnabled { get; set; }

    /// <summary>Процесс, которому наблюдатель поднимает приоритет при запуске.</summary>
    public string AutoPriorityProcess { get; set; } = "cs2.exe";
    public bool AutoPriorityEnabled { get; set; }
}

/// <summary>Хранилище твиков процессов: %LOCALAPPDATA%\Implexity\process_tweaks.json.</summary>
public static class ProcessTweaksStore
{
    private static readonly object Gate = new();

    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Implexity", "process_tweaks.json");

    public static ProcessTweaks Load()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var loaded = JsonSerializer.Deserialize<ProcessTweaks>(File.ReadAllText(StorePath));
                if (loaded != null)
                    return loaded;
            }
        }
        catch
        {
            // повреждённый файл — начинаем с настроек по умолчанию
        }

        return new ProcessTweaks();
    }

    public static void Save(ProcessTweaks tweaks)
    {
        lock (Gate)
        {
            try
            {
                var dir = Path.GetDirectoryName(StorePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(StorePath,
                    JsonSerializer.Serialize(tweaks, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // не удалось сохранить — не критично
            }
        }
    }
}
