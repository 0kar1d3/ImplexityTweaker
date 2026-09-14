using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class FirstSetupPage : Page
{
    public FirstSetupPage()
    {
        InitializeComponent();
        var s = AppSettings.Load();
        ShowHiddenToggle.IsChecked = s.ShowHidden ?? true;
        ShowExtensionsToggle.IsChecked = s.ShowExtensions ?? true;
        ThisPcToggle.IsChecked = s.ThisPc ?? true;
        PauseWuToggle.IsChecked = s.PauseWu ?? false;
        ThisPcDesktopToggle.IsChecked = s.ThisPcDesktop ?? true;
        NoShortcutSuffixToggle.IsChecked = s.NoShortcutSuffix ?? true;
        HideTaskViewWidgetsToggle.IsChecked = s.HideTaskViewWidgets ?? false;
        ReduceAdsToggle.IsChecked = s.ReduceAds ?? true;
        EndTaskToggle.IsChecked = s.EndTask ?? false;
        DisableStickyKeysToggle.IsChecked = s.DisableStickyKeys ?? AccessibilityTweaks.GetStickyKeysDisabled();

        // Игровая оптимизация и страховка: по умолчанию включены (кроме парковки ядер).
        QuickGameModeToggle.IsChecked = s.QuickGameMode ?? true;
        QuickCoreParkingToggle.IsChecked = s.QuickCoreParking ?? false;
        QuickSnapshotToggle.IsChecked = s.QuickSnapshot ?? true;
    }

    private async void InstallDirectPlay_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("dism.exe /Online /Enable-Feature /FeatureName:DirectPlay /NoRestart");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "DirectPlay установлен (или уже установлен).");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void InstallNetFx35_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("dism.exe /Online /Enable-Feature /FeatureName:NetFx3 /All /NoRestart");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", ".NET Framework 2.0/3.0/3.5 установлен (или уже был установлен).");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void InstallOldPhotoViewer_Click(object sender, RoutedEventArgs e)
    {
        var ps = @"
$ErrorActionPreference = 'SilentlyContinue';
$cap = 'HKLM:\SOFTWARE\Microsoft\Windows Photo Viewer\Capabilities\FileAssociations';
New-Item -Path $cap -Force | Out-Null;

$mappings = @{
  '.bmp' = 'PhotoViewer.FileAssoc.BITMAP'
  '.dib' = 'PhotoViewer.FileAssoc.BITMAP'
  '.jpg' = 'PhotoViewer.FileAssoc.JPEG'
  '.jpe' = 'PhotoViewer.FileAssoc.JPEG'
  '.jpeg' = 'PhotoViewer.FileAssoc.JPEG'
  '.jxr' = 'PhotoViewer.FileAssoc.JPEG'
  '.jfif' = 'PhotoViewer.FileAssoc.JFIF'
  '.wdp' = 'PhotoViewer.FileAssoc.WDP'
  '.png' = 'PhotoViewer.FileAssoc.PNG'
  '.gif' = 'PhotoViewer.FileAssoc.TIFF'
  '.tiff' = 'PhotoViewer.FileAssoc.TIFF'
  '.tif' = 'PhotoViewer.FileAssoc.TIFF'
}

foreach ($k in $mappings.Keys) {
  New-ItemProperty -Path $cap -Name $k -PropertyType String -Value $mappings[$k] -Force | Out-Null
}";

        var r = await CommandRunner.RunPowerShellAsync(ps);
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Классический Windows Photo Viewer включен в реестре. Для выбора по файлам может потребоваться обновление/перелогин.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.ShowHidden = ShowHiddenToggle.IsChecked == true;
        s.ShowExtensions = ShowExtensionsToggle.IsChecked == true;
        s.ThisPc = ThisPcToggle.IsChecked == true;
        s.PauseWu = PauseWuToggle.IsChecked == true;
        s.ThisPcDesktop = ThisPcDesktopToggle.IsChecked == true;
        s.NoShortcutSuffix = NoShortcutSuffixToggle.IsChecked == true;
        s.HideTaskViewWidgets = HideTaskViewWidgetsToggle.IsChecked == true;
        s.ReduceAds = ReduceAdsToggle.IsChecked == true;
        s.EndTask = EndTaskToggle.IsChecked == true;
        s.DisableStickyKeys = DisableStickyKeysToggle.IsChecked == true;

        s.QuickGameMode = QuickGameModeToggle.IsChecked == true;
        s.QuickCoreParking = QuickCoreParkingToggle.IsChecked == true;
        s.QuickSnapshot = QuickSnapshotToggle.IsChecked == true;
        s.Save();

        var parts = new List<string>();

        if (ShowHiddenToggle.IsChecked == true)
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Hidden /t REG_DWORD /d 1 /f");

        if (ShowExtensionsToggle.IsChecked == true)
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v HideFileExt /t REG_DWORD /d 0 /f");

        if (ThisPcToggle.IsChecked == true)
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v LaunchTo /t REG_DWORD /d 1 /f");

        if (PauseWuToggle.IsChecked == true)
            parts.Add("Stop-Service wuauserv -Force -ErrorAction SilentlyContinue; sc.exe config wuauserv start= disabled");

        if (ThisPcDesktopToggle.IsChecked == true)
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel"" /v {20D04FE0-3AEA-1069-A2D8-08002B30309D} /t REG_DWORD /d 0 /f");

        if (NoShortcutSuffixToggle.IsChecked == true)
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer"" /v link /t REG_BINARY /d 00000000 /f");

        if (HideTaskViewWidgetsToggle.IsChecked == true)
        {
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v ShowTaskViewButton /t REG_DWORD /d 0 /f");
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarDa /t REG_DWORD /d 0 /f");
        }

        if (ReduceAdsToggle.IsChecked == true)
        {
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo"" /v Enabled /t REG_DWORD /d 0 /f");
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v Start_IrisRecommendations /t REG_DWORD /d 0 /f");
        }

        // Кнопка «Завершить задачу» на панели задач (Windows 11): изменения вступают
        // в силу после перезапуска проводника, поэтому запускаем его следом.
        if (EndTaskToggle.IsChecked == true)
        {
            parts.Add(@"reg add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"" /v TaskbarEndTask /t REG_DWORD /d 1 /f");
            parts.Add("Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 500; Start-Process explorer.exe");
        }

        // Отключение «залипания клавиш» (пять нажатий Shift).
        if (DisableStickyKeysToggle.IsChecked == true)
        {
            parts.Add(@"reg add ""HKCU\Control Panel\Accessibility\StickyKeys"" /v Flags /t REG_SZ /d 0 /f; " +
                      @"reg add ""HKCU\Control Panel\Accessibility\StickyKeys"" /v HotkeyActive /t REG_SZ /d 0 /f; " +
                      @"reg add ""HKCU\Control Panel\Accessibility\StickyKeys"" /v HotkeyApproved /t REG_SZ /d 0 /f");
        }

        var gamingEnabled = QuickGameModeToggle.IsChecked == true
            || QuickCoreParkingToggle.IsChecked == true;

        if (parts.Count == 0 && !gamingEnabled)
        {
            await Dialogs.ShowInfoAsync("Нет действий", "Включите хотя бы один параметр.");
            return;
        }

        // Снимок делаем до изменений: если что-то пойдёт не так, всё можно вернуть.
        var snapshotCreated = false;
        if (QuickSnapshotToggle.IsChecked == true)
        {
            try
            {
                await TweakBackupService.CreateAsync();
                snapshotCreated = true;
            }
            catch
            {
                // снимок — страховка, его сбой не должен мешать настройке
            }
        }

        var errors = new List<string>();

        if (parts.Count > 0)
        {
            var r = await CommandRunner.RunPowerShellAsync(string.Join("; ", parts));
            if (r.ExitCode != 0)
                errors.Add(r.Error + r.Output);
        }

        // Игровая оптимизация: игровой режим и парковка ядер.
        async Task ApplyAsync(string label, Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                errors.Add(LocalizationService.T(label) + ": " + ex.Message);
            }
        }

        if (QuickGameModeToggle.IsChecked == true)
            await ApplyAsync("Игровой режим Windows", () => GamingService.SetGameModeAsync(true));

        if (QuickCoreParkingToggle.IsChecked == true)
            await ApplyAsync("Отключить парковку ядер", () => GamingService.SetCoreParkingAsync(true));

        if (errors.Count > 0)
        {
            await Dialogs.ShowErrorAsync("Ошибка", string.Join("\n", errors));
            return;
        }

        await Dialogs.ShowInfoAsync("Готово", snapshotCreated
            ? "Быстрая настройка применена. Снимок настроек сохранён — вернуть всё назад можно в разделе «Система и восстановление»."
            : "Быстрая настройка применена.");
    }
}
