using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;
using System.Linq;

namespace ImplexityTweaker.Pages;

public partial class SystemRecoveryPage : Page
{
    public SystemRecoveryPage()
    {
        InitializeComponent();
        TelemetryFrame.Navigate(new TelemetryPage());
        AnalyzePathBox.Text = "C:\\";
        RefreshBackupList();
        Loaded += async (_, _) => await LoadRestorePointsAsync();
    }

    // Прокрутка страницы колесом мыши, когда курсор находится над блоком телеметрии:
    // вложенный ScrollViewer фрейма перехватывает колесо и блокирует прокрутку родителя.
    private void TelemetryFrame_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (RootScroll == null)
            return;

        if (e.Delta > 0)
            RootScroll.LineUp();
        else
            RootScroll.LineDown();

        e.Handled = true;
    }

    private void OpenSysRestore_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("SystemPropertiesProtection") { UseShellExecute = true });
    }

    /// <summary>Создаёт только точку восстановления — ничего больше не меняет.</summary>
    private async void CreateRestorePoint_Click(object sender, RoutedEventArgs e)
    {
        var description = LocalizationService.T("Точка восстановления Implexity");

        var (ok, error, _) = await ProgressWindow.RunIndeterminateAsync(
            Window.GetWindow(this),
            LocalizationService.T("Создание точки восстановления"),
            LocalizationService.T("Windows сохраняет состояние системы… это может занять до минуты."),
            async (_, _) =>
            {
                var created = await DiagnosticsService.CreateRestorePointAsync(description);
                if (!created)
                    throw new Exception(LocalizationService.T(
                        "Windows не создала точку восстановления. Проверьте, включена ли защита системы и есть ли свободное место на диске C:."));
            });

        await LoadRestorePointsAsync();

        if (ok)
            await Dialogs.ShowInfoAsync("Готово", "Точка восстановления создана.");
        else if (error != null)
            await Dialogs.ShowErrorAsync("Ошибка", error);
    }

    private async void Compact_Click(object sender, RoutedEventArgs e)
    {
        var (ok, error, _) = await ProgressWindow.RunIndeterminateAsync(
            Window.GetWindow(this),
            LocalizationService.T("Сжатие системы (CompactOS)"),
            LocalizationService.T("Сжатие системного раздела… это может занять несколько минут."),
            async (window, token) =>
            {
                var r = await CommandRunner.RunProcessAsync(
                    "compact.exe",
                    "/compactos:always",
                    line => window.SetIndeterminate(true, line),
                    token);

                if (r.ExitCode != 0)
                    throw new Exception(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
            });

        if (ok)
            await Dialogs.ShowInfoAsync("Готово", "Команда сжатия выполнена.");
        else if (error != null)
            await Dialogs.ShowErrorAsync("Ошибка", error);
        // cancelled → ничего не показываем.
    }

    // ===================== Диагностика =====================

    private TextBlock DiagText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
        Style = (Style)FindResource("ItemDescriptionText")
    };

    private async void RunDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var steps = new (string Label, Func<Task> Step)[]
        {
            (LocalizationService.T("Проверка образа системы (DISM)…"), DiagnosticsService.CheckImageAsync),
            (LocalizationService.T("Проверка диска C: (chkdsk /scan)…"), DiagnosticsService.CheckDiskAsync),
            (LocalizationService.T("Проверка системных файлов (SFC)…"), DiagnosticsService.SfcAsync),
            (LocalizationService.T("Проверка хранилища компонентов…"), DiagnosticsService.ScanSystemFilesAsync),
            (LocalizationService.T("Проверка загрузчика (BCD)…"), DiagnosticsService.CheckBootAsync),
        };

        var (ok, error) = await ProgressWindow.RunAsync(Window.GetWindow(this), LocalizationService.T("Диагностика системы"), steps);

        if (ok)
            await Dialogs.ShowInfoAsync("Готово",
                "Диагностика завершена. Windows проверила образ системы, диск, системные файлы и загрузчик и автоматически исправила найденные проблемы. Обычно больше ничего делать не нужно; если система попросит — перезагрузите ПК.");
        else if (error != null)
            await Dialogs.ShowErrorAsync("Ошибка", error);
    }

    private void SmartDisks_Click(object sender, RoutedEventArgs e)
    {
        // Состояние накопителей показываем в отдельном окне: там оценка в процентах,
        // словесная оценка (отлично/нормально/плохо/ужасно) и данные SMART.
        var window = new DisksWindow { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    private async void Drivers_Click(object sender, RoutedEventArgs e)
    {
        DriversHost.Children.Clear();
        DriversHost.Children.Add(DiagText(LocalizationService.T("Проверка устройств и драйверов…")));

        try
        {
            var problems = await DiagnosticsService.GetProblemDevicesAsync();
            var display = await DiagnosticsService.GetDisplayDriversAsync();

            DriversHost.Children.Clear();

            if (problems.Count == 0)
                DriversHost.Children.Add(DiagText(LocalizationService.T("Проблемных устройств не найдено.")));
            else
                foreach (var problem in problems)
                    DriversHost.Children.Add(DiagText(
                        LocalizationService.TF("⚠ {0} ({1}) — код ошибки {2}", problem.Name, problem.DeviceClass, problem.ErrorCode)));

            foreach (var driver in display)
                DriversHost.Children.Add(DiagText(
                    LocalizationService.TF("Видеодрайвер: {0} · версия {1} · {2}", driver.Model, driver.Version, driver.Date)));
        }
        catch (Exception ex)
        {
            DriversHost.Children.Clear();
            DriversHost.Children.Add(DiagText(ex.Message));
        }
    }
    // ===================== Точки восстановления =====================

    private async void RestorePointList_Click(object sender, RoutedEventArgs e) => await LoadRestorePointsAsync();

    /// <summary>Показывает, сколько точек есть сейчас, и заполняет список.</summary>
    private async Task LoadRestorePointsAsync()
    {
        RestoreListHost.Children.Clear();
        RestoreStatusText.Text = LocalizationService.T("Чтение точек восстановления…");

        try
        {
            var points = await DiagnosticsService.GetRestorePointsAsync();
            RestoreListHost.Children.Clear();

            if (points.Count == 0)
            {
                RestoreStatusText.Text = LocalizationService.T(
                    "Точек восстановления нет. Включите защиту системы и создайте точку.");
                return;
            }

            RestoreStatusText.Text = LocalizationService.TF("Доступно точек восстановления: {0}.", points.Count);

            foreach (var point in points)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock { Text = point.Description, Style = (Style)FindResource("ItemTitleText") });
                info.Children.Add(new TextBlock { Text = point.Created, Style = (Style)FindResource("ItemDescriptionText") });
                Grid.SetColumn(info, 0);
                grid.Children.Add(info);

                var restore = new Wpf.Ui.Controls.Button
                {
                    Content = LocalizationService.T("Откатить"),
                    Appearance = Wpf.Ui.Common.ControlAppearance.Secondary,
                    Padding = new Thickness(14, 6, 14, 6),
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = point.SequenceNumber
                };
                restore.Click += RestorePointApply_Click;
                Grid.SetColumn(restore, 1);
                grid.Children.Add(restore);

                RestoreListHost.Children.Add(grid);
            }
        }
        catch (Exception ex)
        {
            RestoreListHost.Children.Clear();
            RestoreStatusText.Text = string.Empty;
            RestoreListHost.Children.Add(DiagText(ex.Message));
        }
    }
    private async void RestorePointApply_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not int sequence)
            return;

        var confirm = await Dialogs.ShowConfirmAsync("Откат системы",
            LocalizationService.TF("Восстановить систему к точке №{0}?\n\nКомпьютер будет перезагружен.", sequence));
        if (!confirm)
            return;

        try
        {
            await DiagnosticsService.RestoreToPointAsync(sequence);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }

    private async void EnableProtection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await DiagnosticsService.EnableSystemProtectionAsync();
            await LoadRestorePointsAsync();
            await Dialogs.ShowInfoAsync("Готово", "Защита системы включена для диска C:.");
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }
    // ===================== Анализ места на диске =====================

    private CancellationTokenSource? _diskAnalyzeCts;

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        var path = AnalyzePathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Укажите существующую папку, например C:\\");
            return;
        }

        _diskAnalyzeCts?.Cancel();
        _diskAnalyzeCts = new CancellationTokenSource();
        var token = _diskAnalyzeCts.Token;

        DiskAnalysisHost.Children.Clear();
        DiskAnalysisHost.Children.Add(DiagText(LocalizationService.T("Подсчёт размеров… это может занять время.")));

        try
        {
            var items = await DiskAnalyzerService.AnalyzeAsync(path, token);
            token.ThrowIfCancellationRequested();

            DiskAnalysisHost.Children.Clear();
            if (items.Count == 0)
            {
                DiskAnalysisHost.Children.Add(DiagText(LocalizationService.T("Папка пуста или нет доступа.")));
                return;
            }

            foreach (var item in items.Where(i => i.Bytes > 0).Take(40))
                DiskAnalysisHost.Children.Add(DiagText(
                    $"{DiskAnalyzerService.FormatSize(item.Bytes)} — {item.Name}"));
        }
        catch (OperationCanceledException)
        {
            DiskAnalysisHost.Children.Clear();
            DiskAnalysisHost.Children.Add(DiagText(LocalizationService.T("Анализ отменён.")));
        }
        catch (Exception ex)
        {
            DiskAnalysisHost.Children.Clear();
            DiskAnalysisHost.Children.Add(DiagText(ex.Message));
        }
    }

    private void CancelAnalyze_Click(object sender, RoutedEventArgs e)
    {
        try { _diskAnalyzeCts?.Cancel(); } catch { /* уже завершено */ }
    }
    // ===================== Бэкап и откат твиков =====================

    private void RefreshBackupList()
    {
        BackupHost.Children.Clear();
        var snapshots = TweakBackupService.List();

        if (snapshots.Count == 0)
        {
            BackupHost.Children.Add(DiagText(LocalizationService.T("Снимков пока нет.")));
            return;
        }

        foreach (var item in snapshots)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = item.Name, Style = (Style)FindResource("ItemTitleText") });
            info.Children.Add(new TextBlock
            {
                Text = LocalizationService.TF("{0} · реестр: {1} · службы: {2} · задачи: {3}",
                    item.Created.ToString("dd.MM.yyyy HH:mm"), item.KeyCount, item.ServiceCount, item.TaskCount),
                Style = (Style)FindResource("ItemDescriptionText")
            });
            info.Children.Add(new TextBlock
            {
                Text = LocalizationService.TF("hosts: {0} · схема питания: {1} · загрузчик (BCD): {2}",
                    LocalizationService.T(item.HasHosts ? "да" : "нет"),
                    LocalizationService.T(item.HasPower ? "да" : "нет"),
                    LocalizationService.T(item.HasBcd ? "да" : "нет")),
                Style = (Style)FindResource("ItemDescriptionText")
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var restore = new Wpf.Ui.Controls.Button
            {
                Content = LocalizationService.T("Откатить"),
                Appearance = Wpf.Ui.Common.ControlAppearance.Primary,
                Padding = new Thickness(16, 6, 16, 6),
                Margin = new Thickness(0, 0, 8, 0),
                Tag = item.Name
            };
            restore.Click += TweakRestore_Click;

            var remove = new Wpf.Ui.Controls.Button
            {
                Content = LocalizationService.T("Удалить"),
                Appearance = Wpf.Ui.Common.ControlAppearance.Secondary,
                Padding = new Thickness(16, 6, 16, 6),
                Tag = item.Name
            };
            remove.Click += TweakDelete_Click;

            buttons.Children.Add(restore);
            buttons.Children.Add(remove);
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            BackupHost.Children.Add(grid);
        }
    }
    private async void TweakSnapshot_Click(object sender, RoutedEventArgs e)
    {
        string? createdName = null;

        var (ok, error, _) = await ProgressWindow.RunIndeterminateAsync(
            Window.GetWindow(this),
            LocalizationService.T("Создание снимка"),
            LocalizationService.T("Сохраняем реестр, службы, задачи, hosts, схему питания и загрузчик…"),
            async (_, _) => { createdName = await TweakBackupService.CreateAsync(); });

        if (!ok)
        {
            if (error != null)
                await Dialogs.ShowErrorAsync("Ошибка", error);
            return;
        }

        RefreshBackupList();
        await Dialogs.ShowInfoAsync(LocalizationService.T("Снимок создан"),
            LocalizationService.TF("Снимок «{0}» сохранён. Теперь можно применять твики.", createdName ?? string.Empty));
    }

    private void TweakSnapshotRefresh_Click(object sender, RoutedEventArgs e) => RefreshBackupList();

    private async void TweakRestore_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name)
            return;

        var confirm = await Dialogs.ShowConfirmAsync(LocalizationService.T("Откат изменений"),
            LocalizationService.TF("Вернуть реестр, службы, задачи, hosts, схему питания, загрузчик и настройки к снимку «{0}»? После отката рекомендуется перезагрузить ПК.", name));
        if (!confirm)
            return;

        try
        {
            var result = await TweakBackupService.RestoreAsync(name);
            await Dialogs.ShowInfoAsync(LocalizationService.T("Готово"),
                LocalizationService.TF("Откат выполнен: реестр — {0}, службы — {1}, задачи — {2}, hosts — {3}, схема питания — {4}, загрузчик — {5}. Перезагрузите ПК для полного применения.",
                    result.Keys,
                    result.Services,
                    result.Tasks,
                    LocalizationService.T(result.Hosts ? "да" : "нет"),
                    LocalizationService.T(result.Power ? "да" : "нет"),
                    LocalizationService.T(result.Bcd ? "да" : "нет")));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }
    private async void TweakDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string name)
            return;

        var confirm = await Dialogs.ShowConfirmAsync(LocalizationService.T("Удаление снимка"),
            LocalizationService.TF("Удалить снимок «{0}»?", name));
        if (!confirm)
            return;

        try
        {
            TweakBackupService.Delete(name);
            RefreshBackupList();
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"), ex.Message);
        }
    }
}
