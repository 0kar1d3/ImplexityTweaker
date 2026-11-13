using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>
/// Простая система локализации. Ключом служит русская строка-исходник,
/// значением — перевод для выбранного языка. Если перевода нет — возвращается
/// русская строка (fallback). Переключение языка через ChangeLanguage.
/// Переводы загружаются из встроенных JSON-ресурсов Localization/{code}.json.
/// </summary>
public static class LocalizationService
{
    public sealed record LanguageInfo(string Code, string NativeName, string EnglishName);

    public static readonly IReadOnlyList<LanguageInfo> Languages =
    [
        new("ru", "Русский", "Russian"),
        new("en", "English", "English"),
        new("uk", "Українська", "Ukrainian"),
        new("de", "Deutsch", "German"),
        new("sk", "Slovenčina", "Slovak"),
    ];

    public static string CurrentCode { get; private set; } = "ru";

    /// <summary>Вызывается после смены языка (для пересборки навигации и страниц).</summary>
    public static event EventHandler? LanguageChanged;

    private static readonly Dictionary<string, Dictionary<string, string>> _translations = LoadTranslations();

    public static void Initialize(string? code)
    {
        // Если язык не выбран пользователем — определяем его из системы.
        CurrentCode = string.IsNullOrWhiteSpace(code) ? ResolveSystemLanguage() : Normalize(code);
    }

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    /// <summary>
    /// Определяет язык интерфейса из языка системы (Windows).
    /// Если языка системы нет в списке — возвращается английский,
    /// для казахского (kk) — русский.
    /// </summary>
    public static string ResolveSystemLanguage()
    {
        string code = "en";
        try
        {
            code = CultureInfo.GetCultureInfo(GetUserDefaultUILanguage())
                .TwoLetterISOLanguageName.ToLowerInvariant();
        }
        catch
        {
            // остаёмся на fallback ниже
        }

        if (code == "kk")
            return "ru";

        foreach (var l in Languages)
        {
            if (l.Code == code)
                return l.Code;
        }
        return "en";
    }

    public static void ChangeLanguage(string code)
    {
        var newCode = Normalize(code);
        if (newCode == CurrentCode)
            return;
        CurrentCode = newCode;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Переводит русскую строку на текущий язык (fallback — русский).</summary>
    public static string T(string russian) => Translate(CurrentCode, russian);

    /// <summary>То же, что T, но с форматированием: T("Через {0} мин.", 5).</summary>
    public static string TF(string russianFormat, params object[] args)
        => string.Format(Translate(CurrentCode, russianFormat), args);

    private static string Translate(string code, string russian)
    {
        if (string.IsNullOrEmpty(russian) || code == "ru")
            return russian;
        if (_translations.TryGetValue(code, out var dict)
            && dict.TryGetValue(russian, out var translated)
            && !string.IsNullOrWhiteSpace(translated))
        {
            return translated;
        }
        return russian;
    }

    private static string Normalize(string? code)
    {
        foreach (var l in Languages)
        {
            if (string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
                return l.Code;
        }
        return "ru";
    }

    private static Dictionary<string, Dictionary<string, string>> LoadTranslations()
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var assembly = Assembly.GetExecutingAssembly();

        foreach (var lang in Languages)
        {
            if (lang.Code == "ru")
                continue;

            var resourceName = $"ImplexityTweaker.Localization.{lang.Code}.json";
            try
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    AppLogger.Warn("Localization", $"Embedded resource not found: {resourceName}");
                    continue;
                }

                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                if (dict != null)
                    result[lang.Code] = dict;
            }
            catch (Exception ex)
            {
                AppLogger.Error("Localization", $"Failed to load {resourceName}", ex);
            }
        }

        return result;
    }
}
