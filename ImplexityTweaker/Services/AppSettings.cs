using System.IO;
using System.Text.Json;

namespace ImplexityTweaker.Services;

public class AppSettings
{
    public string ThemeId { get; set; } = ThemeApplier.ThemeImplexity;

    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImplexityTweaker", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                s.ThemeId = ThemeApplier.NormalizeThemeId(s.ThemeId);
                return s;
            }
        }
        catch
        {
            /* ignore */
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            /* ignore */
        }
    }
}
