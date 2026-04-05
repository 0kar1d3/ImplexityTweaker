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
        ShowHiddenToggle.IsChecked = true;
        ShowExtensionsToggle.IsChecked = true;
        ThisPcToggle.IsChecked = true;
        ThisPcDesktopToggle.IsChecked = true;
        NoShortcutSuffixToggle.IsChecked = true;
        ReduceAdsToggle.IsChecked = true;
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

        if (parts.Count == 0)
        {
            await Dialogs.ShowInfoAsync("Нет действий", "Включите хотя бы один параметр.");
            return;
        }

        var r = await CommandRunner.RunPowerShellAsync(string.Join("; ", parts));
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Быстрая настройка применена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }
}
