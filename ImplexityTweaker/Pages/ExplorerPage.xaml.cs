using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using System.Diagnostics;

namespace ImplexityTweaker.Pages;

public partial class ExplorerPage : Page
{
    public ExplorerPage()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var s = AppSettings.Load();

        // Если в конфиге задано состояние — применяем его, иначе читаем из системы.
        HiddenToggle.IsChecked = s.ShowHiddenFiles ?? ExplorerTweaks.GetShowHiddenFiles();
        SystemToggle.IsChecked = s.ShowSystemFiles ?? ExplorerTweaks.GetShowSystemFiles();
        ExtToggle.IsChecked = s.ShowFileExtensions ?? ExplorerTweaks.GetShowFileExtensions();
        ThisPcToggle.IsChecked = s.LaunchToThisPc ?? (ExplorerTweaks.GetLaunchTo() == 1);
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        ExplorerTweaks.SetShowHiddenFiles(HiddenToggle.IsChecked == true);
        ExplorerTweaks.SetShowSystemFiles(SystemToggle.IsChecked == true);
        ExplorerTweaks.SetShowFileExtensions(ExtToggle.IsChecked == true);
        ExplorerTweaks.SetLaunchToThisPc(ThisPcToggle.IsChecked == true);
        SaveToSettings();
        await Dialogs.ShowInfoAsync("Готово", "Настройки проводника применены. При необходимости перезапустите проводник.");
    }

    private async void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        HiddenToggle.IsChecked = false;
        SystemToggle.IsChecked = false;
        ExtToggle.IsChecked = true;
        ThisPcToggle.IsChecked = false;
        await ApplyAndNotifyAsync("Параметры проводника сброшены на базовые.");
    }

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("powershell", "-NoProfile -Command \"taskkill /f /im explorer.exe; Start-Process explorer.exe\"")
        {
            UseShellExecute = true
        });
        await Dialogs.ShowInfoAsync("Готово", "Проводник перезапущен.");
    }

    private async Task ApplyAndNotifyAsync(string message)
    {
        ExplorerTweaks.SetShowHiddenFiles(HiddenToggle.IsChecked == true);
        ExplorerTweaks.SetShowSystemFiles(SystemToggle.IsChecked == true);
        ExplorerTweaks.SetShowFileExtensions(ExtToggle.IsChecked == true);
        ExplorerTweaks.SetLaunchToThisPc(ThisPcToggle.IsChecked == true);
        SaveToSettings();
        await Dialogs.ShowInfoAsync("Готово", message);
    }

    private void SaveToSettings()
    {
        var s = AppSettings.Load();
        s.ShowHiddenFiles = HiddenToggle.IsChecked == true;
        s.ShowSystemFiles = SystemToggle.IsChecked == true;
        s.ShowFileExtensions = ExtToggle.IsChecked == true;
        s.LaunchToThisPc = ThisPcToggle.IsChecked == true;
        s.Save();
    }
}
