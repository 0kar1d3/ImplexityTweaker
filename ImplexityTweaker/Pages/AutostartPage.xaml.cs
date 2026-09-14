using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImplexityTweaker.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace ImplexityTweaker.Pages;

public partial class AutostartPage : Page
{
    private const string HkcuRun = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmRun = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string HkcuApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string HklmApproved = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string FolderApproved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public ObservableCollection<AutostartEntry> Entries { get; } = [];

    public AutostartPage()
    {
        InitializeComponent();
        EntryList.ItemsSource = Entries;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try { LoadingIndicator.Visibility = Visibility.Visible; } catch { }

        var items = await Task.Run(CollectEntries);

        Entries.Clear();
        foreach (var item in items)
            Entries.Add(item);
        CountText.Text = LocalizationService.TF("Всего записей: {0}", Entries.Count);

        try { LoadingIndicator.Visibility = Visibility.Collapsed; } catch { }
    }

    // Собирает записи из HKCU\Run, HKLM\Run и папки автозагрузки,
    // учитывая состояние из StartupApproved.
    private static List<AutostartEntry> CollectEntries()
    {
        var list = new List<AutostartEntry>();

        AddRunEntries(list, Registry.CurrentUser, HkcuRun, HkcuApproved, "HKCU");
        AddRunEntries(list, Registry.LocalMachine, HklmRun, HklmApproved, "HKLM");

        // Папка автозагрузки (shell:startup)
        try
        {
            var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(folder))
            {
                var approved = ReadApproved(Registry.CurrentUser, FolderApproved);
                foreach (var file in Directory.GetFiles(folder))
                {
                    var name = Path.GetFileNameWithoutExtension(file);
                    list.Add(new AutostartEntry
                    {
                        Name = name,
                        Command = file,
                        Source = "Папка автозагрузки",
                        IsStartupFolder = true,
                        IsEnabled = !(approved.TryGetValue(name, out var data) && data.Length > 0 && data[0] >= 6)
                    });
                }
            }
        }
        catch
        {
        }

        return list
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void AddRunEntries(List<AutostartEntry> list, RegistryKey root, string runPath, string approvedPath, string source)
    {
        try
        {
            var approved = ReadApproved(root, approvedPath);

            using var run = root.OpenSubKey(runPath);
            if (run == null)
                return;

            foreach (var name in run.GetValueNames())
            {
                if (run.GetValue(name) is not string command)
                    continue;

                list.Add(new AutostartEntry
                {
                    Name = name,
                    Command = command,
                    Source = source,
                    IsStartupFolder = false,
                    IsEnabled = !(approved.TryGetValue(name, out var data) && data.Length > 0 && data[0] >= 6)
                });
            }
        }
        catch
        {
        }
    }

    // StartupApproved хранит значения REG_BINARY: первый байт 2/3 — включено, 6/7 — отключено.
    private static Dictionary<string, byte[]> ReadApproved(RegistryKey root, string path)
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = root.OpenSubKey(path);
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    if (key.GetValue(name) is byte[] data)
                        dict[name] = data;
                }
            }
        }
        catch
        {
        }
        return dict;
    }

    private static byte[] ApprovedBytes(byte first)
    {
        var data = new byte[12];
        data[0] = first;
        return data;
    }

    // ===================== Включить / отключить =====================

    private async void Toggle_Changed(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: AutostartEntry entry } toggle)
            return;

        bool enable = toggle.IsChecked == true;
        try
        {
            await Task.Run(() =>
            {
                if (entry.IsStartupFolder)
                {
                    using var key = Registry.CurrentUser.CreateSubKey(FolderApproved, true);
                    if (enable)
                        key?.DeleteValue(entry.Name, throwOnMissingValue: false);
                    else
                        key?.SetValue(entry.Name, ApprovedBytes(7), RegistryValueKind.Binary);
                }
                else
                {
                    var root = entry.Source == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;
                    var approved = entry.Source == "HKLM" ? HklmApproved : HkcuApproved;
                    using var key = root.CreateSubKey(approved, true);
                    if (enable)
                        key?.DeleteValue(entry.Name, throwOnMissingValue: false);
                    else
                        key?.SetValue(entry.Name, ApprovedBytes(6), RegistryValueKind.Binary);
                }
            });

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
            await RefreshAsync();
        }
    }

    // ===================== Добавить =====================

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.T("Выберите программу для автозагрузки"),
            Filter = LocalizationService.T("Программы (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|Все файлы (*.*)|*.*"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true)
            return;

        var file = dlg.FileName;
        var name = Path.GetFileNameWithoutExtension(file);
        var command = "\"" + file + "\"";

        try
        {
            await Task.Run(() =>
            {
                using var runKey = Registry.CurrentUser.CreateSubKey(HkcuRun, true);
                runKey?.SetValue(name, command, RegistryValueKind.String);
            });

            await Dialogs.ShowInfoAsync("Готово", LocalizationService.TF("«{0}» добавлен в автозагрузку.", name));
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

    // ===================== Аудит =====================

    private void Audit_Click(object sender, RoutedEventArgs e)
    {
        AuditHost.Children.Clear();

        var suspicious = AutostartAuditService.Scan()
            .Where(x => !string.IsNullOrEmpty(x.Problem))
            .ToList();

        if (suspicious.Count == 0)
        {
            AuditHost.Children.Add(Info(LocalizationService.T("Лишних записей не найдено.")));
            return;
        }

        foreach (var entry in suspicious)
            AuditHost.Children.Add(Info($"{entry.Name} — {LocalizationService.T(entry.Problem)} ({LocalizationService.T(entry.Source)})"));
    }

    private TextBlock Info(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
        Style = (Style)FindResource("ItemDescriptionText")
    };
}

public class AutostartEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";
    public bool IsStartupFolder { get; init; }
    public bool IsEnabled { get; init; }

    public string SourceText => IsStartupFolder ? LocalizationService.T("Папка автозагрузки") : Source;
    public string StateText => IsEnabled ? LocalizationService.T("Включено") : LocalizationService.T("Отключено");

    private ImageSource? _icon;
    public ImageSource? Icon => _icon ??= ExtractIcon(Command);

    private static ImageSource? ExtractIcon(string command)
    {
        try
        {
            var exe = ParseExePath(command);
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return null;

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
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

    private static string ParseExePath(string command)
    {
        var cmd = (command ?? "").Trim();
        if (cmd.StartsWith("\""))
        {
            var end = cmd.IndexOf('"', 1);
            if (end > 0)
                return cmd.Substring(1, end - 1);
        }
        else
        {
            var space = cmd.IndexOf(' ');
            return space > 0 ? cmd.Substring(0, space) : cmd;
        }
        return cmd;
    }
}

