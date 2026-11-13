using System;
using System.IO;
using System.Text.Json;

namespace ImplexityTweaker.Services;

public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    TopCenter,
    Custom
}

public enum OverlayLayoutMode
{
    Horizontal,
    Vertical
}

public enum OverlayScale
{
    Compact,
    Standard,
    Large
}

public enum OverlayVisualStyle
{
    Classic = 0,
    TelemetryText = 1,
    RoundedGlass = 2,
    GamingBars = 3
}

public sealed class OverlaySettings
{
    public bool Enabled { get; set; }
    public bool AutoStartWithTweaker { get; set; }

    // Метрики
    public bool ShowFps { get; set; } = true;
    public bool ShowFrametime { get; set; } = true;
    public bool ShowOnePercentLow { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowCpuTemp { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowGpuTemp { get; set; } = true;
    public bool ShowRam { get; set; } = true;
    public bool ShowProcessName { get; set; } = true;
    public bool ShowPerCoreCpu { get; set; } = true;
    public bool ShowDetailedGpu { get; set; } = true;
    public bool ShowFramerateGraph { get; set; } = true;

    // Внешний вид
    public OverlayVisualStyle VisualStyle { get; set; } = OverlayVisualStyle.Classic;
    public OverlayPosition Position { get; set; } = OverlayPosition.TopLeft;
    public double CustomLeft { get; set; } = 32;
    public double CustomTop { get; set; } = 32;
    public OverlayLayoutMode Layout { get; set; } = OverlayLayoutMode.Horizontal;
    public OverlayScale Scale { get; set; } = OverlayScale.Standard;
    public int BackgroundOpacityPercent { get; set; } = 78;
    public int RefreshIntervalMs { get; set; } = 1000;
    public bool UseAppTheme { get; set; }
    public string Hotkey { get; set; } = "F9";

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Implexity",
        "overlay.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static OverlaySettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<OverlaySettings>(json, JsonOpts) ?? new OverlaySettings();
            }
        }
        catch
        {
        }

        return new OverlaySettings();
    }

    public static void Save(OverlaySettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOpts));
        }
        catch
        {
        }
    }
}
