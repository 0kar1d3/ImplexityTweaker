using System.Diagnostics;
using System.Linq;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class ProcessesPage : Page
{
    public ProcessesPage()
    {
        InitializeComponent();
        RefreshList();
    }

    private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        ProcessList.Items.Clear();
        foreach (var p in Process.GetProcesses().OrderBy(p => p.ProcessName).Take(60))
        {
            try
            {
                ProcessList.Items.Add($"{p.ProcessName,-30} PID {p.Id,-8} RAM {p.WorkingSet64 / 1024 / 1024,5} MB");
            }
            catch
            {
                /* доступ запрещён */
            }
        }
    }

    private void TaskMgr_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
    }

    private async void Kill_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var input = KillInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;

        (int ExitCode, string Output, string Error) r;
        if (int.TryParse(input, out var pid))
            r = await CommandRunner.RunPowerShellAsync($"Stop-Process -Id {pid} -Force");
        else
            r = await CommandRunner.RunPowerShellAsync($"Get-Process -Name \"{input}\" -ErrorAction SilentlyContinue | Stop-Process -Force");

        if (r.ExitCode == 0)
        {
            RefreshList();
            await Dialogs.ShowInfoAsync("Готово", "Процесс завершен (если был найден).");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
        }
    }
}
