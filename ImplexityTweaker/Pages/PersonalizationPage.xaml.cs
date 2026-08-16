using Microsoft.Win32;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class PersonalizationPage : Page
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string LockScreenPolicies = @"Software\Policies\Microsoft\Windows\System";
    private const string AccessibilityStickyKeys = @"Control Panel\Accessibility\StickyKeys";

    public PersonalizationPage()
    {
        InitializeComponent();
        var s = AppSettings.Load();

        using var p = Registry.CurrentUser.OpenSubKey(Personalize, false);
        var enableTransparency = p?.GetValue("EnableTransparency") as int? ?? 1;
        // прозрачность
        TransparencyToggle.IsChecked = s.Transparency ?? (enableTransparency == 0);

        var appsUseLightTheme = p?.GetValue("AppsUseLightTheme") as int? ?? 1;
        var systemUsesLightTheme = p?.GetValue("SystemUsesLightTheme") as int? ?? 1;
        DarkThemeToggle.IsChecked = s.DarkTheme ?? (appsUseLightTheme == 0 && systemUsesLightTheme == 0);

        using var a = Registry.CurrentUser.OpenSubKey(Advanced, false);
        EndTaskToggle.IsChecked = s.EndTask ?? ((a?.GetValue("TaskbarEndTask") as int? ?? 0) == 1);

        // TaskbarSi: 0 = small, 1 = default
        SmallWindowButtonsToggle.IsChecked = s.SmallWindowButtons ?? ((a?.GetValue("TaskbarSi") as int? ?? 1) == 0);

        // lock screen blur policy
        try
        {
            using var sys = Registry.LocalMachine.OpenSubKey(LockScreenPolicies, false);
            DisableLockScreenBlurToggle.IsChecked = s.DisableLockScreenBlur ?? ((sys?.GetValue("DisableAcrylicBackgroundOnLogon") as int? ?? 0) == 1);
        }
        catch
        {
            DisableLockScreenBlurToggle.IsChecked = s.DisableLockScreenBlur ?? false;
        }

        // boot UX toggles — восстанавливаем из конфига, иначе по умолчанию выключены.
        SuperDetailedInfoToggle.IsChecked = s.SuperDetailedInfo ?? false;
        DisableBootLogoToggle.IsChecked = s.DisableBootLogo ?? false;
        DisableBootAnimationToggle.IsChecked = s.DisableBootAnimation ?? false;
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        // Сохраняем состояние переключателей в конфиг.
        var s = AppSettings.Load();
        s.Transparency = TransparencyToggle.IsChecked == true;
        s.EndTask = EndTaskToggle.IsChecked == true;
        s.SmallWindowButtons = SmallWindowButtonsToggle.IsChecked == true;
        s.DisableLockScreenBlur = DisableLockScreenBlurToggle.IsChecked == true;
        s.DarkTheme = DarkThemeToggle.IsChecked == true;
        s.SuperDetailedInfo = SuperDetailedInfoToggle.IsChecked == true;
        s.DisableBootLogo = DisableBootLogoToggle.IsChecked == true;
        s.DisableBootAnimation = DisableBootAnimationToggle.IsChecked == true;
        s.Save();

        using (var p = Registry.CurrentUser.CreateSubKey(Personalize, true))
        {
            // прозрачность
            p?.SetValue("EnableTransparency", TransparencyToggle.IsChecked == true ? 0 : 1, RegistryValueKind.DWord);

            // темная тема
            var appsLight = DarkThemeToggle.IsChecked == true ? 0 : 1;
            var sysLight = DarkThemeToggle.IsChecked == true ? 0 : 1;
            p?.SetValue("AppsUseLightTheme", appsLight, RegistryValueKind.DWord);
            p?.SetValue("SystemUsesLightTheme", sysLight, RegistryValueKind.DWord);
        }

        using (var a = Registry.CurrentUser.CreateSubKey(Advanced, true))
        {
            a?.SetValue("TaskbarEndTask", EndTaskToggle.IsChecked == true ? 1 : 0, RegistryValueKind.DWord);

            a?.SetValue("TaskbarSi", SmallWindowButtonsToggle.IsChecked == true ? 0 : 1, RegistryValueKind.DWord);
        }

        if (DisableLockScreenBlurToggle.IsChecked == true)
        {
            // Requires admin. If it fails, user will get error output from command.
            var r = await CommandRunner.RunPowerShellAsync(
                "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Force | Out-Null; " +
                "reg add 'HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' /v DisableAcrylicBackgroundOnLogon /t REG_DWORD /d 1 /f");
            if (r.ExitCode != 0)
                await Dialogs.ShowErrorAsync("Ошибка", string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
        }
        else
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(LockScreenPolicies, throwOnMissingSubKey: false);
            }
            catch { /* ignore */ }
        }

        if (SuperDetailedInfoToggle.IsChecked == true)
            Process.Start(new ProcessStartInfo("msinfo32.exe") { UseShellExecute = true });

        // Boot UX tweaks require admin privileges and often a reboot.
        if (DisableBootLogoToggle.IsChecked == true || DisableBootAnimationToggle.IsChecked == true)
        {
            var enable = "on";
            var r = await CommandRunner.RunPowerShellAsync($"bcdedit /set {{globalsettings}} bootuxdisabled {enable}");
            if (r.ExitCode != 0)
                await Dialogs.ShowErrorAsync("Ошибка", string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
        }

        await Dialogs.ShowInfoAsync("Готово", "Параметры персонализации обновлены.");
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:taskbar") { UseShellExecute = true });
    }
}
