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
        if (!TryParseMinutes(out var m))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите число минут от 1 до 525600 (1 год).");
            return;
        }
        Process.Start(new ProcessStartInfo("shutdown", $"/s /t {m * 60}") { UseShellExecute = true });
        await Dialogs.ShowInfoAsync("Готово", LocalizationService.TF("Выключение через {0} мин.", m));
    }

    private async void Reboot_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseMinutes(out var m))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите число минут от 1 до 525600 (1 год).");
            return;
        }
        Process.Start(new ProcessStartInfo("shutdown", $"/r /t {m * 60}") { UseShellExecute = true });
        await Dialogs.ShowInfoAsync("Готово", LocalizationService.TF("Перезагрузка через {0} мин.", m));
    }

    // Ограничиваем диапазон: не даём ввести число минут, при котором m * 60
    // переполнит int (например, 10 миллиардов минут) и уйдёт в отрицательное.
    private bool TryParseMinutes(out int minutes)
    {
        minutes = 0;
        return int.TryParse(MinutesBox.Text, out var m) && m >= 1 && m <= 525600; // 525600 мин = 1 год
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("shutdown", "/a") { UseShellExecute = true });
    }
}
