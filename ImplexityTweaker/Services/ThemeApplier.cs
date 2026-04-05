using System.Windows;
using System.Windows.Media;

namespace ImplexityTweaker.Services;


public static class ThemeApplier
{
    public const string ThemeImplexity = "Implexity";
    private const string LegacyStoredThemeId = "Maku";
    public const string ThemeAurora = "Aurora";
    public const string ThemeGraphite = "Graphite";
    public const string ThemeDawn = "Dawn";

    public static event EventHandler? ThemeChanged;

    public static string CurrentThemeId { get; private set; } = ThemeImplexity;

    public static IReadOnlyList<(string Id, string Title, string Description)> ThemeCatalog { get; } =
    [
        (ThemeImplexity, "Implexity", "Тёмно-синий фон (#050a14) и фиолетовый акцент (#9333ea)."),
        (ThemeAurora, "Aurora", "Тёмная тема с сине-зелёным акцентом."),
        (ThemeGraphite, "Graphite", "Нейтральная тёмная палитра."),
        (ThemeDawn, "Dawn", "Светлая мягкая тема.")
    ];

    
    public static string NormalizeThemeId(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return ThemeImplexity;
        if (string.Equals(themeId, LegacyStoredThemeId, StringComparison.OrdinalIgnoreCase))
            return ThemeImplexity;
        return themeId!;
    }

    public static void Apply(string? themeId)
    {
        CurrentThemeId = NormalizeThemeId(themeId);
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        Brush bg, sidebar, surface, surfaceAlt, border, accent, accentSoft, text, textMuted;

        switch (CurrentThemeId)
        {
            case ThemeAurora:
                bg = Hex("#0a1620");
                sidebar = Hex("#0d1a26");
                surface = Hex("#111f2e");
                surfaceAlt = Hex("#152a3d");
                border = Hex("#1e3a4d");
                accent = Hex("#2dd4bf");
                accentSoft = Hex("#134e4a");
                text = Hex("#f0fdfa");
                textMuted = Hex("#94a3a8");
                break;
            case ThemeGraphite:
                bg = Hex("#1c1c1c");
                sidebar = Hex("#252526");
                surface = Hex("#2d2d30");
                surfaceAlt = Hex("#333337");
                border = Hex("#3f3f46");
                accent = Hex("#3b82f6");
                accentSoft = Hex("#1e3a5f");
                text = Hex("#fafafa");
                textMuted = Hex("#a1a1aa");
                break;
            case ThemeDawn:
                bg = Hex("#f1f5f9");
                sidebar = Hex("#e2e8f0");
                surface = Hex("#ffffff");
                surfaceAlt = Hex("#f8fafc");
                border = Hex("#cbd5e1");
                accent = Hex("#6366f1");
                accentSoft = Hex("#e0e7ff");
                text = Hex("#0f172a");
                textMuted = Hex("#475569");
                break;
            case ThemeImplexity:
            default:
                bg = Hex("#050a14");
                sidebar = Hex("#070f1f");
                surface = Hex("#0c1528");
                surfaceAlt = Hex("#101e36");
                border = Hex("#1a2840");
                accent = Hex("#9333ea");
                accentSoft = Hex("#4c1d95");
                text = Hex("#f8fafc");
                textMuted = Hex("#94a3b8");
                break;
        }

        SetBrush("App.Background", bg);
        SetBrush("App.Sidebar", sidebar);
        SetBrush("App.Surface", surface);
        SetBrush("App.SurfaceAlt", surfaceAlt);
        SetBrush("App.Border", border);
        SetBrush("App.Accent", accent);
        SetBrush("App.AccentSoft", accentSoft);
        SetBrush("App.Text", text);
        SetBrush("App.TextMuted", textMuted);

        TryApplyWpfUiAccent(accent);

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// проверка настроек
    public static void ApplyFromSavedSettings()
    {
        Apply(AppSettings.Load().ThemeId);
    }

    private static void TryApplyWpfUiAccent(Brush accent)
    {
        if (accent is not SolidColorBrush scb)
            return;
        try
        {
            var c = scb.Color;
            var mgr = Type.GetType("Wpf.Ui.Appearance.ApplicationAccentColorManager, Wpf.Ui");
            var themeEnum = Type.GetType("Wpf.Ui.Appearance.ApplicationTheme, Wpf.Ui")
                            ?? Type.GetType("Wpf.Ui.Appearance.ThemeType, Wpf.Ui");
            if (mgr == null || themeEnum == null)
                return;
            var dark = Enum.Parse(themeEnum, "Dark", ignoreCase: true);
            var apply = mgr.GetMethod("Apply", [typeof(Color), themeEnum]);
            apply?.Invoke(null, [c, dark]);
        }
        catch
        {
            
        }
    }

    private static void SetBrush(string key, Brush brush)
    {
        Application.Current.Resources[key] = brush;
    }

    private static SolidColorBrush Hex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        return new SolidColorBrush(c) { Opacity = 1.0 };
    }
}
