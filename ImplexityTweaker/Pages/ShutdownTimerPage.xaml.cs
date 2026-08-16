using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class ShutdownTimerPage : Page
{
    public ShutdownTimerPage()
    {
        InitializeComponent();
    }

    private async void Shutdown_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinutesBox.Text, out var m) || m < 1)
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите число минут больше 0.");
            return;
        }
        Process.Start(new ProcessStartInfo("shutdown", $"/s /t {m * 60}") { UseShellExecute = true });
        await Dialogs.ShowInfoAsync("Готово", $"Выключение через {m} мин.");
    }

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MinutesBox.Text, out var m) || m < 1)
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите число минут больше 0.");
            return;
        }
        Process.Start(new ProcessStartInfo("shutdown", $"/r /t {m * 60}") { UseShellExecute = true });
        await Dialogs.ShowInfoAsync("Готово", $"Перезагрузка через {m} мин.");
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("shutdown", "/a") { UseShellExecute = true });
    }
}
