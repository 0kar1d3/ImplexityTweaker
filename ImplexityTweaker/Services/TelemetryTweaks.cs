namespace ImplexityTweaker.Services;

/// <summary>
/// Скрипты отключения/возврата телеметрии. Тот же набор команд, что применяет
/// страница «Телеметрия», но оформлен как сервис — им пользуются пресеты
/// («Приватность и без рекламы», «Безопасность: включить защиту»).
/// </summary>
public static class TelemetryTweaks
{
    /// <summary>Какие пункты телеметрии включать в скрипт.</summary>
    public sealed record Selection(
        bool Policy,
        bool DiagTrack,
        bool Dmwappush,
        bool CompatAppraiser,
        bool DeviceCensus,
        bool AdvertisingId,
        bool TailoredExperiences,
        bool StartSuggestions,
        bool FeedbackFrequency,
        bool WerConsent)
    {
        /// <summary>Всё сразу (полный «тихий» профиль).</summary>
        public static Selection All { get; } = new(true, true, true, true, true, true, true, true, true, true);

        /// <summary>Ничего не выбрано (скрипт будет пустым).</summary>
        public static Selection None { get; } = new(false, false, false, false, false, false, false, false, false, false);
    }

    /// <summary>Собирает PowerShell-скрипт для выбранных пунктов (пустая строка — нечего делать).</summary>
    public static string BuildDisableScript(Selection s)
    {
        var parts = new List<string>();

        if (s.Policy)
            parts.Add(
                "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -Type DWord -Value 0 -Force");

        if (s.DiagTrack)
            parts.Add(
                "Stop-Service -Name DiagTrack -Force -ErrorAction SilentlyContinue; " +
                "Set-Service -Name DiagTrack -StartupType Disabled -ErrorAction SilentlyContinue; " +
                "sc.exe config DiagTrack start= disabled");

        if (s.Dmwappush)
            parts.Add(
                "Stop-Service -Name dmwappushservice -Force -ErrorAction SilentlyContinue; " +
                "Set-Service -Name dmwappushservice -StartupType Disabled -ErrorAction SilentlyContinue; " +
                "sc.exe config dmwappushservice start= disabled");

        if (s.CompatAppraiser)
            parts.Add("schtasks.exe /Change /TN '\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser' /Disable");

        if (s.DeviceCensus)
            parts.Add("schtasks.exe /Change /TN '\\Microsoft\\Windows\\Device Information\\Device' /Disable");

        if (s.AdvertisingId)
            parts.Add(
                "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Type DWord -Value 0 -Force");

        if (s.TailoredExperiences)
            parts.Add(
                "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Name TailoredExperiencesWithDiagnosticDataEnabled -Type DWord -Value 0 -Force");

        if (s.StartSuggestions)
            parts.Add(
                "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name Start_IrisRecommendations -Type DWord -Value 0 -Force");

        if (s.FeedbackFrequency)
            parts.Add(
                "New-Item -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Name PeriodInNanoSeconds -Type QWord -Value 0 -Force; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Name NumberOfSIUFInPeriod -Type DWord -Value 0 -Force");

        if (s.WerConsent)
            parts.Add(
                "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\Windows Error Reporting' -Force | Out-Null; " +
                "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\Windows Error Reporting' -Name Disabled -Type DWord -Value 1 -Force");

        return string.Join("; ", parts);
    }

    /// <summary>Скрипт возврата телеметрии к стандартным значениям Windows.</summary>
    public static string BuildRestoreScript()
    {
        var parts = new List<string>
        {
            "Remove-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -ErrorAction SilentlyContinue",
            "sc.exe config DiagTrack start= auto",
            "Start-Service -Name DiagTrack -ErrorAction SilentlyContinue",
            "sc.exe config dmwappushservice start= demand",
            "schtasks.exe /Change /TN '\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser' /Enable",
            "schtasks.exe /Change /TN '\\Microsoft\\Windows\\Device Information\\Device' /Enable",
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Type DWord -Value 1 -Force -ErrorAction SilentlyContinue",
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Name TailoredExperiencesWithDiagnosticDataEnabled -Type DWord -Value 1 -Force -ErrorAction SilentlyContinue",
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name Start_IrisRecommendations -Type DWord -Value 1 -Force -ErrorAction SilentlyContinue",
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Name NumberOfSIUFInPeriod -Type DWord -Value 1 -Force -ErrorAction SilentlyContinue",
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\Windows Error Reporting' -Name Disabled -Type DWord -Value 0 -Force -ErrorAction SilentlyContinue",
        };

        return string.Join("; ", parts);
    }

    /// <summary>Применяет скрипт отключения выбранных пунктов и сохраняет их в настройки.</summary>
    public static async Task<(int ExitCode, string Output, string Error)> DisableAsync(Selection selection)
    {
        var script = BuildDisableScript(selection);
        if (string.IsNullOrWhiteSpace(script))
            return (0, string.Empty, string.Empty);

        var result = await CommandRunner.RunPowerShellAsync(script);
        SaveSelectionToSettings(selection);
        return result;
    }

    /// <summary>Возвращает телеметрию к значениям по умолчанию.</summary>
    public static async Task<(int ExitCode, string Output, string Error)> RestoreAsync()
    {
        var result = await CommandRunner.RunPowerShellAsync(BuildRestoreScript());

        // В «Телеметрии» галочка = «отключить», поэтому после возврата все флаги снимаем.
        var s = AppSettings.Load();
        s.TelemetryPolicy = false;
        s.TelemetryDiagTrack = false;
        s.TelemetryDmwappush = false;
        s.TelemetryCompatAppraiser = false;
        s.TelemetryDeviceCensus = false;
        s.TelemetryAdvertisingId = false;
        s.TelemetryTailoredExperiences = false;
        s.TelemetryStartSuggestions = false;
        s.TelemetryFeedbackFrequency = false;
        s.TelemetryWerConsent = false;
        s.Save();

        return result;
    }

    /// <summary>Сохраняет выбранные пункты в настройки (чтобы страница «Телеметрия» показывала то же).</summary>
    public static void SaveSelectionToSettings(Selection s)
    {
        var st = AppSettings.Load();
        st.TelemetryPolicy = s.Policy;
        st.TelemetryDiagTrack = s.DiagTrack;
        st.TelemetryDmwappush = s.Dmwappush;
        st.TelemetryCompatAppraiser = s.CompatAppraiser;
        st.TelemetryDeviceCensus = s.DeviceCensus;
        st.TelemetryAdvertisingId = s.AdvertisingId;
        st.TelemetryTailoredExperiences = s.TailoredExperiences;
        st.TelemetryStartSuggestions = s.StartSuggestions;
        st.TelemetryFeedbackFrequency = s.FeedbackFrequency;
        st.TelemetryWerConsent = s.WerConsent;
        st.Save();
    }
}
