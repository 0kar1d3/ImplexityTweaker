using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

/// <summary>
/// Страница «Бенчмарк»: замер процессора (одно ядро и все ядра), памяти, диска, сети
/// и времени последней загрузки Windows, индекс Implexity, сравнение с прошлым прогоном
/// и история сохранённых результатов.
/// </summary>
public partial class BenchmarkPage : Page
{
    private bool _running;

    public BenchmarkPage()
    {
        InitializeComponent();

        HeaderText.Text = LocalizationService.TF(
            "{0} · тест занимает около минуты, запускайте его без тяжёлых задач в фоне.",
            WindowsVersionService.DescribeSystem());

        RunHintText.Text = LocalizationService.T(
            "Замеряем процессор, память, диск и время последней загрузки Windows. Результат сохраняется на диск, поэтому прогоны можно сравнивать между собой.");

        FooterText.Text = LocalizationService.TF(
            "Результаты лежат в папке {0}", BenchmarkService.ResultsFolder);

        RefreshResults();
    }

    /// <summary>Перечитывает историю прогонов и перерисовывает последний результат и список.</summary>
    private void RefreshResults()
    {
        var history = BenchmarkService.LoadHistory();

        var last = history.Count > 0 ? history[0] : null;
        var previous = history.Count > 1 ? history[1] : null;

        LastPanel.Visibility = last is null ? Visibility.Collapsed : Visibility.Visible;
        NoLastText.Visibility = last is null ? Visibility.Visible : Visibility.Collapsed;
        NoHistoryText.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (last != null)
        {
            LastIndexText.Text = Math.Round(last.Index, 0).ToString("0", CultureInfo.CurrentCulture);
            LastMetaText.Text = LocalizationService.TF(
                "{0} · {1} · ядер: {2}",
                last.Created.ToString("d MMMM yyyy, HH:mm", CultureInfo.CurrentCulture),
                last.CpuName,
                last.LogicalCores);
            LastDeltaText.Text = BuildIndexDelta(last, previous);
        }

        MetricList.ItemsSource = last is null ? null : BuildMetrics(last, previous);
        HistoryList.ItemsSource = history.Select((result, index) => new BenchmarkHistoryRow(result, index == 0)).ToList();
    }

    /// <summary>Текстовое сравнение индекса последнего прогона с предыдущим.</summary>
    private static string BuildIndexDelta(BenchmarkResult last, BenchmarkResult? previous)
    {
        if (previous is null)
            return LocalizationService.T("Это первый прогон — сравнить пока не с чем.");

        var delta = last.Index - previous.Index;

        if (Math.Abs(delta) < 1)
            return LocalizationService.T("Индекс совпал с прошлым прогоном.");

        return delta > 0
            ? LocalizationService.TF("На {0} ед. выше прошлого прогона.", Math.Round(delta, 0))
            : LocalizationService.TF("На {0} ед. ниже прошлого прогона.", Math.Round(-delta, 0));
    }

    /// <summary>Строки метрик последнего прогона с разницей относительно предыдущего.</summary>
    private static List<BenchmarkMetricRow> BuildMetrics(BenchmarkResult last, BenchmarkResult? previous)
    {
        var rows = new List<BenchmarkMetricRow>();
        var current = last.Metrics;
        var older = previous?.Metrics;

        for (int i = 0; i < current.Count; i++)
        {
            var metric = current[i];

            double? delta = null;
            if (older != null && i < older.Count && (older[i].Value > 0 || metric.Value > 0))
                delta = Math.Round(metric.Value - older[i].Value, 1);

            rows.Add(new BenchmarkMetricRow(
                LocalizationService.T(metric.Title),
                metric.Value,
                metric.Unit,
                metric.LowerIsBetter,
                delta));
        }

        return rows;
    }


    /// <summary>Запускает тест в фоне и показывает прогресс прямо на странице.</summary>
    private async void RunBenchmark_Click(object sender, RoutedEventArgs e)
    {
        if (_running)
            return;

        _running = true;
        RunButton.IsEnabled = false;
        RunProgress.Value = 0;
        RunProgress.Visibility = Visibility.Visible;
        RunText.Visibility = Visibility.Visible;
        RunText.Text = LocalizationService.T("Подготовка…");

        var progress = new Progress<BenchmarkProgress>(value =>
        {
            RunProgress.Value = value.Percent;
            RunText.Text = LocalizationService.TF("{0} {1}%", LocalizationService.T(value.Label), value.Percent);
        });

        try
        {
            var result = await Task.Run(() => BenchmarkService.RunAsync(progress));

            BenchmarkService.Save(result);
            RefreshResults();

            await Dialogs.ShowInfoAsync(
                "Бенчмарк",
                LocalizationService.TF("Тест завершён. Индекс Implexity: {0} ед.", Math.Round(result.Index, 0)));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync("Ошибка бенчмарка", ex.Message);
        }
        finally
        {
            _running = false;
            RunButton.IsEnabled = true;
            RunProgress.Visibility = Visibility.Collapsed;
            RunText.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => BenchmarkService.OpenResultsFolder();

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshResults();

    private async void ShareResult_Click(object sender, RoutedEventArgs e)
    {
        var history = BenchmarkService.LoadHistory();
        if (history.Count == 0)
        {
            await Dialogs.ShowInfoAsync(
                LocalizationService.T("Бенчмарк"),
                LocalizationService.T("Сначала запустите тест производительности, чтобы поделиться карточкой результата."));
            return;
        }

        try
        {
            var last = history[0];
            var previous = history.Count > 1 ? history[1] : null;
            var path = ShareCardService.CopyBenchmarkCardToClipboard(last, previous);

            await Dialogs.ShowInfoAsync(
                LocalizationService.T("Карточка скопирована"),
                LocalizationService.TF("Карточка с индексом Implexity ({0} ед.) скопирована в буфер обмена как картинка (PNG) — можно сразу вставить (Ctrl+V) в Discord или Telegram!\n\nФайл также сохранён:\n{1}",
                    Math.Round(last.Index, 0), path));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"), ex.Message);
        }
    }

}

/// <summary>Строка метрики в блоке последнего прогона (значение и разница к прошлому замеру).</summary>
public sealed class BenchmarkMetricRow
{
    private static readonly Brush Better = Frozen("#7BDCB0");
    private static readonly Brush Worse = Frozen("#F08A8A");

    public BenchmarkMetricRow(string label, double value, string unit, bool lowerIsBetter, double? delta)
    {
        Label = label;
        ValueText = value <= 0
            ? LocalizationService.T("нет данных")
            : Math.Round(value, 1).ToString("0.#", CultureInfo.CurrentCulture) + " " + LocalizationService.T(unit);

        DeltaText = string.Empty;
        DeltaVisibility = Visibility.Collapsed;
        DeltaForeground = Brushes.Gray;

        if (delta is null || Math.Abs(delta.Value) < 0.05)
            return;

        var improved = lowerIsBetter ? delta.Value < 0 : delta.Value > 0;
        var sign = delta.Value > 0 ? "+" : "−";

        DeltaText = sign + Math.Round(Math.Abs(delta.Value), 1).ToString("0.#", CultureInfo.CurrentCulture) + " " + LocalizationService.T(unit);
        DeltaForeground = improved ? Better : Worse;
        DeltaVisibility = Visibility.Visible;
    }

    public string Label { get; }

    public string ValueText { get; }

    public string DeltaText { get; }

    public Visibility DeltaVisibility { get; }

    public Brush DeltaForeground { get; }

    private static Brush Frozen(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

/// <summary>Строка истории прогонов.</summary>
public sealed class BenchmarkHistoryRow
{
    public BenchmarkHistoryRow(BenchmarkResult result, bool isLatest)
    {
        When = result.Created.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);
        IndexText = LocalizationService.TF("{0} ед.", Math.Round(result.Index, 0));
        Summary = LocalizationService.TF(
            "{0} · CPU {1} / {2} MOps/s · RAM {3} ГБ/с",
            result.OsLabel,
            Math.Round(result.CpuSingle, 0),
            Math.Round(result.CpuMulti, 0),
            Math.Round(result.Memory, 1));
        LatestVisibility = isLatest ? Visibility.Visible : Visibility.Collapsed;
    }

    public string When { get; }

    public string Summary { get; }

    public string IndexText { get; }

    public Visibility LatestVisibility { get; }
}

