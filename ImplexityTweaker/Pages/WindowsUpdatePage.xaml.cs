using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using System.Diagnostics;

namespace ImplexityTweaker.Pages;

public partial class WindowsUpdatePage : Page
{
    public WindowsUpdatePage()
    {
        InitializeComponent();
        StartupModeBox.SelectedIndex = 1;
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("Stop-Service wuauserv -Force -ErrorAction SilentlyContinue; Set-Service wuauserv -StartupType Disabled -ErrorAction SilentlyContinue; sc.exe config wuauserv start= disabled");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Служба обновления отключена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("sc.exe config wuauserv start= demand; Start-Service wuauserv -ErrorAction SilentlyContinue");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Служба обновления восстановлена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void ApplyMode_Click(object sender, RoutedEventArgs e)
    {
        var selected = (StartupModeBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Manual (Demand)";
        var mode = selected switch
        {
            "Disabled" => "disabled",
            "Automatic" => "auto",
            _ => "demand"
        };
        var r = await CommandRunner.RunPowerShellAsync($"sc.exe config wuauserv start= {mode}");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", $"Режим запуска службы установлен: {selected}.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
    }
}
