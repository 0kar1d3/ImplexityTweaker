using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Сведения о версии Windows (название, сборка, DisplayVersion, редакция) и проверка
/// доступности твиков на текущей системе. Значения читаются из реестра один раз и кэшируются.
/// Именно этот сервис используют пресеты и глобальный поиск, чтобы не предлагать
/// твики, которых на этой сборке нет.
/// </summary>
public static class WindowsVersionService
{
    public const int Windows11MinBuild = 22000;

    /// <summary>Windows 11 22H2 — с этой сборки появилась кнопка «Завершить задачу» и т.п.</summary>
    public const int Windows11_22H2Build = 22621;

    /// <summary>Результат проверки: можно ли применять твик на этой системе и почему нет.</summary>
    public sealed record Availability(bool IsAvailable, string Reason)
    {
        public static readonly Availability Ok = new(true, string.Empty);
    }

    private static readonly Lazy<(string ProductName, int Build, int Ubr, string DisplayVersion, string EditionId)> Cached = new(Read);

    public static string ProductName => Cached.Value.ProductName;

    /// <summary>Номер сборки, например 26100.</summary>
    public static int Build => Cached.Value.Build;

    /// <summary>Ревизия сборки (UBR), например 1742.</summary>
    public static int Ubr => Cached.Value.Ubr;

    /// <summary>Версия вида 22H2 / 23H2 / 24H2 (может быть пустой на старых сборках).</summary>
    public static string DisplayVersion => Cached.Value.DisplayVersion;

    /// <summary>Идентификатор редакции из реестра: Core, Professional, Enterprise, Education…</summary>
    public static string EditionId => Cached.Value.EditionId;

    public static bool IsWindows11 => Build >= Windows11MinBuild;

    public static bool IsWindows10 => Build is > 0 and < Windows11MinBuild;

    /// <summary>Номер сборки в виде 26100.1742 (или «—», если определить не удалось).</summary>
    public static string BuildLabel => Build <= 0 ? "—" : Ubr > 0 ? $"{Build}.{Ubr}" : Build.ToString();

    /// <summary>Например: Windows 11 24H2 (26100.1742).</summary>
    public static string VersionLabel
    {
        get
        {
            var family = Build <= 0 ? "Windows" : IsWindows11 ? "Windows 11" : "Windows 10";
            var version = string.IsNullOrWhiteSpace(DisplayVersion) ? BuildLabel : $"{DisplayVersion} ({BuildLabel})";
            return $"{family} {version}".Trim();
        }
    }

    /// <summary>Название редакции на текущем языке (Professional → Профессиональная).</summary>
    public static string EditionLabel => EditionId switch
    {
        "Core" or "CoreSingleLanguage" or "CoreCountrySpecific" => LocalizationService.T("Домашняя"),
        "Professional" or "ProfessionalN" => LocalizationService.T("Профессиональная"),
        "ProfessionalEducation" or "ProfessionalEducationN" => LocalizationService.T("Профессиональная для образования"),
        "ProfessionalWorkstation" or "ProfessionalWorkstationN" => LocalizationService.T("Профессиональная для рабочих станций"),
        "Enterprise" or "EnterpriseN" => LocalizationService.T("Корпоративная"),
        "Education" or "EducationN" => LocalizationService.T("Для образования"),
        "EnterpriseS" or "IoTEnterpriseS" => LocalizationService.T("Корпоративная LTSC/IoT"),
        "IoTEnterprise" => LocalizationService.T("IoT Корпоративная"),
        "" => LocalizationService.T("Неизвестно"),
        _ => EditionId,
    };

    /// <summary>Строка для шапки страниц: «Система: Windows 11 24H2 (26100.1742) · Профессиональная».</summary>
    public static string DescribeSystem()
        => LocalizationService.TF("Система: {0} · {1}", VersionLabel, EditionLabel);

    /// <summary>
    /// Проверяет доступность твика: минимум/максимум сборки, «только Windows 11»
    /// и требуемую редакцию (например Professional). Причина отказа — на текущем языке.
    /// </summary>
    public static Availability Check(int minBuild = 0, int maxBuild = 0, bool windows11Only = false, string? requiredEdition = null)
    {
        var actual = string.IsNullOrWhiteSpace(DisplayVersion) ? BuildLabel : $"{DisplayVersion} ({BuildLabel})";

        if (windows11Only && !IsWindows11)
            return new Availability(false, LocalizationService.TF("Требуется Windows 11 (сборка 22000+), у вас {0}.", actual));

        if (minBuild > 0 && Build > 0 && Build < minBuild)
            return new Availability(false, LocalizationService.TF("Требуется сборка Windows {0} или новее, у вас {1}.", minBuild, actual));

        if (maxBuild > 0 && Build > maxBuild)
            return new Availability(false, LocalizationService.TF("Доступно только до сборки {0}, у вас {1}.", maxBuild, actual));

        if (!string.IsNullOrWhiteSpace(requiredEdition)
            && !EditionId.StartsWith(requiredEdition, StringComparison.OrdinalIgnoreCase))
            return new Availability(false, LocalizationService.TF("Требуется редакция Windows {0} или старше, у вас {1}.", requiredEdition, EditionLabel));

        return Availability.Ok;
    }

    private static (string ProductName, int Build, int Ubr, string DisplayVersion, string EditionId) Read()
    {
        string product = string.Empty;
        string display = string.Empty;
        string edition = string.Empty;
        int build = 0;
        int ubr = 0;

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                product = key.GetValue("ProductName")?.ToString() ?? string.Empty;
                edition = key.GetValue("EditionID")?.ToString() ?? string.Empty;
                display = key.GetValue("DisplayVersion")?.ToString() ?? string.Empty;

                build = ParseNumber(key.GetValue("CurrentBuildNumber")?.ToString());
                if (build <= 0)
                    build = ParseNumber(key.GetValue("CurrentBuild")?.ToString());
                ubr = ParseNumber(key.GetValue("UBR")?.ToString());
            }
        }
        catch
        {
            // Реестр недоступен — вернём то, что успели прочитать.
        }

        return (product, build, ubr, display, edition);
    }

    private static int ParseNumber(string? value)
        => int.TryParse(value, out var result) ? result : 0;
}
