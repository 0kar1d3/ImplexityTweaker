using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class AppInstallPage : Page
{
    public AppInstallPage()
    {
        InitializeComponent();
    }

    private async void Bundle_Click(object sender, RoutedEventArgs e)
    {
        var script =
            "winget install --id Google.Chrome -e --accept-package-agreements --accept-source-agreements --silent; " +
            "winget install --id 7zip.7zip -e --accept-package-agreements --accept-source-agreements --silent; " +
            "winget install --id VideoLAN.VLC -e --accept-package-agreements --accept-source-agreements --silent; " +
            "winget install --id Microsoft.VisualStudioCode -e --accept-package-agreements --accept-source-agreements --silent";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Установка завершена или уже была выполнена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("powershell", "-NoExit -Command winget search .") { UseShellExecute = true });
    }

    private async void InstallManual_Click(object sender, RoutedEventArgs e)
    {
        var id = ManualWingetIdBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите winget ID пакета.");
            return;
        }

        var cmd = $"winget install --id {id} -e --accept-package-agreements --accept-source-agreements";
        var r = await CommandRunner.RunPowerShellAsync(cmd);
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", $"Пакет {id} установлен (или уже установлен).");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        var r = await CommandRunner.RunPowerShellAsync("winget upgrade --all --accept-package-agreements --accept-source-agreements");
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Обновление пакетов завершено.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void Firefox_Click(object sender, RoutedEventArgs e)
    {
        await InstallWingetAsync("Mozilla.Firefox", "Firefox");
    }

    private async void Steam_Click(object sender, RoutedEventArgs e)
    {
        await InstallWingetAsync("Valve.Steam", "Steam");
    }

    private async void Discord_Click(object sender, RoutedEventArgs e)
    {
        await InstallWingetAsync("Discord.Discord", "Discord");
    }

    private async void Telegram_Click(object sender, RoutedEventArgs e)
    {
        await InstallWingetAsync("Telegram.TelegramDesktop", "Telegram Desktop");
    }

    private void OpenZapretGithub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/Flowseal/zapret-discord-youtube") { UseShellExecute = true });
    }

    private async Task InstallWingetAsync(string id, string displayName)
    {
        var cmd = $"winget install --id {id} -e --accept-package-agreements --accept-source-agreements --silent";
        var r = await CommandRunner.RunPowerShellAsync(cmd);
        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", $"{displayName} установлен (или уже был установлен).");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }
}
