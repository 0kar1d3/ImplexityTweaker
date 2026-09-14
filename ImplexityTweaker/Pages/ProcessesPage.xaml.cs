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
            await Dialogs.ShowErrorAsync("Ошибка", LocalizationService.T("Не удалось получить список процессов: ") + ex.Message);
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
            await Dialogs.ShowErrorAsync("Ошибка", LocalizationService.T("Не удалось получить список процессов: ") + ex.Message);
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

                    // У активных приложений есть открытое окно — их выносим в отдельную группу.
                    var hasWindow = false;
                    try { hasWindow = p.MainWindowHandle != IntPtr.Zero; } catch { }

                    list.Add(new ProcessInfo(p.ProcessName, p.Id, ramMb, exePath, hasWindow));
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
        if (SearchBox == null || SortCombo == null || ActiveList == null || BackgroundList == null || CountText == null)
            return;

        var query = SearchBox.Text?.Trim() ?? string.Empty;

        IEnumerable<ProcessInfo> items = _allProcesses;
        if (!string.IsNullOrWhiteSpace(query))
            items = items.Where(p => p.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

        var source = items.ToList();

        // Режим отображения: одна строка на .exe или все процессы отдельно.
        var grouped = GroupCombo == null || GroupCombo.SelectedIndex == 0;
        if (grouped)
            source = GroupByName(source);

        IEnumerable<ProcessInfo> sorted = SortCombo.SelectedIndex switch
        {
            1 => source.OrderBy(p => p.RamMb),
            2 => source.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            3 => source.OrderByDescending(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => source.OrderByDescending(p => p.RamMb),
        };

        var filtered = sorted.ToList();

        // Полоска памяти — относительно самой «тяжёлой» строки списка.
        var maxRam = filtered.Count == 0 ? 1 : Math.Max(1, filtered.Max(p => p.RamMb));
        foreach (var process in filtered)
            process.BarWidth = 6 + 174 * (process.RamMb / (double)maxRam);

        var active = filtered.Where(p => p.IsActive).ToList();
        var background = filtered.Where(p => !p.IsActive).ToList();

        ActiveList.ItemsSource = active;
        BackgroundList.ItemsSource = background;

        ActiveCountText.Text = active.Count.ToString();
        BackgroundCountText.Text = background.Count.ToString();

        ActiveEmptyText.Visibility = active.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BackgroundEmptyText.Visibility = background.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        CountText.Text = grouped
            ? LocalizationService.TF("Строк: {0} · всего процессов: {1}", filtered.Count, _allProcesses.Count)
            : LocalizationService.TF("Показано процессов: {0} из {1}", filtered.Count, _allProcesses.Count);
    }

    /// <summary>Сворачивает процессы с одинаковым именем в одну строку.</summary>
    private static List<ProcessInfo> GroupByName(List<ProcessInfo> source)
    {
        var result = new List<ProcessInfo>();

        foreach (var group in source.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToList();
            if (items.Count == 1)
            {
                result.Add(items[0]);
                continue;
            }

            result.Add(new ProcessInfo(
                items[0].Name,
                items.Select(p => p.Pid).ToArray(),
                items.Sum(p => p.RamMb),
                items.FirstOrDefault(p => !string.IsNullOrEmpty(p.ExePath))?.ExePath,
                items.Any(p => p.IsActive)));
        }

        return result;
    }

    private void GroupCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilterAndSort();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilterAndSort();

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyFilterAndSort();

    private async void KillProcess_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProcessInfo info })
            return;

        // В сгруппированном режиме строка объединяет несколько процессов одного .exe.
        var pids = info.Pids.Length > 0 ? info.Pids : new[] { info.Pid };

        var message = pids.Length > 1
            ? LocalizationService.TF("Завершить все процессы \"{0}\" — {1} шт.?", info.Name, pids.Length)
            : LocalizationService.TF("Завершить процесс \"{0}\" (PID {1})?", info.Name, pids[0]);

        var confirm = await Dialogs.ShowConfirmAsync("Завершить процесс", message);
        if (!confirm)
            return;

        try
        {
            var r = await CommandRunner.RunPowerShellAsync($"Stop-Process -Id {string.Join(",", pids)} -Force");
            if (r.ExitCode == 0)
            {
                await RefreshListAsync();
                await Dialogs.ShowInfoAsync("Готово",
                    LocalizationService.TF("Процессы \"{0}\" завершены ({1} шт.).", info.Name, pids.Length));
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
    public int[] Pids { get; }
    public long RamMb { get; }
    public string RamText => $"{RamMb:N0} {LocalizationService.T("МБ")}";

    /// <summary>Подпись с идентификаторами: одна строка может объединять несколько процессов.</summary>
    public string PidText
    {
        get
        {
            if (Pids.Length <= 1)
                return "PID: " + (Pids.Length == 1 ? Pids[0].ToString() : "—");

            var shown = string.Join(", ", Pids.Take(3));
            if (Pids.Length > 3)
                shown += "…";

            return LocalizationService.TF("Процессов: {0} · PID: {1}", Pids.Length, shown);
        }
    }
    public string? ExePath { get; }

    /// <summary>Активный процесс — у него есть открытое окно.</summary>
    public bool IsActive { get; }

    /// <summary>Ширина полоски памяти в пикселях (задаётся при построении списка).</summary>
    public double BarWidth { get; set; }

    private ImageSource? _icon;
    public ImageSource? Icon => _icon ??= ExtractIcon(ExePath);

    public ProcessInfo(string name, int pid, long ramMb, string? exePath, bool isActive)
        : this(name, new[] { pid }, ramMb, exePath, isActive)
    {
    }

    /// <summary>Несколько процессов одного имени, объединённые в одну строку.</summary>
    public ProcessInfo(string name, int[] pids, long ramMb, string? exePath, bool isActive)
    {
        Name = name;
        Pids = pids;
        Pid = pids.Length > 0 ? pids[0] : -1;
        RamMb = ramMb;
        ExePath = exePath;
        IsActive = isActive;
        BarWidth = 6;
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
