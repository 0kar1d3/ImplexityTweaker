using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;

namespace ImplexityTweaker.Pages;

public partial class AppInstallPage : Page
{
    public AppInstallPage()
    {
        InitializeComponent();
    }

    private async void Chrome_Click(object sender, RoutedEventArgs e)
        => await InstallWingetAsync("Google.Chrome", "Chrome");

    private async void SevenZip_Click(object sender, RoutedEventArgs e)
        => await InstallWingetAsync("7zip.7zip", "7-Zip");

    private async void Vlc_Click(object sender, RoutedEventArgs e)
        => await InstallWingetAsync("VideoLAN.VLC", "VLC");

    private async void VsCode_Click(object sender, RoutedEventArgs e)
        => await InstallWingetAsync("Microsoft.VisualStudioCode", "VS Code");

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

        // Ручная установка идёт без --silent: если пакету нужен установщик с UI,
        // winget покажет его и дождётся завершения.
        await InstallWingetAsync(id, id, silent: false);
    }

    private async void UpgradeAll_Click(object sender, RoutedEventArgs e)
    {
        const string cmd = "winget upgrade --all --accept-package-agreements --accept-source-agreements";

        var (ok, error, _) = await ProgressWindow.RunIndeterminateAsync(
            Window.GetWindow(this),
            LocalizationService.T("Установка приложений"),
            LocalizationService.T("Обновление всех пакетов… это может занять несколько минут."),
            async (window, token) =>
            {
                var r = await CommandRunner.RunPowerShellAsync(
                    cmd,
                    line => window.SetIndeterminate(true, line),
                    token);

                // «Нет доступных обновлений» — это не ошибка, а нормальный результат.
                if (!IsWingetBenignSuccess(r.ExitCode, r.Output, r.Error))
                    throw new Exception(BuildWingetError(r.ExitCode, r.Output, r.Error));
            });

        if (ok)
            await Dialogs.ShowInfoAsync("Готово", "Обновление пакетов завершено.");
        else if (error != null)
            await Dialogs.ShowErrorAsync("Ошибка", error);
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

    private async void Spotify_Click(object sender, RoutedEventArgs e)
    {
        await InstallWingetAsync("Spotify.Spotify", "Spotify");
    }

    private void OpenZapretGithub_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/Flowseal/zapret-discord-youtube") { UseShellExecute = true });
    }

    private async Task InstallWingetAsync(string id, string displayName, bool silent = true)
    {
        var cmd = $"winget install --id {id} -e --accept-package-agreements --accept-source-agreements"
                  + (silent ? " --silent" : "");

        var (ok, error, _) = await ProgressWindow.RunIndeterminateAsync(
            Window.GetWindow(this),
            LocalizationService.T("Установка приложений"),
            LocalizationService.TF("{0}: установка… это может занять несколько минут.", displayName),
            async (window, token) =>
            {
                var r = await CommandRunner.RunPowerShellAsync(
                    cmd,
                    line => window.SetIndeterminate(true, line),
                    token);

                if (!IsWingetBenignSuccess(r.ExitCode, r.Output, r.Error))
                    throw new Exception(BuildWingetError(r.ExitCode, r.Output, r.Error));
            });

        if (ok)
            await Dialogs.ShowInfoAsync("Готово", LocalizationService.TF("{0} установлен (или уже был установлен).", displayName));
        else if (error != null)
            await Dialogs.ShowErrorAsync("Ошибка", error);
        // Отмена — ничего не показываем.
    }

    /// <summary>
    /// winget возвращает НЕНУЛЕВОЙ код даже когда всё в порядке:
    /// пакет уже установлен или нет доступных обновлений. Такие случаи считаем успехом,
    /// иначе пользователь видел бы «Ошибка» при повторной установке.
    /// </summary>
    private static bool IsWingetBenignSuccess(int exitCode, string output, string error)
    {
        if (exitCode == 0)
            return true;

        var text = ((output ?? "") + "\n" + (error ?? "")).ToLowerInvariant();
        return text.Contains("found an existing package already installed")
            || text.Contains("already been installed")
            || text.Contains("already installed")
            || text.Contains("no available upgrade found")
            || text.Contains("no newer package versions are available")
            || text.Contains("no applicable upgrade found")
            || text.Contains("no installed package found matching")
            || text.Contains("successfully installed")
            || text.Contains("installation succeeded");
    }

    private static string BuildWingetError(int exitCode, string output, string error)
    {
        var text = ((error ?? "") + "\n" + (output ?? "")).Trim();
        return string.IsNullOrWhiteSpace(text)
            ? LocalizationService.TF("winget завершился с кодом {0} (0x{1:X8}).", exitCode, exitCode)
            : text;
    }
}
