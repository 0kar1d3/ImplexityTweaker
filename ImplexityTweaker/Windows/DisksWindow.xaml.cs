using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

/// <summary>
/// Отдельное окно с состоянием накопителей: оценка здоровья в процентах,
/// словесная оценка (отлично / нормально / плохо / ужасно) и данные SMART.
/// </summary>
public partial class DisksWindow : Window
{
    private const int DwmwaImmersiveDarkMode = 20;
    private const int DwmwaImmersiveDarkModeOld = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private bool _busy;

    public DisksWindow()
    {
        InitializeComponent();

        SubtitleText.Text = LocalizationService.T(
            "Оценка состояния накопителей по данным SMART. Значения ориентировочные.");
        LegendText.Text = LocalizationService.T(
            "Шкала: Отлично 90–100 % · Нормально 70–89 % · Плохо 40–69 % · Ужасно 0–39 %");

        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        if (_busy)
            return;

        _busy = true;
        RefreshButton.IsEnabled = false;
        LoadingIndicator.Visibility = Visibility.Visible;
        InfoText.Visibility = Visibility.Collapsed;
        SummaryText.Text = string.Empty;
        DisksHost.Children.Clear();

        try
        {
            var disks = await StorageService.GetHealthAsync();

            if (disks.Count == 0)
            {
                ShowInfo(LocalizationService.T("Накопители не найдены."));
                return;
            }

            foreach (var disk in disks)
                DisksHost.Children.Add(BuildDiskCard(disk));

            // Худшая оценка — чтобы сразу было видно, есть ли проблемный накопитель.
            var worst = disks
                .Where(d => d.HealthPercent.HasValue)
                .OrderBy(d => d.HealthPercent.GetValueOrDefault())
                .FirstOrDefault();

            SummaryText.Text = LocalizationService.TF("Накопителей: {0} · худшая оценка: {1}",
                disks.Count,
                worst == null ? LocalizationService.T("Нет данных") : GradeWord(worst.HealthPercent));
        }
        catch (Exception ex)
        {
            ShowInfo(LocalizationService.T("Ошибка чтения данных SMART.") + " " + ex.Message);
        }
        finally
        {
            LoadingIndicator.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            _busy = false;
        }
    }

    private void ShowInfo(string text)
    {
        InfoText.Text = text;
        InfoText.Visibility = Visibility.Visible;
    }

    /// <summary>Тёмный заголовок окна в тёмных темах (косметика).</summary>
    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var value = 1;
            if (DwmSetWindowAttribute(handle, DwmwaImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, DwmwaImmersiveDarkModeOld, ref value, sizeof(int));
        }
        catch
        {
            // без тёмного заголовка окно просто останется системным
        }
    }

    // ===================== Оценка и оформление =====================

    /// <summary>Безопасно берёт кисть темы: если ресурса нет — возвращает запасную.</summary>
    private static Brush Res(string key, Brush fallback) =>
        Application.Current?.Resources[key] as Brush ?? fallback;

    private static Brush GradeBrush(StorageService.DiskGrade grade) => grade switch
    {
        StorageService.DiskGrade.Excellent => new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E)),
        StorageService.DiskGrade.Normal => new SolidColorBrush(Color.FromRgb(0xA3, 0xE6, 0x35)),
        StorageService.DiskGrade.Poor => new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)),
        StorageService.DiskGrade.Terrible => new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
        _ => Res("App.TextMuted", Brushes.Gray)
    };

    private static string GradeWord(int? health) => StorageService.GradeOf(health) switch
    {
        StorageService.DiskGrade.Excellent => LocalizationService.T("Отлично"),
        StorageService.DiskGrade.Normal => LocalizationService.T("Нормально"),
        StorageService.DiskGrade.Poor => LocalizationService.T("Плохо"),
        StorageService.DiskGrade.Terrible => LocalizationService.T("Ужасно"),
        _ => LocalizationService.T("Нет данных")
    };

    private static string MetaLine(StorageService.DiskHealth disk)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(disk.Media))
            parts.Add(disk.Media);
        parts.Add($"{disk.SizeGb:0.#} {LocalizationService.T("ГБ")}");
        parts.Add(LocalizationService.T(disk.StatusWord));
        return string.Join(" · ", parts);
    }

    private static Grid BuildHealthBar(int? percent, Brush fillBrush, Brush borderBrush)
    {
        var value = Math.Clamp(percent.GetValueOrDefault(), 0, 100);

        var bar = new Grid { Height = 8, Margin = new Thickness(0, 11, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(value, 1), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - value, 1), GridUnitType.Star) });

        var track = new Border { Background = borderBrush, CornerRadius = new CornerRadius(4), Opacity = 0.7 };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);

        if (percent.HasValue && value > 0)
        {
            var fill = new Border { Background = fillBrush, CornerRadius = new CornerRadius(4) };
            Grid.SetColumn(fill, 0);
            bar.Children.Add(fill);
        }

        return bar;
    }

    private static Border BuildChip(string label, string value, Brush background, Brush borderBrush, Brush muted)
    {
        var chip = new Border
        {
            Background = background,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6)
        };

        chip.Child = new TextBlock
        {
            Text = $"{LocalizationService.T(label)}: {value}",
            FontSize = 11.5,
            Foreground = muted
        };

        return chip;
    }

    /// <summary>Карточка накопителя: название, оценка в процентах, полоса и данные SMART.</summary>
    private static Border BuildDiskCard(StorageService.DiskHealth disk)
    {
        var surface = Res("App.SurfaceAlt", Brushes.Transparent);
        var background = Res("App.Background", Brushes.Transparent);
        var border = Res("App.Border", Brushes.Gray);
        var muted = Res("App.TextMuted", Brushes.Gray);
        var gradeBrush = GradeBrush(disk.Grade);

        var card = new Border
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 0, 10)
        };

        var root = new Grid();
        for (var i = 0; i < 3; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // --- строка 1: маркер оценки, название и характеристики, процент ---
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = gradeBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        var titleBlock = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        titleBlock.Children.Add(new TextBlock
        {
            Text = disk.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        titleBlock.Children.Add(new TextBlock
        {
            Text = MetaLine(disk),
            FontSize = 12,
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });
        Grid.SetColumn(titleBlock, 1);
        header.Children.Add(titleBlock);

        var healthBlock = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        healthBlock.Children.Add(new TextBlock
        {
            Text = disk.HealthPercent.HasValue ? $"{disk.HealthPercent.Value} %" : "—",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = gradeBrush,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        healthBlock.Children.Add(new TextBlock
        {
            Text = GradeWord(disk.HealthPercent),
            FontSize = 12,
            Foreground = gradeBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 0, 0)
        });
        Grid.SetColumn(healthBlock, 2);
        header.Children.Add(healthBlock);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // --- строка 2: полоса здоровья ---
        var bar = BuildHealthBar(disk.HealthPercent, gradeBrush, border);
        Grid.SetRow(bar, 1);
        root.Children.Add(bar);

        // --- строка 3: показатели SMART ---
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        chips.Children.Add(BuildChip("Тип", string.IsNullOrEmpty(disk.Media) ? "—" : disk.Media, background, border, muted));
        chips.Children.Add(BuildChip("Объём", $"{disk.SizeGb:0.#} {LocalizationService.T("ГБ")}", background, border, muted));
        chips.Children.Add(BuildChip("Состояние", LocalizationService.T(disk.StatusWord), background, border, muted));
        chips.Children.Add(BuildChip("Температура",
            disk.Temperature.HasValue ? $"{disk.Temperature.Value} °C" : "—", background, border, muted));
        chips.Children.Add(BuildChip("Макс. температура",
            disk.TemperatureMax.HasValue ? $"{disk.TemperatureMax.Value} °C" : "—", background, border, muted));
        chips.Children.Add(BuildChip("Износ",
            disk.WearPercent.HasValue ? $"{disk.WearPercent.Value} %" : "—", background, border, muted));
        chips.Children.Add(BuildChip("Наработка",
            disk.PowerOnHours.HasValue ? LocalizationService.TF("{0} ч", disk.PowerOnHours.Value) : "—",
            background, border, muted));
        chips.Children.Add(BuildChip("Ошибки чтения", disk.ReadErrors?.ToString() ?? "—", background, border, muted));
        chips.Children.Add(BuildChip("Ошибки записи", disk.WriteErrors?.ToString() ?? "—", background, border, muted));
        chips.Children.Add(BuildChip("SMART-прогноз", disk.PredictFailure switch
        {
            true => LocalizationService.T("Возможен сбой"),
            false => LocalizationService.T("Всё в порядке"),
            _ => "—"
        }, background, border, muted));

        Grid.SetRow(chips, 2);
        root.Children.Add(chips);

        card.Child = root;
        return card;
    }
}
