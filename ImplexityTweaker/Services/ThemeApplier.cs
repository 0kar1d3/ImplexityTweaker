using System;
using System.Collections.Generic;
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
    public const string ThemeIos = "iOS";
    public const string ThemeAppleLight = "AppleLight";
    public const string ThemeAppleMidnight = "AppleMidnight";
    public const string ThemeAppleTitanium = "AppleTitanium";
    public const string ThemeAppleRose = "AppleRose";

    public const string StyleWindows = "Windows";
    public const string StyleApple = "Apple";

    public static event EventHandler? ThemeChanged;
    public static event EventHandler? UiStyleChanged;

    public static string CurrentThemeId { get; private set; } = ThemeIos;
    public static string CurrentUiStyle { get; private set; } = StyleApple;

    /// <summary>Тёмная ли текущая тема (для тех мест, где нужен цвет «наоборот»).</summary>
    public static bool IsLightTheme => CurrentThemeId is ThemeLight or ThemeCream or ThemeAppleLight;

    /// <summary>Тема в стиле Apple iOS / macOS (светофор окна, скругления, системный синий/зелёный).</summary>
    public static bool IsIosTheme => CurrentThemeId is ThemeIos or ThemeAppleLight or ThemeAppleMidnight or ThemeAppleTitanium or ThemeAppleRose;

    /// <summary>Включён ли отдельный дизайн-режим Apple (iPhone / macOS Settings).</summary>
    public static bool IsAppleStyle => CurrentUiStyle == StyleApple;

    public static IReadOnlyList<(string Id, string Title)> UiStyleCatalog { get; } =
    [
        (StyleWindows, "Windows"),
        (StyleApple, "Apple"),
    ];

    public static IReadOnlyList<(string Id, string Title, string Description)> WindowsThemeCatalog { get; } =
    [
        (ThemeImplexity, "Implexity", "Тёмно-синий фон (#050a14) и индиго-акцент (#6366f1)."),
        (ThemeAurora, "Aurora", "Тёмная тема с сине-зелёным акцентом."),
        (ThemeGraphite, "Graphite", "Нейтральная тёмная палитра."),
        (ThemeLight, "Light", "Спокойная светлая тема: серо-голубые панели и индиго-акцент — не слепит глаза."),
        (ThemeCream, "Cream", "Кремовая тема: тёплый фон и нежно-розовые кнопки."),
    ];

    public static IReadOnlyList<(string Id, string Title, string Description)> AppleThemeCatalog { get; } =
    [
        (ThemeIos, "Тёмный графит", "Глубокий тёмный графит и фирменный синий акцент (#0A84FF)."),
        (ThemeAppleLight, "Светлое стекло", "Светлое матовое стекло, чистый белый фон и системный синий (#007AFF)."),
        (ThemeAppleMidnight, "Полуночный индиго", "Тёмно-синяя атмосфера Midnight и акцент Indigo (#5E5CE6)."),
        (ThemeAppleTitanium, "Природный титан", "Тёплый титановый графит и янтарный акцент (#FF9F0A)."),
        (ThemeAppleRose, "Динамический розовый", "Глубокий тёмный фон с розовым акцентом (#FF375F)."),
    ];

    public static IReadOnlyList<(string Id, string Title, string Description)> ThemeCatalog { get; } =
    [
        .. WindowsThemeCatalog,
        .. AppleThemeCatalog,
    ];

    public static IReadOnlyList<(string Id, string Title, string Description)> GetThemesForStyle(string? style)
        => string.Equals(NormalizeUiStyle(style), StyleApple, StringComparison.Ordinal)
            ? AppleThemeCatalog
            : WindowsThemeCatalog;

    public static bool IsThemeInStyle(string? themeId, string? style)
    {
        var normalized = NormalizeThemeId(themeId);
        foreach (var (id, _, _) in GetThemesForStyle(style))
        {
            if (string.Equals(id, normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static string GetDefaultThemeForStyle(string? style)
        => string.Equals(NormalizeUiStyle(style), StyleApple, StringComparison.Ordinal)
            ? ThemeIos
            : ThemeImplexity;

    public static string NormalizeThemeId(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
            return ThemeIos;
        foreach (var (id, _, _) in ThemeCatalog)
        {
            if (string.Equals(id, themeId, StringComparison.OrdinalIgnoreCase))
                return id;
        }
        return ThemeIos;
    }

    public static void Apply(string? themeId)
    {
        CurrentThemeId = NormalizeThemeId(themeId);
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        var light = IsLightTheme;
        Brush bg, sidebar, surface, surfaceAlt, border, borderSubtle;
        Brush accent, accentSoft, iconAccent, navActiveBg, text, textMuted, hover;
        Brush success, successSoft, warning, warningSoft, danger, dangerSoft;

        var accentText = Hex("#ffffff");

        switch (CurrentThemeId)
        {
            case ThemeAurora:
                bg = Hex("#09131c");
                sidebar = Hex("#0b1824");
                surface = Hex("#10202e");
                surfaceAlt = Hex("#15293a");
                border = Hex("#1e394e");
                borderSubtle = Hex("#162c3d");
                accent = Hex("#14b8a6");
                accentSoft = Hex("#134e4a");
                iconAccent = Hex("#2dd4bf");
                navActiveBg = Hex("#1f14b8a6");
                text = Hex("#f0fdfa");
                textMuted = Hex("#93adb8");
                hover = Hex("#12FFFFFF");
                accentText = Hex("#042f2e");
                break;

            case ThemeGraphite:
                bg = Hex("#19191b");
                sidebar = Hex("#202023");
                surface = Hex("#27272a");
                surfaceAlt = Hex("#303034");
                border = Hex("#3b3b42");
                borderSubtle = Hex("#2e2e34");
                accent = Hex("#3b82f6");
                accentSoft = Hex("#1d3a63");
                iconAccent = Hex("#60a5fa");
                navActiveBg = Hex("#243b82f6");
                text = Hex("#fafafa");
                textMuted = Hex("#a1a1aa");
                hover = Hex("#12FFFFFF");
                break;

            case ThemeLight:
                bg = Hex("#edf1f7");
                sidebar = Hex("#f4f6fa");
                surface = Hex("#ffffff");
                surfaceAlt = Hex("#e7ecf4");
                border = Hex("#d5dde9");
                borderSubtle = Hex("#e3e8f0");
                accent = Hex("#4f46e5");
                accentSoft = Hex("#e0e7ff");
                iconAccent = Hex("#4f46e5");
                navActiveBg = Hex("#164f46e5");
                text = Hex("#18202c");
                textMuted = Hex("#5c6678");
                hover = Hex("#0C000000");
                break;

            case ThemeCream:
                bg = Hex("#efe1c6");
                sidebar = Hex("#f6ebd5");
                surface = Hex("#fbf3e4");
                surfaceAlt = Hex("#ead6b0");
                border = Hex("#d6bd93");
                borderSubtle = Hex("#e3ceaa");
                accent = Hex("#e589a4");
                accentSoft = Hex("#fadbe4");
                iconAccent = Hex("#b55779");
                navActiveBg = Hex("#24e589a4");
                text = Hex("#453520");
                textMuted = Hex("#7e6744");
                hover = Hex("#0F000000");
                accentText = Hex("#4d2033");
                break;

            case ThemeIos:
                bg = Hex("#0b0b0d");
                sidebar = Hex("#161618");
                surface = Hex("#1c1c1e");
                surfaceAlt = Hex("#2c2c2e");
                border = Hex("#323236");
                borderSubtle = Hex("#262629");
                accent = Hex("#0a84ff");
                accentSoft = Hex("#143356");
                iconAccent = Hex("#0a84ff");
                navActiveBg = Hex("#260a84ff");
                text = Hex("#ffffff");
                textMuted = Hex("#8e8e93");
                hover = Hex("#14FFFFFF");
                break;

            case ThemeAppleLight:
                bg = Hex("#f2f4f8");
                sidebar = Hex("#eef1f6");
                surface = Hex("#ffffff");
                surfaceAlt = Hex("#e5e8ef");
                border = Hex("#d5dae3");
                borderSubtle = Hex("#e4e7ee");
                accent = Hex("#007aff");
                accentSoft = Hex("#dbeafe");
                iconAccent = Hex("#007aff");
                navActiveBg = Hex("#1f007aff");
                text = Hex("#1d1d1f");
                textMuted = Hex("#6e6e73");
                hover = Hex("#0C000000");
                break;

            case ThemeAppleMidnight:
                bg = Hex("#090d1a");
                sidebar = Hex("#11172a");
                surface = Hex("#171f36");
                surfaceAlt = Hex("#222c4a");
                border = Hex("#2c385c");
                borderSubtle = Hex("#202a46");
                accent = Hex("#5e5ce6");
                accentSoft = Hex("#22215c");
                iconAccent = Hex("#7d7aff");
                navActiveBg = Hex("#285e5ce6");
                text = Hex("#f5f7ff");
                textMuted = Hex("#8f99b8");
                hover = Hex("#14FFFFFF");
                break;

            case ThemeAppleTitanium:
                bg = Hex("#12110f");
                sidebar = Hex("#1a1916");
                surface = Hex("#22201d");
                surfaceAlt = Hex("#2e2b27");
                border = Hex("#3b3731");
                borderSubtle = Hex("#2b2824");
                accent = Hex("#ff9f0a");
                accentSoft = Hex("#3d290c");
                iconAccent = Hex("#ffb340");
                navActiveBg = Hex("#26ff9f0a");
                text = Hex("#f7f5f0");
                textMuted = Hex("#9e988e");
                hover = Hex("#14FFFFFF");
                break;

            case ThemeAppleRose:
                bg = Hex("#120b10");
                sidebar = Hex("#1b1118");
                surface = Hex("#241720");
                surfaceAlt = Hex("#32202c");
                border = Hex("#422a3a");
                borderSubtle = Hex("#301e2a");
                accent = Hex("#ff375f");
                accentSoft = Hex("#471321");
                iconAccent = Hex("#ff6482");
                navActiveBg = Hex("#26ff375f");
                text = Hex("#fff5f7");
                textMuted = Hex("#a88c96");
                hover = Hex("#14FFFFFF");
                break;

            case ThemeImplexity:
            default:
                bg = Hex("#070c16");
                sidebar = Hex("#0a1120");
                surface = Hex("#0e1729");
                surfaceAlt = Hex("#142038");
                border = Hex("#1e2d48");
                borderSubtle = Hex("#16233a");
                accent = Hex("#6366f1");
                accentSoft = Hex("#2d2c72");
                iconAccent = Hex("#818cf8");
                navActiveBg = Hex("#226366f1");
                text = Hex("#f8fafc");
                textMuted = Hex("#94a3b8");
                hover = Hex("#12FFFFFF");
                break;
        }

        if (light)
        {
            success = Hex("#15803d");
            successSoft = Hex("#dcfce7");
            warning = Hex("#b45309");
            warningSoft = Hex("#fef3c7");
            danger = Hex("#b91c1c");
            dangerSoft = Hex("#fee2e2");
        }
        else if (IsIosTheme)
        {
            success = Hex("#30d158");
            successSoft = Hex("#12301d");
            warning = Hex("#ff9f0a");
            warningSoft = Hex("#38260c");
            danger = Hex("#ff453a");
            dangerSoft = Hex("#3b1615");
        }
        else
        {
            success = Hex("#4ade80");
            successSoft = Hex("#143326");
            warning = Hex("#fbbf24");
            warningSoft = Hex("#382813");
            danger = Hex("#f87171");
            dangerSoft = Hex("#3b181d");
        }

        SetBrush("App.Background", bg);
        SetBrush("App.Sidebar", sidebar);
        SetBrush("App.Surface", surface);
        SetBrush("App.SurfaceAlt", surfaceAlt);
        SetBrush("App.Border", border);
        SetBrush("App.BorderSubtle", borderSubtle);
        SetBrush("App.Accent", accent);
        SetBrush("App.AccentSoft", accentSoft);
        SetBrush("App.IconAccent", iconAccent);
        SetBrush("App.NavActiveBg", navActiveBg);
        SetBrush("App.Text", text);
        SetBrush("App.TextMuted", textMuted);
        SetBrush("App.Hover", hover);
        SetBrush("App.AccentText", accentText);

        SetBrush("App.Success", success);
        SetBrush("App.SuccessSoft", successSoft);
        SetBrush("App.Warning", warning);
        SetBrush("App.WarningSoft", warningSoft);
        SetBrush("App.Danger", danger);
        SetBrush("App.DangerSoft", dangerSoft);

        app.Resources["App.CardRadius"] = new CornerRadius(IsIosTheme || IsAppleStyle ? 20 : 8);
        app.Resources["App.ControlRadius"] = new CornerRadius(IsIosTheme || IsAppleStyle ? 12 : 6);

        UpdateLiquidGlassBrushes(app);

        TryApplyWpfUiTheme(light);
        TryApplyWpfUiAccent(accent, light);

        try
        {
            ApplyWpfUiOverrides(bg, sidebar, surface, surfaceAlt, border, accent, accentSoft,
                text, textMuted, hover, accentText);
        }
        catch
        {
            // Оформление не должно ломать запуск приложения.
        }

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void ApplyFromSavedSettings()
    {
        var s = AppSettings.Load();
        CurrentUiStyle = NormalizeUiStyle(s.UiStyle);
        Apply(s.ThemeId);
        ApplyUiStyle(s.UiStyle);
    }

    public static string NormalizeUiStyle(string? style)
        => string.Equals(style, StyleWindows, StringComparison.OrdinalIgnoreCase) ? StyleWindows : StyleApple;

    public static void ApplyUiStyle(string? style)
    {
        CurrentUiStyle = NormalizeUiStyle(style);
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        var apple = IsAppleStyle;
        app.Resources["App.CardRadius"] = new CornerRadius(apple || IsIosTheme ? 20 : 8);
        app.Resources["App.ControlRadius"] = new CornerRadius(apple || IsIosTheme ? 12 : 6);
        app.Resources["ControlCornerRadius"] = new CornerRadius(apple ? 12 : 4);

        UpdateLiquidGlassBrushes(app);

        if (apple && app.TryFindResource("AppleToggleSwitchStyle") is Style appleToggle)
            app.Resources[typeof(Wpf.Ui.Controls.ToggleSwitch)] = appleToggle;
        else
            app.Resources.Remove(typeof(Wpf.Ui.Controls.ToggleSwitch));

        if (apple && app.TryFindResource("AppleCardStyle") is Style appleCard)
            app.Resources[typeof(Wpf.Ui.Controls.Card)] = appleCard;
        else
            app.Resources.Remove(typeof(Wpf.Ui.Controls.Card));

        if (apple && app.TryFindResource("AppleButtonStyle") is Style appleButton)
            app.Resources[typeof(Wpf.Ui.Controls.Button)] = appleButton;
        else
            app.Resources.Remove(typeof(Wpf.Ui.Controls.Button));

        UiStyleChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void UpdateLiquidGlassBrushes(Application app)
    {
        if (!IsAppleStyle)
        {
            app.Resources["App.GlassRimBrush"] = app.Resources["App.Border"];
            app.Resources["App.GlassSurfaceBrush"] = app.Resources["App.Surface"];
            app.Resources["App.GlassSidebarBrush"] = app.Resources["App.Sidebar"];
            app.Resources["App.GlassPillBrush"] = app.Resources["App.SurfaceAlt"];
            app.Resources["App.AppleCanvasBrush"] = app.Resources["App.Background"];
            app.Resources["App.PrimaryButtonBrush"] = app.Resources["App.Accent"];
            return;
        }

        var light = IsLightTheme;
        if (light)
        {
            app.Resources["App.GlassRimBrush"] = MakeLinear("#EEFFFFFF", "#66FFFFFF", "#BBFFFFFF", 0, 0, 1, 1);
            app.Resources["App.GlassSurfaceBrush"] = MakeLinear("#EBFFFFFF", "#D4FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.GlassSidebarBrush"] = MakeLinear("#E4F5F7FC", "#D0E9EEF8", null, 0, 0, 0, 1);
            app.Resources["App.GlassPillBrush"] = MakeLinear("#AAFFFFFF", "#72FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.AppleCanvasBrush"] = MakeLinear("#E7EEF9", "#F5F7FC", "#EDF1F8", 0, 0, 1, 1);
        }
        else if (CurrentThemeId == ThemeAppleMidnight)
        {
            app.Resources["App.GlassRimBrush"] = MakeLinear("#54FFFFFF", "#18FFFFFF", "#2CFFFFFF", 0, 0, 1, 1);
            app.Resources["App.GlassSurfaceBrush"] = MakeLinear("#D41A233D", "#BA131A2E", null, 0, 0, 0, 1);
            app.Resources["App.GlassSidebarBrush"] = MakeLinear("#E6141B30", "#D40E1324", null, 0, 0, 0, 1);
            app.Resources["App.GlassPillBrush"] = MakeLinear("#28FFFFFF", "#14FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.AppleCanvasBrush"] = MakeLinear("#151F42", "#080C18", "#101630", 0, 0, 1, 1);
        }
        else if (CurrentThemeId == ThemeAppleTitanium)
        {
            app.Resources["App.GlassRimBrush"] = MakeLinear("#52FFF5E6", "#16FFFFFF", "#2AFFF0D8", 0, 0, 1, 1);
            app.Resources["App.GlassSurfaceBrush"] = MakeLinear("#D4272420", "#BA1D1B18", null, 0, 0, 0, 1);
            app.Resources["App.GlassSidebarBrush"] = MakeLinear("#E6211F1B", "#D4171512", null, 0, 0, 0, 1);
            app.Resources["App.GlassPillBrush"] = MakeLinear("#26FFFFFF", "#12FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.AppleCanvasBrush"] = MakeLinear("#211C15", "#0E0D0B", "#181511", 0, 0, 1, 1);
        }
        else if (CurrentThemeId == ThemeAppleRose)
        {
            app.Resources["App.GlassRimBrush"] = MakeLinear("#54FFE8F0", "#18FFFFFF", "#2CFFE0EC", 0, 0, 1, 1);
            app.Resources["App.GlassSurfaceBrush"] = MakeLinear("#D42A1B25", "#BA1E121A", null, 0, 0, 0, 1);
            app.Resources["App.GlassSidebarBrush"] = MakeLinear("#E623161F", "#D4170E14", null, 0, 0, 0, 1);
            app.Resources["App.GlassPillBrush"] = MakeLinear("#26FFFFFF", "#12FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.AppleCanvasBrush"] = MakeLinear("#291320", "#0E080C", "#1E0F18", 0, 0, 1, 1);
        }
        else
        {
            app.Resources["App.GlassRimBrush"] = MakeLinear("#54FFFFFF", "#16FFFFFF", "#2CFFFFFF", 0, 0, 1, 1);
            app.Resources["App.GlassSurfaceBrush"] = MakeLinear("#D422232A", "#BA18191F", null, 0, 0, 0, 1);
            app.Resources["App.GlassSidebarBrush"] = MakeLinear("#E61D1E25", "#D414151B", null, 0, 0, 0, 1);
            app.Resources["App.GlassPillBrush"] = MakeLinear("#28FFFFFF", "#14FFFFFF", null, 0, 0, 0, 1);
            app.Resources["App.AppleCanvasBrush"] = MakeLinear("#101729", "#07080C", "#0D101B", 0, 0, 1, 1);
        }

        var accentColor = (app.Resources["App.Accent"] as SolidColorBrush)?.Color ?? Color.FromRgb(10, 132, 255);
        var topAccent = Color.FromArgb(255,
            (byte)Math.Min(255, accentColor.R + 28),
            (byte)Math.Min(255, accentColor.G + 24),
            (byte)Math.Min(255, accentColor.B + 20));
        var bottomAccent = Color.FromArgb(255,
            (byte)Math.Max(0, accentColor.R - 18),
            (byte)Math.Max(0, accentColor.G - 18),
            (byte)Math.Max(0, accentColor.B - 14));
        var primaryBrush = new LinearGradientBrush(topAccent, bottomAccent, new Point(0, 0), new Point(0, 1));
        primaryBrush.Freeze();
        app.Resources["App.PrimaryButtonBrush"] = primaryBrush;
    }

    private static LinearGradientBrush MakeLinear(string c1, string c2, string? c3, double x1, double y1, double x2, double y2)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(x1, y1), EndPoint = new Point(x2, y2) };
        brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c1)!, 0));
        if (!string.IsNullOrEmpty(c3))
        {
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c2)!, 0.45));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c3)!, 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(c2)!, 1));
        }
        brush.Freeze();
        return brush;
    }

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
            // На этапе старта окна ещё нет — словари уже переключены.
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

    private static void ApplyWpfUiOverrides(
        Brush background, Brush sidebar, Brush surface, Brush surfaceAlt, Brush border,
        Brush accent, Brush accentSoft, Brush text, Brush textMuted, Brush hover, Brush accentText)
    {
        var app = Application.Current;
        if (app?.Resources == null)
            return;

        if (_overrideDict != null && app.Resources.MergedDictionaries.Contains(_overrideDict))
            app.Resources.MergedDictionaries.Remove(_overrideDict);

        var dict = new ResourceDictionary();

        // Фон окна и слоёв
        Set(dict, "ApplicationBackgroundBrush", background);
        Set(dict, "SolidBackgroundFillColorBaseBrush", background);
        Set(dict, "SolidBackgroundFillColorBaseAltBrush", sidebar);
        Set(dict, "SolidBackgroundFillColorSecondaryBrush", surface);
        Set(dict, "SolidBackgroundFillColorTertiaryBrush", surfaceAlt);
        Set(dict, "SolidBackgroundFillColorQuarternaryBrush", surfaceAlt);
        Set(dict, "LayerFillColorDefaultBrush", surface);
        Set(dict, "LayerOnAcrylicFillColorDefaultBrush", surface);
        Set(dict, "AcrylicBackgroundFillColorDefaultBrush", surface);

        // Карточки
        Set(dict, "CardBackgroundFillColorDefaultBrush", surface);
        Set(dict, "CardBackgroundFillColorSecondaryBrush", surfaceAlt);
        Set(dict, "CardStrokeColorDefaultBrush", border);

        // Контролы
        Set(dict, "ControlFillColorDefaultBrush", surfaceAlt);
        Set(dict, "ControlFillColorSecondaryBrush", surface);
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

        // Акцент
        Set(dict, "AccentFillColorDefaultBrush", accent);
        Set(dict, "AccentFillColorSecondaryBrush", accentSoft);
        Set(dict, "AccentFillColorTertiaryBrush", accentSoft);
        Set(dict, "AccentFillColorDisabledBrush", accentSoft);
        Set(dict, "AccentTextFillColorPrimaryBrush", accentText);
        Set(dict, "AccentTextFillColorSecondaryBrush", accentText);
        Set(dict, "AccentTextFillColorTertiaryBrush", accentText);

        if (IsIosTheme)
        {
            var iosGreen = Hex("#30d158");
            var iosGreenHover = Hex("#34c759");
            var iosGreenPress = Hex("#28b84c");
            Set(dict, "ToggleSwitchFillOn", iosGreen);
            Set(dict, "ToggleSwitchFillOnPointerOver", iosGreenHover);
            Set(dict, "ToggleSwitchFillOnPressed", iosGreenPress);
        }

        app.Resources.MergedDictionaries.Add(dict);
        _overrideDict = dict;
    }

    private static ResourceDictionary? _overrideDict;

    private static void Set(ResourceDictionary resources, string key, Brush brush) => resources[key] = brush;

    private static void SetBrush(string key, Brush brush) => Application.Current.Resources[key] = brush;

    private static SolidColorBrush Hex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        c.A = c.A == 0 ? (byte)255 : c.A;
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
