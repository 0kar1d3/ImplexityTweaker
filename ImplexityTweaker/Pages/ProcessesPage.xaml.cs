using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class ProcessesPage : Page
{
    private readonly List<ProcessInfo> _allProcesses = new();

    public ProcessesPage()
    {
        InitializeComponent();
        Loaded += ProcessesPage_Loaded;
    }

    private async void ProcessesPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshListAsync();
        }
        catch (Exception ex)
        {
            FinishLoading();
            await Dialogs.ShowErrorAsync("Ошибка", "Не удалось получить список процессов: " + ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RefreshListAsync();
        }
        catch (Exception ex)
        {
            FinishLoading();
            await Dialogs.ShowErrorAsync("Ошибка", "Не удалось получить список процессов: " + ex.Message);
        }
    }

    private void FinishLoading()
    {
        LoadingIndicator.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
    }

    private async Task RefreshListAsync()
    {
        RefreshButton.IsEnabled = false;
        LoadingIndicator.Visibility = Visibility.Visible;

        var processes = await Task.Run(() =>
        {
            var list = new List<ProcessInfo>();
            Process[] all;
            try
            {
                all = Process.GetProcesses();
            }
            catch
            {
                return list;
            }

            foreach (var p in all)
            {
                try
                {
                    var ramMb = p.WorkingSet64 / 1024 / 1024;
                    string? exePath = null;
                    try { exePath = p.MainModule?.FileName; } catch { }
                    list.Add(new ProcessInfo(p.ProcessName, p.Id, ramMb, exePath));
                }
                catch
                {
                    // доступ запрещён — пропускаем
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return list;
        });

        _allProcesses.Clear();
        _allProcesses.AddRange(processes);

        ApplyFilterAndSort();

        LoadingIndicator.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = true;
    }

    private void ApplyFilterAndSort()
    {
        // Во время InitializeComponent() событие SelectionChanged у SortCombo
        // может сработать раньше, чем будут инициализированы ProcessList/CountText.
        if (SearchBox == null || SortCombo == null || ProcessList == null || CountText == null)
            return;

        var query = SearchBox.Text?.Trim() ?? string.Empty;

        IEnumerable<ProcessInfo> items = _allProcesses;
        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(p => p.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

        var sortIndex = SortCombo.SelectedIndex;
        items = sortIndex switch
        {
            1 => items.OrderBy(p => p.RamMb),
            2 => items.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            3 => items.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderByDescending(p => p.RamMb),
        };

        ProcessList.ItemsSource = items.ToList();
        CountText.Text = $"Показано процессов: {ProcessList.Items.Count} из {_allProcesses.Count}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilterAndSort();

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilterAndSort();

    private async void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int pid })
            return;

        var process = _allProcesses.FirstOrDefault(p => p.Pid == pid);
        var name = process?.Name ?? pid.ToString();

        var confirm = await Dialogs.ShowConfirmAsync("Завершить процесс",
            $"Завершить процесс \"{name}\" (PID {pid})?");
        if (!confirm)
            return;

        try
        {
            var r = await CommandRunner.RunPowerShellAsync($"Stop-Process -Id {pid} -Force");
            if (r.ExitCode == 0)
            {
                await RefreshListAsync();
                await Dialogs.ShowInfoAsync("Готово", $"Процесс \"{name}\" завершен.");
            }
            else
            {
                await Dialogs.ShowErrorAsync("Ошибка", r.Error + r.Output);
            }
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }
}

public class ProcessInfo
{
    public string Name { get; }
    public int Pid { get; }
    public long RamMb { get; }
    public string RamText => $"{RamMb:N0} МБ";
    public string? ExePath { get; }

    private ImageSource? _icon;
    public ImageSource? Icon => _icon ??= ExtractIcon(ExePath);

    public ProcessInfo(string name, int pid, long ramMb, string? exePath)
    {
        Name = name;
        Pid = pid;
        RamMb = ramMb;
        ExePath = exePath;
    }

    private static ImageSource? ExtractIcon(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null)
                return null;
            return Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }
}
