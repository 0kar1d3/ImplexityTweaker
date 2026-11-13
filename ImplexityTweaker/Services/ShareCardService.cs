using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImplexityTweaker.Services;

/// <summary>
/// Генерация красивой PNG-карточки результатов бенчмарка и характеристик ПК
/// с копированием изображения прямо в буфер обмена (для отправки в Discord / Telegram).
/// </summary>
public static class ShareCardService
{
    public static string SharesFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Implexity", "shares");

    public static string CopyBenchmarkCardToClipboard(BenchmarkResult result, BenchmarkResult? previous)
    {
        var bg = BrushFromResource("App.Background", "#050a14");
        var surface = BrushFromResource("App.Surface", "#0c1528");
        var surfaceAlt = BrushFromResource("App.SurfaceAlt", "#101e36");
        var border = BrushFromResource("App.Border", "#1a2840");
        var accent = BrushFromResource("App.Accent", "#6366f1");
        var iconAccent = BrushFromResource("App.IconAccent", "#818cf8");
        var text = BrushFromResource("App.Text", "#f8fafc");
        var muted = BrushFromResource("App.TextMuted", "#94a3b8");

        var root = new Border
        {
            Width = 760,
            Background = bg,
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(28, 24, 28, 24)
        };

        var mainStack = new StackPanel();

        // Верхняя шапка с брендингом и индексом
        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var brandStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        brandStack.Children.Add(new TextBlock
        {
            Text = "⚡ IMPLEXITY TWEAKER · BENCHMARK",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = iconAccent
        });
        brandStack.Children.Add(new TextBlock
        {
            Text = result.CpuName,
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 2)
        });
        brandStack.Children.Add(new TextBlock
        {
            Text = $"{result.OsLabel} · {LocalizationService.T("потоков")}: {result.LogicalCores} · {result.Created:dd.MM.yyyy HH:mm}",
            FontSize = 12,
            Foreground = muted
        });
        Grid.SetColumn(brandStack, 0);
        headerGrid.Children.Add(brandStack);

        var scoreBox = new Border
        {
            Background = surfaceAlt,
            BorderBrush = accent,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(20, 10, 20, 10),
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var scoreStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        scoreStack.Children.Add(new TextBlock
        {
            Text = Math.Round(result.Index, 0).ToString("0", CultureInfo.CurrentCulture),
            FontSize = 34,
            FontWeight = FontWeights.Bold,
            Foreground = iconAccent,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        scoreStack.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Индекс Implexity"),
            FontSize = 11,
            Foreground = muted,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        scoreBox.Child = scoreStack;
        Grid.SetColumn(scoreBox, 1);
        headerGrid.Children.Add(scoreBox);

        mainStack.Children.Add(headerGrid);

        // Таблица метрик
        var metricsBorder = new Border
        {
            Background = surface,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18, 14, 18, 14)
        };

        var metricsStack = new StackPanel();
        for (int i = 0; i < result.Metrics.Count; i++)
        {
            var m = result.Metrics[i];
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = LocalizationService.T(m.Title),
                FontSize = 13,
                Foreground = text,
                VerticalAlignment = VerticalAlignment.Center
            });

            var valText = m.Value <= 0
                ? LocalizationService.T("нет данных")
                : $"{Math.Round(m.Value, 1):0.#} {LocalizationService.T(m.Unit)}";

            var valBlock = new TextBlock
            {
                Text = valText,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = iconAccent,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valBlock, 1);
            row.Children.Add(valBlock);
            metricsStack.Children.Add(row);
        }

        metricsBorder.Child = metricsStack;
        mainStack.Children.Add(metricsBorder);

        // Футер
        mainStack.Children.Add(AppendFooter(muted, iconAccent));
        root.Child = mainStack;

        return RenderAndCopy(root, 760, "Implexity-Benchmark");
    }

    public static string CopyPcInfoCardToClipboard(
        string machineName,
        string osText,
        string cpuText,
        string gpuText,
        string ramText,
        string boardText,
        string diskText,
        string biosText = "",
        string networkText = "")
    {
        var bg = BrushFromResource("App.Background", "#050a14");
        var surface = BrushFromResource("App.Surface", "#0c1528");
        var border = BrushFromResource("App.Border", "#1a2840");
        var accent = BrushFromResource("App.Accent", "#6366f1");
        var iconAccent = BrushFromResource("App.IconAccent", "#818cf8");
        var text = BrushFromResource("App.Text", "#f8fafc");
        var muted = BrushFromResource("App.TextMuted", "#94a3b8");

        var root = new Border
        {
            Width = 820,
            Background = bg,
            BorderBrush = accent,
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(28, 24, 28, 24)
        };

        var mainStack = new StackPanel();

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock
        {
            Text = "⚡ IMPLEXITY TWEAKER · SYSTEM SPECS",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = iconAccent
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{machineName} · {DateTime.Now:dd.MM.yyyy}",
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = text,
            Margin = new Thickness(0, 4, 0, 0)
        });
        mainStack.Children.Add(header);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var leftCol = new StackPanel();
        var rightCol = new StackPanel();

        Border SpecTile(string title, string body)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = iconAccent,
                Margin = new Thickness(0, 0, 0, 6)
            });
            sp.Children.Add(new TextBlock
            {
                Text = TruncateLines(body, 40),
                FontSize = 12,
                Foreground = text,
                TextWrapping = TextWrapping.Wrap
            });

            return new Border
            {
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10),
                Child = sp
            };
        }

        leftCol.Children.Add(SpecTile(LocalizationService.T("Процессор"), cpuText));
        leftCol.Children.Add(SpecTile(LocalizationService.T("Видеокарта"), gpuText));
        leftCol.Children.Add(SpecTile(LocalizationService.T("Оперативная память"), ramText));
        if (!string.IsNullOrWhiteSpace(networkText))
            leftCol.Children.Add(SpecTile(LocalizationService.T("Сеть"), networkText));

        rightCol.Children.Add(SpecTile(LocalizationService.T("Операционная система"), osText));
        rightCol.Children.Add(SpecTile(LocalizationService.T("Материнская плата"), boardText));
        if (!string.IsNullOrWhiteSpace(biosText))
            rightCol.Children.Add(SpecTile("BIOS / UEFI", biosText));
        rightCol.Children.Add(SpecTile(LocalizationService.T("Накопители"), diskText));

        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(rightCol, 2);
        grid.Children.Add(leftCol);
        grid.Children.Add(rightCol);
        mainStack.Children.Add(grid);

        mainStack.Children.Add(AppendFooter(muted, iconAccent));
        root.Child = mainStack;

        return RenderAndCopy(root, 820, "Implexity-Specs");
    }

    private static UIElement AppendFooter(Brush muted, Brush accent)
    {
        var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var userText = AccountService.Current is { } acc
            ? LocalizationService.TF("Профиль: {0} ({1})", acc.DisplayName, acc.AccountNumber)
            : "implexity.fun · github.com/0kar1d3/ImplexityTweaker";

        grid.Children.Add(new TextBlock
        {
            Text = userText,
            FontSize = 11,
            Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center
        });

        var badge = new TextBlock
        {
            Text = "Implexity Tweaker v2.0",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = accent,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        return grid;
    }

    private static string TruncateLines(string input, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "—";

        var lines = input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= maxLines)
            return string.Join("\n", lines);

        return string.Join("\n", lines[..maxLines]) + "\n…";
    }

    private static string RenderAndCopy(FrameworkElement element, double width, string filePrefix)
    {
        element.Measure(new Size(width, double.PositiveInfinity));
        var desired = element.DesiredSize;
        double height = Math.Max(260, Math.Ceiling(desired.Height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        // Рендерим с 2x DPI (192 DPI) для максимальной чёткости в Discord/Telegram
        const double scale = 2.0;
        int pixelWidth = (int)Math.Round(width * scale);
        int pixelHeight = (int)Math.Round(height * scale);

        var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(element);
        rtb.Freeze();

        Clipboard.SetImage(rtb);

        Directory.CreateDirectory(SharesFolder);
        var filePath = Path.Combine(SharesFolder, $"{filePrefix}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            encoder.Save(fs);
        }

        return filePath;
    }

    private static Brush BrushFromResource(string key, string fallbackHex)
    {
        try
        {
            if (Application.Current?.Resources[key] is Brush b)
                return b;
        }
        catch { }

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallbackHex)!);
        brush.Freeze();
        return brush;
    }
}
