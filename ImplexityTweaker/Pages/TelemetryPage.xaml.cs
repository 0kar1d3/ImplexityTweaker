using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using Wpf.Ui.Controls;

namespace ImplexityTweaker.Pages;

public partial class TelemetryPage : Page
{
    public TelemetryPage()
    {
        InitializeComponent();
        ApplyAllSafeDefaults();
    }

    private void ApplyAllSafeDefaults()
    {
        TogglePolicyTelemetry.IsChecked = true;
        ToggleDiagTrack.IsChecked = true;
        ToggleDmwappush.IsChecked = true;
        ToggleCompatAppraiser.IsChecked = true;
        ToggleDeviceCensus.IsChecked = true;
        ToggleAdvertisingId.IsChecked = true;
        ToggleTailoredExperiences.IsChecked = true;
        ToggleStartSuggestions.IsChecked = true;
        ToggleFeedbackFrequency.IsChecked = true;
        ToggleWerConsent.IsChecked = true;
    }

    private async void ApplySelected_Click(object sender, RoutedEventArgs e)
    {
        await RunBuildAsync(onlyChecked: true);
    }

    private async void ApplyAllSafe_Click(object sender, RoutedEventArgs e)
    {
        ApplyAllSafeDefaults();
        await RunBuildAsync(onlyChecked: false);
    }

    private async Task RunBuildAsync(bool onlyChecked)
    {
        var parts = new List<string>();

        void Add(bool condition, string ps)
        {
            if (condition)
                parts.Add(ps);
        }

        bool On(ToggleSwitch t) => !onlyChecked || t.IsChecked == true;

        Add(On(TogglePolicyTelemetry),
            "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -Type DWord -Value 0 -Force");

        Add(On(ToggleDiagTrack),
            "Stop-Service -Name DiagTrack -Force -ErrorAction SilentlyContinue; " +
            "Set-Service -Name DiagTrack -StartupType Disabled -ErrorAction SilentlyContinue; " +
            "sc.exe config DiagTrack start= disabled");

        Add(On(ToggleDmwappush),
            "Stop-Service -Name dmwappushservice -Force -ErrorAction SilentlyContinue; " +
            "Set-Service -Name dmwappushservice -StartupType Disabled -ErrorAction SilentlyContinue; " +
            "sc.exe config dmwappushservice start= disabled");

        Add(On(ToggleCompatAppraiser), "schtasks.exe /Change /TN '\\Microsoft\\Windows\\Application Experience\\Microsoft Compatibility Appraiser' /Disable");

        Add(On(ToggleDeviceCensus), "schtasks.exe /Change /TN '\\Microsoft\\Windows\\Device Information\\Device' /Disable");

        Add(On(ToggleAdvertisingId),
            "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Type DWord -Value 0 -Force");

        Add(On(ToggleTailoredExperiences),
            "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Privacy' -Name TailoredExperiencesWithDiagnosticDataEnabled -Type DWord -Value 0 -Force");

        Add(On(ToggleStartSuggestions),
            "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced' -Name Start_IrisRecommendations -Type DWord -Value 0 -Force");

        Add(On(ToggleFeedbackFrequency),
            "New-Item -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Name PeriodInNanoSeconds -Type QWord -Value 0 -Force; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Siuf\\Rules' -Name NumberOfSIUFInPeriod -Type DWord -Value 0 -Force");

        Add(On(ToggleWerConsent),
            "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\Windows Error Reporting' -Force | Out-Null; " +
            "Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\Windows Error Reporting' -Name Disabled -Type DWord -Value 1 -Force");

        if (parts.Count == 0)
        {
            await Dialogs.ShowInfoAsync("Нечего применять", "Включите хотя бы один параметр.");
            return;
        }

        var script = string.Join("; ", parts);
        var result = await CommandRunner.RunPowerShellAsync(script);

        if (result.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Выбранные параметры телеметрии применены.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
    }
}
