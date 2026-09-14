using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

/// <summary>
/// Одна простая задача: включить обновления Windows как в системе
/// или полностью их отключить. Текущее состояние читается у служб.
/// </summary>
public partial class WindowsUpdatePage : Page
{
    private static readonly Brush EnabledBrush = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly Brush DisabledBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));

    private bool _busy;

    public WindowsUpdatePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshStateAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshStateAsync();

    /// <summary>Показывает реальное состояние служб обновления, а не сохранённые настройки.</summary>
    private async Task RefreshStateAsync()
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            var r = await CommandRunner.RunPowerShellAsync(@"
foreach ($name in 'wuauserv','UsoSvc') {
    $s = Get-Service -Name $name -ErrorAction SilentlyContinue
    if ($null -ne $s) { Write-Output ($name + ';' + $s.Status + ';' + $s.StartType) } else { Write-Output ($name + ';;') }
}");

            var states = ParseStates(r.Output);
            if (!states.TryGetValue("wuauserv", out var wu))
            {
                StateTitleText.Text = LocalizationService.T("Не удалось определить состояние службы обновлений.");
                StateTitleText.Foreground = DisabledBrush;
                StateDetailText.Text = string.Empty;
                return;
            }

            var disabled = wu.StartType.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            StateTitleText.Text = LocalizationService.T(disabled
                ? "Обновления Windows отключены"
                : "Обновления Windows включены");
            StateTitleText.Foreground = disabled ? DisabledBrush : EnabledBrush;

            StateDetailText.Text = disabled
                ? LocalizationService.T("Служба обновлений отключена — новые обновления, включая обновления безопасности, не устанавливаются.")
                : LocalizationService.TF("Служба обновлений: {0}, запуск — {1}.",
                    LocalizationService.T(StatusWord(wu.Status)),
                    LocalizationService.T(StartWord(wu.StartType)));
        }
        catch (Exception ex)
        {
            StateTitleText.Text = LocalizationService.T("Не удалось определить состояние службы обновлений.");
            StateDetailText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private static Dictionary<string, (string Status, string StartType)> ParseStates(string output)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.Trim().Split(';');
            if (parts.Length < 3 || parts[0].Length == 0)
                continue;

            result[parts[0]] = (parts[1].Trim(), parts[2].Trim());
        }
        return result;
    }

    private static string StatusWord(string status) => status.ToLowerInvariant() switch
    {
        "running" => "работает",
        "stopped" => "остановлена",
        _ => status
    };

    private static string StartWord(string startType) => startType.ToLowerInvariant() switch
    {
        "automatic" => "автоматически",
        "manual" => "вручную",
        "disabled" => "отключена",
        _ => startType
    };

    private async void Enable_Click(object sender, RoutedEventArgs e)
    {
        // Возвращаем настройки Windows по умолчанию: служба обновлений — по требованию.
        var r = await CommandRunner.RunPowerShellAsync(
            "sc.exe config wuauserv start= demand | Out-Null; " +
            "sc.exe config UsoSvc start= auto | Out-Null; " +
            "Start-Service UsoSvc -ErrorAction SilentlyContinue; " +
            "Start-Service wuauserv -ErrorAction SilentlyContinue");

        await RefreshStateAsync();

        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Служба обновлений включена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        var confirm = await Dialogs.ShowConfirmAsync("Отключение обновлений Windows",
            "Обновления Windows перестанут скачиваться и устанавливаться, включая обновления безопасности.");
        if (!confirm)
            return;

        var r = await CommandRunner.RunPowerShellAsync(
            "Stop-Service wuauserv -Force -ErrorAction SilentlyContinue; " +
            "Stop-Service UsoSvc -Force -ErrorAction SilentlyContinue; " +
            "sc.exe config wuauserv start= disabled | Out-Null; " +
            "sc.exe config UsoSvc start= disabled | Out-Null");

        await RefreshStateAsync();

        if (r.ExitCode == 0)
            await Dialogs.ShowInfoAsync("Готово", "Служба обновлений отключена.");
        else
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true });
    }
}
