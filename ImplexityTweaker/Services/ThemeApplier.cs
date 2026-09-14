using System.Windows;
using System.Windows.Media;

namespace ImplexityTweaker.Services;


public static class ThemeApplier
{
    public const string ThemeImplexity = "Implexity";
    public const string ThemeAurora = "Aurora";
    public const string ThemeGraphite = "Graphite";
    public const string ThemeLight = "Light";
    public const string ThemeCream = "Cream";

    public static event EventHandler? ThemeChanged;

    public static string CurrentThemeId { get; private set; } = ThemeImplexity;

    /// <summary>Тёмная ли текущая тема (для тех мест, где нужен цвет «наоборот»).</summary>
    public static bool IsLightTheme => CurrentThemeId is ThemeLight or ThemeCream;

    public static IReadOnlyList<(string Id, string Title, string Description)> ThemeCatalog { get; } =
    [
        (ThemeImplexity, "Implexity", "Тёмно-синий фон (#050a14) и индиго-акцент (#6366f1)."),
        (ThemeAurora, "Aurora", "Тёмная тема с сине-зелёным акцентом."),
        (ThemeGraphite, "Graphite", "Нейтральная тёмная палитра."),
        (ThemeLight, "Light", "Спокойная светлая тема: серо-голубые панели и индиго-акцент — не слепит глаза."),
        (ThemeCream, "Cream", "Кремовая тема: тёплый фон и нежно-розовые кнопки."),
    ];

    
    public static string NormalizeThemeId(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return ThemeImplexity;
        // Валидируем: если ID не соответствует ни одной известной теме, откатываемся на Implexity.
        foreach (var (id, _, _) in ThemeCatalog)
        {
            if (string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return ThemeImplexity;
    }

    public static void Apply(string? themeId)
    {
        CurrentThemeId = NormalizeThemeId(themeId);
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        var light = CurrentThemeId is ThemeLight or ThemeCream;
        Brush bg, sidebar, surface, surfaceAlt, border, accent, accentSoft, iconAccent, text, textMuted, hover;

        // Цвет текста на акцентных кнопках: для пастельных акцентов он тёмный.
        var accentText = Hex("#ffffff");

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
                iconAccent = Hex("#5eead4");
                text = Hex("#f0fdfa");
                textMuted = Hex("#94a3a8");
                hover = Hex("#18FFFFFF");
                break;
            case ThemeGraphite:
                bg = Hex("#1c1c1c");
                sidebar = Hex("#252526");
                surface = Hex("#2d2d30");
                surfaceAlt = Hex("#333337");
                border = Hex("#3f3f46");
                accent = Hex("#3b82f6");
                accentSoft = Hex("#1e3a5f");
                iconAccent = Hex("#60a5fa");
                text = Hex("#fafafa");
                textMuted = Hex("#a1a1aa");
                hover = Hex("#18FFFFFF");
                break;
            case ThemeLight:
                // Мягкая светлая тема: фон не белоснежный, чтобы не утомлять глаза.
                bg = Hex("#e9edf4");
                sidebar = Hex("#f6f8fc");
                surface = Hex("#f6f8fc");
                surfaceAlt = Hex("#e3e9f2");
                border = Hex("#cfd8e6");
                accent = Hex("#4f46e5");
                accentSoft = Hex("#c7d2fe");
                iconAccent = Hex("#4f46e5");
                text = Hex("#1b2434");
                textMuted = Hex("#5b6577");
                hover = Hex("#0F000000");
                break;

            case ThemeCream:
                // Кремовая тема: насыщенный кремовый фон, тёплый текст и нежно-розовый акцент.
                // Фон специально глубокий: карточки WPF-UI полупрозрачные и берут его оттенок.
                bg = Hex("#eedcbb");
                sidebar = Hex("#f7ebd3");
                surface = Hex("#f5e6c8");
                surfaceAlt = Hex("#ecd8b3");
                border = Hex("#d8bd91");
                accent = Hex("#ef9db4");
                accentSoft = Hex("#fadbe4");
                iconAccent = Hex("#b96686");
                text = Hex("#4a3a24");
                textMuted = Hex("#89714c");
                hover = Hex("#18000000");
                accentText = Hex("#5c2f42");
                break;

            case ThemeImplexity:
            default:
                bg = Hex("#050a14");
                sidebar = Hex("#070f1f");
                surface = Hex("#0c1528");
                surfaceAlt = Hex("#101e36");
                border = Hex("#1a2840");
                accent = Hex("#6366f1");
                accentSoft = Hex("#312e81");
                iconAccent = Hex("#818cf8");
                text = Hex("#f8fafc");
                textMuted = Hex("#94a3b8");
                hover = Hex("#18FFFFFF");
                break;
        }

        SetBrush("App.Background", bg);
        SetBrush("App.Sidebar", sidebar);
        SetBrush("App.Surface", surface);
        SetBrush("App.SurfaceAlt", surfaceAlt);
        SetBrush("App.Border", border);
        SetBrush("App.Accent", accent);
        SetBrush("App.AccentSoft", accentSoft);
        SetBrush("App.IconAccent", iconAccent);
        SetBrush("App.Text", text);
        SetBrush("App.TextMuted", textMuted);
        SetBrush("App.Hover", hover);

        TryApplyWpfUiTheme(light);
        TryApplyWpfUiAccent(accent, light);

        // Карточки, поля ввода и границы в WPF-UI берут цвета из собственных ресурсов —
        // перебиваем их палитрой темы, иначе светлые темы выглядят «пятнами».
        if (light)
        {
            try
            {
                ApplyWpfUiOverrides(bg, sidebar, surface, surfaceAlt, border, accent, accentSoft,
                    text, textMuted, hover, accentText);
            }
            catch
            {
                // Оформление не должно ломать запуск приложения.
            }
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);

        ProbeKeys();
    }

    // ВРЕМЕННАЯ диагностика: какие кандидаты-кисти WPF-UI остались белёсыми.
    private static void ProbeKeys()
    {
        try
        {
            string[] keys =
            [
                "CardBackgroundFillColorDefaultBrush", "CardBackgroundFillColorSecondaryBrush",
                "CardBackgroundFillColorDefault", "CardBackgroundFillColorSecondary",
                "CardBackgroundFillColorTertiaryBrush", "CardStrokeColorDefaultBrush",
                "ControlFillColorDefaultBrush", "ControlFillColorSecondaryBrush",
                "ControlFillColorTertiaryBrush", "ControlFillColorQuarternaryBrush",
                "ControlFillColorAltBrush", "ControlFillColorAltSecondaryBrush",
                "ControlFillColorInputActiveBrush", "ControlFillColorTransparentBrush",
                "ControlFillColorTransparent",
                "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorBaseAltBrush",
                "SolidBackgroundFillColorSecondaryBrush", "SolidBackgroundFillColorTertiaryBrush",
                "SolidBackgroundFillColorQuarternaryBrush", "SolidBackgroundFillColorTransparentBrush",
                "LayerFillColorDefaultBrush", "LayerFillColorAltBrush",
                "LayerOnAcrylicFillColorDefaultBrush", "LayerOnMicaBaseAltFillColorDefaultBrush",
                "LayerOnMicaBaseAltFillColorSecondaryBrush", "LayerOnMicaBaseAltFillColorTertiaryBrush",
                "AcrylicBackgroundFillColorDefaultBrush", "AcrylicBackgroundFillColorBaseBrush",
                "MicaBackgroundFillColorBaseBrush", "ApplicationBackgroundBrush",
            ];

            var sb = new System.Text.StringBuilder();
            foreach (var key in keys)
            {
                var value = Application.Current.TryFindResource(key);
                var text = value switch
                {
                    SolidColorBrush b => b.Color + " a=" + b.Color.A + " op=" + b.Opacity,
                    null => "null",
                    _ => value.GetType().Name
                };
                sb.AppendLine(key + " => " + text);
            }

            System.IO.File.WriteAllText(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "theme_probe.txt"), sb.ToString());
        }
        catch
        {
            // диагностика не должна влиять на работу приложения
        }
    }

    /// проверка настроек
    public static void ApplyFromSavedSettings()
    {
        Apply(AppSettings.Load().ThemeId);
    }

    /// <summary>
    /// Переключает базовую тему WPF-UI (Light/Dark). Без этого контролы (карточки, кнопки,
    /// поля ввода) остались бы тёмными на светлом фоне.
    /// </summary>
    private static void TryApplyWpfUiTheme(bool light)
    {
        try
        {
            Wpf.Ui.Appearance.Theme.Apply(
                light ? Wpf.Ui.Appearance.ThemeType.Light : Wpf.Ui.Appearance.ThemeType.Dark,
                Wpf.Ui.Appearance.BackgroundType.None,
                false,
                false);
        }
        catch
        {
            // На этапе старта окна ещё нет, и WPF-UI бросает ArgumentNullException,
            // но словари темы к этому моменту уже переключены — просто игнорируем.
        }
    }

    private static void TryApplyWpfUiAccent(Brush accent, bool light)
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
            var themeValue = Enum.Parse(themeEnum, light ? "Light" : "Dark", ignoreCase: true);
            var apply = mgr.GetMethod("Apply", [typeof(Color), themeEnum]);
            apply?.Invoke(null, [c, themeValue]);
        }
        catch
        {
            
        }
    }

    /// <summary>
    /// Перебивает кисти WPF-UI (карточки, поля ввода, границы, текст) палитрой темы.
    /// Контролы WPF-UI берут цвета из своих ресурсов, а не из App.*, поэтому без этого
    /// светлые темы выглядели «пятнами»: кремовый фон + серо-белые карточки.
    /// </summary>
    private static void ApplyWpfUiOverrides(
        Brush background, Brush sidebar, Brush surface, Brush surfaceAlt, Brush border,
        Brush accent, Brush accentSoft, Brush text, Brush textMuted, Brush hover, Brush accentText)
    {
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        if (_overrideDict != null && app.Resources.MergedDictionaries.Contains(_overrideDict))
            app.Resources.MergedDictionaries.Remove(_overrideDict);

        // Отдельный словарь добавляем последним в MergedDictionaries: так он перекрывает
        // палитру WPF-UI и при этом не трогает словари с отложенным содержимым.
        var dict = new ResourceDictionary();

        // Фон окна и панелей
        Set(dict, "ApplicationBackgroundBrush", background);
        Set(dict, "SolidBackgroundFillColorBaseBrush", background);
        Set(dict, "SolidBackgroundFillColorBaseAltBrush", sidebar);
        Set(dict, "SolidBackgroundFillColorSecondaryBrush", surface);
        Set(dict, "SolidBackgroundFillColorTertiaryBrush", surfaceAlt);
        Set(dict, "SolidBackgroundFillColorQuarternaryBrush", surfaceAlt);
        Set(dict, "LayerFillColorDefaultBrush", surface);
        Set(dict, "LayerOnAcrylicFillColorDefaultBrush", surface);
        Set(dict, "AcrylicBackgroundFillColorDefaultBrush", surface);

        // Карточки и блоки
        Set(dict, "CardBackgroundFillColorDefaultBrush", surface);
        Set(dict, "CardBackgroundFillColorSecondaryBrush", surfaceAlt);
        Set(dict, "CardStrokeColorDefaultBrush", border);

        // Поля ввода, списки, обычные кнопки
        Set(dict, "ControlFillColorDefaultBrush", surface);
        Set(dict, "ControlFillColorSecondaryBrush", surfaceAlt);
        Set(dict, "ControlFillColorTertiaryBrush", surfaceAlt);
        Set(dict, "ControlFillColorInputActiveBrush", surface);
        Set(dict, "ControlFillColorDisabledBrush", surfaceAlt);
        Set(dict, "SubtleFillColorSecondaryBrush", hover);
        Set(dict, "SubtleFillColorTertiaryBrush", hover);

        // Границы
        Set(dict, "ControlStrokeColorDefaultBrush", border);
        Set(dict, "ControlStrokeColorSecondaryBrush", border);
        Set(dict, "ControlStrokeColorOnAccentDefaultBrush", border);
        Set(dict, "DividerStrokeColorDefaultBrush", border);

        // Текст
        Set(dict, "TextFillColorPrimaryBrush", text);
        Set(dict, "TextFillColorSecondaryBrush", textMuted);
        Set(dict, "TextFillColorTertiaryBrush", textMuted);
        Set(dict, "TextFillColorDisabledBrush", textMuted);

        // Акцент: кнопки, переключатели, выделение
        Set(dict, "AccentFillColorDefaultBrush", accent);
        Set(dict, "AccentFillColorSecondaryBrush", accentSoft);
        Set(dict, "AccentFillColorTertiaryBrush", accentSoft);
        Set(dict, "AccentFillColorDisabledBrush", accentSoft);
        Set(dict, "AccentTextFillColorPrimaryBrush", accentText);
        Set(dict, "AccentTextFillColorSecondaryBrush", accentText);
        Set(dict, "AccentTextFillColorTertiaryBrush", accentText);

        app.Resources.MergedDictionaries.Add(dict);
        _overrideDict = dict;
    }

    private static ResourceDictionary? _overrideDict;

    /// <summary>Кисти WPF-UI перекрываем в ресурсах приложения.</summary>
    private static void Set(ResourceDictionary resources, string key, Brush brush)
    {
        // Набор ключей немного отличается между версиями WPF-UI — лишние просто не используются.
        resources[key] = brush;
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

