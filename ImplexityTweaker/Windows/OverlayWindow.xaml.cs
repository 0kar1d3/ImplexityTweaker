using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

/// <summary>
/// Поверх-игровое HUD-окно ImplexityOverlay.
/// Полностью прозрачно для кликов мыши (WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW),
/// не перехватывает фокус и не отображается в Alt+Tab.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    public sealed record MetricChip(string Label, string Value, Brush ValueBrush, Brush? DotBrush = null, bool IsHero = false);

    private static Brush FreezeBrush(byte r, byte g, byte b, byte a = 255)
    {
        var br = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        br.Freeze();
        return br;
    }

    private static DropShadowEffect CreateShadow(double blur, double depth, double opacity)
    {
        var fx = new DropShadowEffect
        {
            BlurRadius = blur,
            ShadowDepth = depth,
            Direction = 315,
            Opacity = opacity,
            Color = Colors.Black
        };
        fx.Freeze();
        return fx;
    }

    // Фиксированные оптимизированные кисти и тени без аллокаций на каждый тик
    private static readonly DropShadowEffect TextOutlineShadow = CreateShadow(3.0, 1.5, 0.96);
    private static readonly DropShadowEffect CardSoftShadow = CreateShadow(16.0, 3.0, 0.42);
    private static readonly Brush TelemetryGpuLabel = FreezeBrush(0x4A, 0xDE, 0x80);
    private static readonly Brush TelemetryCpuLabel = FreezeBrush(0x60, 0xA5, 0xFA);
    private static readonly Brush TelemetryRamLabel = FreezeBrush(0x22, 0xD3, 0xEE);
    private static readonly Brush TelemetryFpsLabel = FreezeBrush(0xFB, 0x71, 0x85);
    private static readonly Brush TelemetryAmberVal = FreezeBrush(0xFF, 0x9F, 0x43);
    private static readonly Brush TelemetryUnitBrush = FreezeBrush(0xE2, 0xE8, 0xF0, 210);
    private static readonly Brush TelemetryWhiteBrush = FreezeBrush(0xFF, 0xFF, 0xFF);
    private static readonly Brush TelemetryCritBrush = FreezeBrush(0xF8, 0x71, 0x71);

    private static readonly Brush DotCpuBrush = FreezeBrush(0x38, 0xBD, 0xF8);
    private static readonly Brush DotGpuBrush = FreezeBrush(0x81, 0x8C, 0xF8);
    private static readonly Brush DotRamBrush = FreezeBrush(0x2D, 0xD4, 0xBF);
    private static readonly Brush DotSubtleBrush = FreezeBrush(0x94, 0xA3, 0xB8);

    private static readonly Brush SubtleDarkTile = FreezeBrush(0xFF, 0xFF, 0xFF, 0x12);
    private static readonly Brush SubtleDarkHeroTile = FreezeBrush(0xFF, 0xFF, 0xFF, 0x1A);
    private static readonly Brush SubtleTrackBg = FreezeBrush(0xFF, 0xFF, 0xFF, 0x22);
    private static readonly Brush SubtleLightTile = FreezeBrush(0x00, 0x00, 0x00, 0x0D);
    private static readonly Brush SubtleLightTrack = FreezeBrush(0x00, 0x00, 0x00, 0x1A);

    private static readonly Brush GraphBoxBg = FreezeBrush(0x00, 0x00, 0x00, 0x58);
    private static readonly Brush GraphBoxBorder = FreezeBrush(0xFF, 0xFF, 0xFF, 0x2E);
    private static readonly Brush GraphGridLine = FreezeBrush(0xFF, 0xFF, 0xFF, 0x26);
    private static readonly Brush GraphLabelBrush = FreezeBrush(0xCB, 0xD5, 0xE1, 0xDD);
    private static readonly Brush GraphGoodFill = FreezeGradient(Color.FromArgb(0x55, 0x4A, 0xDE, 0x80), Color.FromArgb(0x08, 0x4A, 0xDE, 0x80));
    private static readonly Brush GraphWarnFill = FreezeGradient(Color.FromArgb(0x55, 0xFB, 0xBF, 0x24), Color.FromArgb(0x08, 0xFB, 0xBF, 0x24));
    private static readonly Brush GraphLowFill = FreezeGradient(Color.FromArgb(0x55, 0xFB, 0x71, 0x85), Color.FromArgb(0x08, 0xFB, 0x71, 0x85));

    private static Brush FreezeGradient(Color top, Color bottom)
    {
        var g = new LinearGradientBrush(top, bottom, 90);
        g.Freeze();
        return g;
    }

    private IntPtr _hwnd;
    private Brush _labelBrush = Brushes.LightGray;
    private Brush _valueBrush = Brushes.White;
    private Brush _accentBrush = Brushes.CornflowerBlue;
    private Brush _warnBrush = Brushes.Gold;
    private Brush _dangerBrush = TelemetryCritBrush;
    private Brush _goodBrush = Brushes.LightGreen;
    private bool _isLightSurface;
    private bool _dragMode;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyClickThroughStyles();
        SizeChanged += (_, _) => Reposition(OverlayService.CurrentSettings);
        MouseLeftButtonDown += OverlayWindow_MouseLeftButtonDown;
    }

    public void SetDragMode(bool enabled)
    {
        _dragMode = enabled;
        Cursor = enabled ? System.Windows.Input.Cursors.SizeAll : System.Windows.Input.Cursors.Arrow;
        ApplyClickThroughStyles();
    }

    private void OverlayWindow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_dragMode)
            return;

        try
        {
            DragMove();
            var s = OverlayService.CurrentSettings;
            s.Position = OverlayPosition.Custom;
            s.CustomLeft = Math.Round(Left);
            s.CustomTop = Math.Round(Top);
            OverlaySettings.Save(s);
        }
        catch { }
    }

    private void ApplyClickThroughStyles()
    {
        try
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            if (_hwnd == IntPtr.Zero)
                return;

            long ex = GetWindowLongPtr64(_hwnd, GWL_EXSTYLE).ToInt64();
            ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            if (_dragMode)
                ex &= ~WS_EX_TRANSPARENT;
            else
                ex |= WS_EX_TRANSPARENT;
            SetWindowLongPtr64(_hwnd, GWL_EXSTYLE, new IntPtr(ex));
        }
        catch { }
    }

    public void EnsureTopmost()
    {
        if (_hwnd == IntPtr.Zero)
            return;

        try
        {
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        catch { }
    }

    public void ApplySettingsAndRender(OverlaySettings s, OverlayTelemetrySnapshot data)
    {
        ApplyStyle(s);

        bool horizontal = s.VisualStyle != OverlayVisualStyle.TelemetryText && s.Layout == OverlayLayoutMode.Horizontal;
        MetricsPanel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;

        ConfigureHeader(s, data);
        MetricsPanel.Children.Clear();

        switch (s.VisualStyle)
        {
            case OverlayVisualStyle.TelemetryText:
                RenderTelemetryText(s, data);
                break;
            case OverlayVisualStyle.RoundedGlass:
                RenderRoundedGlass(s, data, horizontal);
                break;
            case OverlayVisualStyle.GamingBars:
                RenderGamingBars(s, data, horizontal);
                break;
            case OverlayVisualStyle.Classic:
            default:
                RenderClassic(s, data, horizontal);
                break;
        }

        if (MetricsPanel.Children.Count == 0)
        {
            MetricsPanel.Children.Add(new TextBlock
            {
                Text = "ImplexityOverlay",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = s.VisualStyle == OverlayVisualStyle.TelemetryText ? TelemetryWhiteBrush : _valueBrush
            });
        }

        Reposition(s);
    }

    private void ConfigureHeader(OverlaySettings s, OverlayTelemetrySnapshot data)
    {
        if (!s.ShowProcessName || string.IsNullOrWhiteSpace(data.ProcessName))
        {
            HeaderRow.Visibility = Visibility.Collapsed;
            return;
        }

        HeaderRow.Visibility = Visibility.Visible;
        ProcessLabel.Text = data.ProcessName;

        switch (s.VisualStyle)
        {
            case OverlayVisualStyle.TelemetryText:
                ProcessDot.Visibility = Visibility.Collapsed;
                ProcessLabel.Foreground = TelemetryFpsLabel;
                ProcessLabel.FontSize = 13.5;
                ProcessLabel.FontWeight = FontWeights.Bold;
                HeaderRow.Margin = new Thickness(0, 0, 0, 6);
                break;

            case OverlayVisualStyle.RoundedGlass:
                ProcessDot.Visibility = Visibility.Visible;
                ProcessDot.Width = 6;
                ProcessDot.Height = 6;
                ProcessDot.CornerRadius = new CornerRadius(3);
                ProcessDot.Background = _goodBrush;
                ProcessLabel.Foreground = _valueBrush;
                ProcessLabel.FontSize = 11;
                ProcessLabel.FontWeight = FontWeights.SemiBold;
                HeaderRow.Margin = new Thickness(0, 0, 0, 6);
                break;

            case OverlayVisualStyle.GamingBars:
                ProcessDot.Visibility = Visibility.Visible;
                ProcessDot.Width = 3;
                ProcessDot.Height = 11;
                ProcessDot.CornerRadius = new CornerRadius(1.5);
                ProcessDot.Background = _accentBrush;
                ProcessLabel.Foreground = _labelBrush;
                ProcessLabel.FontSize = 10.5;
                ProcessLabel.FontWeight = FontWeights.SemiBold;
                HeaderRow.Margin = new Thickness(0, 0, 0, 7);
                break;

            case OverlayVisualStyle.Classic:
            default:
                ProcessDot.Visibility = Visibility.Visible;
                ProcessDot.Width = 6;
                ProcessDot.Height = 6;
                ProcessDot.CornerRadius = new CornerRadius(3);
                ProcessDot.Background = _accentBrush;
                ProcessLabel.Foreground = _labelBrush;
                ProcessLabel.FontSize = 11;
                ProcessLabel.FontWeight = FontWeights.SemiBold;
                HeaderRow.Margin = new Thickness(0, 0, 0, 5);
                break;
        }
    }

    private void RenderClassic(OverlaySettings s, OverlayTelemetrySnapshot data, bool horizontal)
    {
        var items = BuildItems(s, data);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (horizontal)
            {
                if (i > 0)
                {
                    MetricsPanel.Children.Add(new Border
                    {
                        Width = 1,
                        Height = 12,
                        Margin = new Thickness(10, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = _labelBrush,
                        Opacity = 0.28
                    });
                }

                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _labelBrush,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                stack.Children.Add(new TextBlock
                {
                    Text = item.Value,
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = item.ValueBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                MetricsPanel.Children.Add(stack);
            }
            else
            {
                var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 0), MinWidth = 175 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    FontSize = 11.5,
                    Foreground = _labelBrush,
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var val = new TextBlock
                {
                    Text = item.Value,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = item.ValueBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(val, 1);
                row.Children.Add(val);
                MetricsPanel.Children.Add(row);
            }
        }
    }

    private void RenderTelemetryText(OverlaySettings s, OverlayTelemetrySnapshot data)
    {
        var entries = BuildTelemetryEntries(s, data);

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var row = new Grid { Margin = new Thickness(0, i == 0 ? 0 : 2.5, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(CreateTelemetryLabelBlock(entry.Category, entry.SubIndex, 14, entry.CategoryBrush));

            for (int c = 0; c < entry.Cells.Length && c < 4; c++)
            {
                var cell = entry.Cells[c];
                var tb = CreateUnitNumberBlock(
                    cell.Number,
                    cell.Unit,
                    cell.Brush,
                    14,
                    c >= 2 ? new Thickness(14, 0, 0, 0) : new Thickness(0));
                tb.HorizontalAlignment = c < 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                Grid.SetColumn(tb, c + 1);
                row.Children.Add(tb);
            }

            MetricsPanel.Children.Add(row);
        }

        if (s.ShowFramerateGraph)
        {
            MetricsPanel.Children.Add(BuildFramerateGraph(data.FramerateHistory, data.Fps, entries.Count > 0));
        }
    }

    private Border BuildFramerateGraph(double[]? history, double currentFps, bool addTopMargin)
    {
        const double canvasW = 244;
        const double canvasH = 38;

        var box = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = GraphBoxBg,
            BorderBrush = GraphBoxBorder,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, addTopMargin ? 8 : 0, 0, 0),
            Width = canvasW + 18,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var outer = new StackPanel();

        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Children.Add(new TextBlock
        {
            Text = "FRAMERATE",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = TelemetryFpsLabel,
            VerticalAlignment = VerticalAlignment.Center
        });

        string rangeText = "—";
        if (history is { Length: >= 2 })
        {
            double minV = double.MaxValue, maxV = double.MinValue;
            foreach (var v in history)
            {
                if (v < minV) minV = v;
                if (v > maxV) maxV = v;
            }
            rangeText = $"{minV:0}–{maxV:0} FPS";
        }
        else if (currentFps > 0)
        {
            rangeText = $"{currentFps:0} FPS";
        }

        var rangeBlock = new TextBlock
        {
            Text = rangeText,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = GraphLabelBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Typography.SetNumeralAlignment(rangeBlock, FontNumeralAlignment.Tabular);
        Grid.SetColumn(rangeBlock, 1);
        header.Children.Add(rangeBlock);
        outer.Children.Add(header);

        var canvas = new Canvas
        {
            Width = canvasW,
            Height = canvasH,
            ClipToBounds = true
        };

        // Средняя направляющая линия
        canvas.Children.Add(new Line
        {
            X1 = 0,
            X2 = canvasW,
            Y1 = canvasH * 0.5,
            Y2 = canvasH * 0.5,
            Stroke = GraphGridLine,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 3, 3 }
        });

        if (history is { Length: >= 2 })
        {
            double minVal = double.MaxValue, maxVal = double.MinValue;
            foreach (var v in history)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }

            double yMin = Math.Max(0, minVal * 0.85);
            double yMax = Math.Max(maxVal * 1.12, yMin + 15);
            double span = Math.Max(1.0, yMax - yMin);

            var linePts = new PointCollection(history.Length);
            var fillPts = new PointCollection(history.Length + 2) { new(0, canvasH) };

            for (int i = 0; i < history.Length; i++)
            {
                double x = history.Length == 1 ? canvasW : i * (canvasW / (history.Length - 1));
                double norm = Math.Clamp((history[i] - yMin) / span, 0.0, 1.0);
                double y = canvasH - 3 - norm * (canvasH - 6);
                var pt = new Point(x, y);
                linePts.Add(pt);
                fillPts.Add(pt);
            }
            fillPts.Add(new Point(canvasW, canvasH));

            double avgFps = currentFps > 0 ? currentFps : history[^1];
            Brush stroke = avgFps >= 60 ? TelemetryGpuLabel : avgFps >= 30 ? _warnBrush : TelemetryFpsLabel;
            Brush fill = avgFps >= 60 ? GraphGoodFill : avgFps >= 30 ? GraphWarnFill : GraphLowFill;

            canvas.Children.Add(new Polygon
            {
                Points = fillPts,
                Fill = fill
            });

            canvas.Children.Add(new Polyline
            {
                Points = linePts,
                Stroke = stroke,
                StrokeThickness = 1.9,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
        }
        else
        {
            canvas.Children.Add(new Line
            {
                X1 = 0,
                X2 = canvasW,
                Y1 = canvasH - 4,
                Y2 = canvasH - 4,
                Stroke = GraphGridLine,
                StrokeThickness = 1.5
            });
        }

        outer.Children.Add(canvas);
        box.Child = outer;
        return box;
    }

    private void RenderRoundedGlass(OverlaySettings s, OverlayTelemetrySnapshot data, bool horizontal)
    {
        var items = BuildItems(s, data);
        Brush pillBg = _isLightSurface ? SubtleLightTile : SubtleDarkTile;
        Brush heroPillBg = _isLightSurface ? SubtleLightTile : SubtleDarkHeroTile;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (horizontal)
            {
                if (item.IsHero)
                {
                    var heroPill = new Border
                    {
                        CornerRadius = new CornerRadius(12),
                        Background = heroPillBg,
                        Padding = new Thickness(9, 3, 9, 3),
                        Margin = new Thickness(i == 0 ? 0 : 8, 0, 2, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var inner = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    inner.Children.Add(CreateDot(item.DotBrush ?? _goodBrush, new Thickness(0, 0, 6, 0)));
                    inner.Children.Add(new TextBlock
                    {
                        Text = item.Label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _labelBrush,
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    inner.Children.Add(new TextBlock
                    {
                        Text = item.Value,
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = item.ValueBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    heroPill.Child = inner;
                    MetricsPanel.Children.Add(heroPill);
                }
                else
                {
                    if (i > 0)
                    {
                        MetricsPanel.Children.Add(new TextBlock
                        {
                            Text = "•",
                            FontSize = 10,
                            Foreground = _labelBrush,
                            Opacity = 0.45,
                            Margin = new Thickness(8, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    if (item.DotBrush != null)
                        stack.Children.Add(CreateDot(item.DotBrush, new Thickness(0, 0, 5, 0)));
                    stack.Children.Add(new TextBlock
                    {
                        Text = item.Label,
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _labelBrush,
                        Margin = new Thickness(0, 0, 5, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = item.Value,
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = item.ValueBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    MetricsPanel.Children.Add(stack);
                }
            }
            else
            {
                var card = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = item.IsHero ? heroPillBg : pillBg,
                    Padding = new Thickness(10, item.IsHero ? 6 : 5, 10, item.IsHero ? 6 : 5),
                    Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 0),
                    MinWidth = 186
                };

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
                left.Children.Add(CreateDot(item.DotBrush ?? DotSubtleBrush, new Thickness(0, 0, 7, 0)));
                left.Children.Add(new TextBlock
                {
                    Text = item.Label,
                    FontSize = item.IsHero ? 12 : 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = item.IsHero ? _valueBrush : _labelBrush,
                    VerticalAlignment = VerticalAlignment.Center
                });
                row.Children.Add(left);

                var val = new TextBlock
                {
                    Text = item.Value,
                    FontSize = item.IsHero ? 14 : 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = item.ValueBrush,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(val, 1);
                row.Children.Add(val);

                card.Child = row;
                MetricsPanel.Children.Add(card);
            }
        }
    }

    private void RenderGamingBars(OverlaySettings s, OverlayTelemetrySnapshot data, bool horizontal)
    {
        Brush tileBg = _isLightSurface ? SubtleLightTile : SubtleDarkTile;
        Brush heroBg = _isLightSurface ? SubtleLightTile : SubtleDarkHeroTile;
        Brush trackBg = _isLightSurface ? SubtleLightTrack : SubtleTrackBg;

        bool hasFrameMetrics = s.ShowFps || s.ShowFrametime || s.ShowOnePercentLow;
        bool firstChild = true;

        if (hasFrameMetrics)
        {
            Brush fpsColor = data.Fps <= 0 ? _valueBrush
                : data.Fps >= 60 ? _goodBrush
                : data.Fps >= 30 ? _warnBrush
                : _dangerBrush;

            var hero = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = heroBg,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0)
            };

            if (horizontal)
            {
                var stack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new Border
                {
                    Width = 3,
                    Height = 20,
                    CornerRadius = new CornerRadius(1.5),
                    Background = fpsColor,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });

                if (s.ShowFps)
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = data.Fps > 0 ? $"{data.Fps:0}" : "—",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = fpsColor,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    stack.Children.Add(new TextBlock
                    {
                        Text = "FPS",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _labelBrush,
                        Margin = new Thickness(4, 3, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }

                if (s.ShowFrametime || s.ShowOnePercentLow)
                {
                    var sub = new StackPanel
                    {
                        Margin = new Thickness(s.ShowFps ? 10 : 0, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    if (s.ShowFrametime)
                    {
                        sub.Children.Add(new TextBlock
                        {
                            Text = data.FrametimeMs > 0 ? $"{data.FrametimeMs:0.0} ms" : "— ms",
                            FontSize = 10.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = _valueBrush
                        });
                    }
                    if (s.ShowOnePercentLow)
                    {
                        sub.Children.Add(new TextBlock
                        {
                            Text = data.Low1PercentFps > 0 ? $"1%: {data.Low1PercentFps:0}" : "1%: —",
                            FontSize = 10,
                            Foreground = _labelBrush
                        });
                    }
                    stack.Children.Add(sub);
                }

                hero.Child = stack;
            }
            else
            {
                var grid = new Grid { MinWidth = 196 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                if (s.ShowFps)
                {
                    var fpsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    fpsRow.Children.Add(new Border
                    {
                        Width = 3,
                        Height = 18,
                        CornerRadius = new CornerRadius(1.5),
                        Background = fpsColor,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    fpsRow.Children.Add(new TextBlock
                    {
                        Text = data.Fps > 0 ? $"{data.Fps:0}" : "—",
                        FontSize = 17,
                        FontWeight = FontWeights.Bold,
                        Foreground = fpsColor,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    fpsRow.Children.Add(new TextBlock
                    {
                        Text = "FPS",
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _labelBrush,
                        Margin = new Thickness(4, 3, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    grid.Children.Add(fpsRow);
                }

                var rightSub = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (s.ShowFrametime)
                {
                    rightSub.Children.Add(new TextBlock
                    {
                        Text = data.FrametimeMs > 0 ? $"FT {data.FrametimeMs:0.0} ms" : "FT —",
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = _valueBrush,
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                }
                if (s.ShowOnePercentLow)
                {
                    rightSub.Children.Add(new TextBlock
                    {
                        Text = data.Low1PercentFps > 0 ? $"1% Low {data.Low1PercentFps:0}" : "1% Low —",
                        FontSize = 10,
                        Foreground = _labelBrush,
                        HorizontalAlignment = HorizontalAlignment.Right
                    });
                }
                Grid.SetColumn(rightSub, 1);
                grid.Children.Add(rightSub);
                hero.Child = grid;
            }

            MetricsPanel.Children.Add(hero);
            firstChild = false;
        }

        if (s.ShowCpu || s.ShowCpuTemp)
        {
            string right = s.ShowCpu
                ? $"{data.CpuPercent:0}%{(s.ShowCpuTemp && data.CpuTempC.HasValue ? $" · {data.CpuTempC.Value:0}°C" : "")}"
                : (data.CpuTempC.HasValue ? $"{data.CpuTempC.Value:0}°C" : "—");
            Brush barBrush = data.CpuPercent >= 90 ? _dangerBrush : data.CpuPercent >= 75 ? _warnBrush : DotCpuBrush;
            AddBarMetricCard("CPU", right, s.ShowCpu ? data.CpuPercent : null, barBrush, tileBg, trackBg, horizontal, ref firstChild);
        }

        if (s.ShowGpu || s.ShowGpuTemp)
        {
            string right = s.ShowGpu
                ? $"{data.GpuPercent:0}%{(s.ShowGpuTemp && data.GpuTempC.HasValue ? $" · {data.GpuTempC.Value:0}°C" : "")}"
                : (data.GpuTempC.HasValue ? $"{data.GpuTempC.Value:0}°C" : "—");
            Brush barBrush = data.GpuPercent >= 95 ? _dangerBrush : data.GpuPercent >= 82 ? _warnBrush : DotGpuBrush;
            AddBarMetricCard("GPU", right, s.ShowGpu ? data.GpuPercent : null, barBrush, tileBg, trackBg, horizontal, ref firstChild);
        }

        if (s.ShowRam)
        {
            string right = horizontal
                ? $"{data.RamUsedGb:0.0}/{data.RamTotalGb:0}G"
                : $"{data.RamUsedGb:0.0} / {data.RamTotalGb:0} GB ({data.RamPercent}%)";
            Brush barBrush = data.RamPercent >= 92 ? _dangerBrush : data.RamPercent >= 82 ? _warnBrush : DotRamBrush;
            AddBarMetricCard("RAM", right, data.RamPercent, barBrush, tileBg, trackBg, horizontal, ref firstChild);
        }
    }

    private void AddBarMetricCard(
        string label,
        string valueText,
        double? percent,
        Brush barBrush,
        Brush tileBg,
        Brush trackBg,
        bool horizontal,
        ref bool firstChild)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(9),
            Background = tileBg,
            Padding = new Thickness(9, 6, 9, 6),
            MinWidth = horizontal ? 106 : 196,
            Margin = horizontal
                ? new Thickness(firstChild ? 0 : 7, 0, 0, 0)
                : new Thickness(0, firstChild ? 0 : 6, 0, 0)
        };
        firstChild = false;

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = _labelBrush,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        });

        var val = new TextBlock
        {
            Text = valueText,
            FontSize = 11.5,
            FontWeight = FontWeights.Bold,
            Foreground = _valueBrush,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(val, 1);
        header.Children.Add(val);
        stack.Children.Add(header);

        if (percent.HasValue)
        {
            double pct = Math.Clamp(percent.Value, 0, 100);
            var track = new Border
            {
                Height = 4,
                CornerRadius = new CornerRadius(2),
                Background = trackBg,
                Margin = new Thickness(0, 5, 0, 0),
                ClipToBounds = true
            };

            var barGrid = new Grid();
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100.0 - pct), GridUnitType.Star) });

            if (pct > 0.5)
            {
                barGrid.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(2),
                    Background = barBrush
                });
            }

            track.Child = barGrid;
            stack.Children.Add(track);
        }

        card.Child = stack;
        MetricsPanel.Children.Add(card);
    }

    private void ApplyStyle(OverlaySettings s)
    {
        double scale = s.Scale switch
        {
            OverlayScale.Compact => 0.88,
            OverlayScale.Large => 1.16,
            _ => 1.0
        };
        HudScaleTransform.ScaleX = scale;
        HudScaleTransform.ScaleY = scale;

        byte alpha = (byte)Math.Round(Math.Clamp(s.BackgroundOpacityPercent, 20, 95) / 100.0 * 255.0);
        bool horizontal = s.Layout == OverlayLayoutMode.Horizontal;

        if (s.UseAppTheme && Application.Current?.Resources["App.Surface"] is SolidColorBrush themeBg &&
            Application.Current.Resources["App.Border"] is SolidColorBrush themeBorder &&
            Application.Current.Resources["App.Text"] is SolidColorBrush themeText &&
            Application.Current.Resources["App.TextMuted"] is SolidColorBrush themeMuted &&
            Application.Current.Resources["App.IconAccent"] is SolidColorBrush themeAccent)
        {
            var bgColor = themeBg.Color;
            _isLightSurface = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) > 140;
            bgColor.A = alpha;
            RootBorder.Background = new SolidColorBrush(bgColor);
            RootBorder.BorderBrush = themeBorder;
            _labelBrush = themeMuted;
            _valueBrush = themeText;
            _accentBrush = themeAccent;
            _goodBrush = (Application.Current.Resources["App.Success"] as Brush) ?? _accentBrush;
            _warnBrush = (Application.Current.Resources["App.Warning"] as Brush) ?? Brushes.Gold;
            _dangerBrush = (Application.Current.Resources["App.Danger"] as Brush) ?? TelemetryCritBrush;
        }
        else
        {
            _isLightSurface = false;
            RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x0A, 0x10, 0x1B));
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x94, 0xA3, 0xB8));
            _labelBrush = DotSubtleBrush;
            _valueBrush = FreezeBrush(0xF8, 0xFA, 0xFC);
            _accentBrush = (Application.Current?.Resources["App.IconAccent"] as Brush) ?? DotGpuBrush;
            _goodBrush = TelemetryGpuLabel;
            _warnBrush = FreezeBrush(0xFB, 0xBF, 0x24);
            _dangerBrush = TelemetryCritBrush;
        }

        switch (s.VisualStyle)
        {
            case OverlayVisualStyle.TelemetryText:
                RootBorder.Margin = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(0);
                RootBorder.BorderThickness = new Thickness(0);
                RootBorder.Padding = new Thickness(10, 7, 10, 7);
                RootBorder.Background = Brushes.Transparent;
                RootBorder.Effect = null;
                ContentPanel.Effect = TextOutlineShadow;
                break;

            case OverlayVisualStyle.RoundedGlass:
                RootBorder.Margin = new Thickness(10);
                RootBorder.CornerRadius = new CornerRadius(horizontal ? 22 : 18);
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.Padding = horizontal ? new Thickness(14, 7, 14, 7) : new Thickness(14, 11, 14, 11);
                if (!s.UseAppTheme)
                {
                    var topCol = Color.FromArgb(alpha, 0x14, 0x1E, 0x33);
                    var botCol = Color.FromArgb(alpha, 0x0A, 0x10, 0x1D);
                    RootBorder.Background = new LinearGradientBrush(topCol, botCol, 90);
                    RootBorder.BorderBrush = new LinearGradientBrush(
                        Color.FromArgb(0x52, 0xFF, 0xFF, 0xFF),
                        Color.FromArgb(0x1E, 0x94, 0xA3, 0xB8),
                        90);
                }
                RootBorder.Effect = CardSoftShadow;
                ContentPanel.Effect = null;
                break;

            case OverlayVisualStyle.GamingBars:
                RootBorder.Margin = new Thickness(10);
                RootBorder.CornerRadius = new CornerRadius(14);
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.Padding = new Thickness(12, 10, 12, 10);
                if (!s.UseAppTheme)
                {
                    RootBorder.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x0A, 0x0F, 0x1C));
                    RootBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x4D, 0x81, 0x8C, 0xF8));
                }
                RootBorder.Effect = CardSoftShadow;
                ContentPanel.Effect = null;
                break;

            case OverlayVisualStyle.Classic:
            default:
                RootBorder.Margin = new Thickness(0);
                RootBorder.CornerRadius = new CornerRadius(8);
                RootBorder.BorderThickness = new Thickness(1);
                RootBorder.Padding = new Thickness(12, 8, 12, 8);
                RootBorder.Effect = null;
                ContentPanel.Effect = null;
                break;
        }
    }

    private readonly record struct TelemetryCell(string Number, string Unit, Brush Brush);

    private sealed record TelemetryEntry(
        string Category,
        string? SubIndex,
        Brush CategoryBrush,
        params TelemetryCell[] Cells);

    private List<TelemetryEntry> BuildTelemetryEntries(OverlaySettings s, OverlayTelemetrySnapshot data)
    {
        var list = new List<TelemetryEntry>();

        // 1. GPU (с поддержкой мульти-GPU, частот, VRAM и питания)
        if (s.ShowGpu || s.ShowGpuTemp)
        {
            if (s.ShowDetailedGpu && data.GpuDetails is { Length: > 0 } gpus)
            {
                foreach (var g in gpus)
                {
                    string idx = g.Index.ToString();
                    var gpuCells = new List<TelemetryCell>(4);
                    Brush loadBrush = g.UtilizationPercent >= 95 ? TelemetryCritBrush : TelemetryAmberVal;

                    if (s.ShowGpuTemp && g.TempC.HasValue)
                        gpuCells.Add(new TelemetryCell($"{g.TempC.Value:0}", "°C", TelemetryAmberVal));

                    if (s.ShowGpu)
                        gpuCells.Add(new TelemetryCell($"{g.UtilizationPercent:0}", "%", loadBrush));

                    if (s.ShowGpu && g.CoreClockMhz.HasValue && g.CoreClockMhz.Value > 0)
                        gpuCells.Add(new TelemetryCell($"{g.CoreClockMhz.Value:0}", "MHz", TelemetryAmberVal));

                    if (s.ShowGpu && g.PowerWatts.HasValue && g.PowerWatts.Value > 0)
                        gpuCells.Add(new TelemetryCell($"{g.PowerWatts.Value:0.0}", "W", TelemetryAmberVal));

                    if (gpuCells.Count > 0)
                        list.Add(new TelemetryEntry("GPU", idx, TelemetryGpuLabel, gpuCells.ToArray()));

                    if (s.ShowGpu && (g.VramUsedMb is > 0 || g.MemClockMhz is > 0))
                    {
                        var memCells = new List<TelemetryCell>(2);
                        if (g.VramUsedMb is > 0)
                            memCells.Add(new TelemetryCell($"{g.VramUsedMb.Value:0}", "MB", TelemetryAmberVal));
                        if (g.MemClockMhz is > 0)
                            memCells.Add(new TelemetryCell($"{g.MemClockMhz.Value:0}", "MHz", TelemetryAmberVal));
                        list.Add(new TelemetryEntry("MEM", idx, TelemetryGpuLabel, memCells.ToArray()));
                    }
                }
            }
            else
            {
                Brush valBrush = data.GpuPercent >= 95 ? TelemetryCritBrush : TelemetryAmberVal;
                var cells = new List<TelemetryCell>(2);
                if (s.ShowGpuTemp && data.GpuTempC.HasValue)
                    cells.Add(new TelemetryCell($"{data.GpuTempC.Value:0}", "°C", TelemetryAmberVal));
                if (s.ShowGpu)
                    cells.Add(new TelemetryCell($"{data.GpuPercent:0}", "%", valBrush));
                else if (cells.Count == 0)
                    cells.Add(new TelemetryCell("—", "", valBrush));
                list.Add(new TelemetryEntry("GPU", null, TelemetryGpuLabel, cells.ToArray()));
            }
        }

        // 2. CPU (по каждому ядру CPU1..CPUN + итоговая строка CPU)
        if (s.ShowCpu || s.ShowCpuTemp)
        {
            if (s.ShowCpu && s.ShowPerCoreCpu && data.CpuCoreLoads is { Length: > 0 } cores)
            {
                for (int c = 0; c < cores.Length; c++)
                {
                    double coreLoad = cores[c];
                    Brush coreBrush = coreLoad >= 90 ? TelemetryCritBrush : TelemetryAmberVal;
                    var coreCells = new List<TelemetryCell>(2);
                    if (s.ShowCpuTemp && data.CpuTempC.HasValue)
                        coreCells.Add(new TelemetryCell($"{data.CpuTempC.Value:0}", "°C", TelemetryAmberVal));
                    coreCells.Add(new TelemetryCell($"{coreLoad:0}", "%", coreBrush));
                    list.Add(new TelemetryEntry("CPU", (c + 1).ToString(), TelemetryCpuLabel, coreCells.ToArray()));
                }
            }

            Brush cpuBrush = data.CpuPercent >= 90 ? TelemetryCritBrush : TelemetryAmberVal;
            var totalCells = new List<TelemetryCell>(2);
            if (s.ShowCpuTemp && data.CpuTempC.HasValue)
                totalCells.Add(new TelemetryCell($"{data.CpuTempC.Value:0}", "°C", TelemetryAmberVal));
            if (s.ShowCpu)
                totalCells.Add(new TelemetryCell($"{data.CpuPercent:0}", "%", cpuBrush));
            else if (totalCells.Count == 0)
                totalCells.Add(new TelemetryCell("—", "", cpuBrush));

            list.Add(new TelemetryEntry("CPU", null, TelemetryCpuLabel, totalCells.ToArray()));
        }

        // 3. RAM
        if (s.ShowRam)
        {
            double usedMb = Math.Round(data.RamUsedGb * 1024.0, 0);
            list.Add(new TelemetryEntry(
                "RAM",
                null,
                TelemetryRamLabel,
                new TelemetryCell($"{usedMb:0}", "MB", TelemetryAmberVal),
                new TelemetryCell($"{data.RamPercent}", "%", TelemetryAmberVal)));
        }

        // 4. FPS / 1% Low / FT
        if (s.ShowFps)
        {
            list.Add(new TelemetryEntry("FPS", null, TelemetryFpsLabel,
                new TelemetryCell(data.Fps > 0 ? $"{data.Fps:0}" : "—", "FPS", TelemetryWhiteBrush)));
        }

        if (s.ShowOnePercentLow)
        {
            list.Add(new TelemetryEntry("1% LOW", null, TelemetryFpsLabel,
                new TelemetryCell(data.Low1PercentFps > 0 ? $"{data.Low1PercentFps:0}" : "—", "FPS", TelemetryAmberVal)));
        }

        if (s.ShowFrametime)
        {
            list.Add(new TelemetryEntry("FT", null, TelemetryFpsLabel,
                new TelemetryCell(data.FrametimeMs > 0 ? $"{data.FrametimeMs:0.0}" : "—", "ms", TelemetryAmberVal)));
        }

        return list;
    }

    private static TextBlock CreateTelemetryLabelBlock(string text, string? subIndex, double size, Brush foreground)
    {
        var tb = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
        tb.Inlines.Add(new Run(text)
        {
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = foreground
        });
        if (!string.IsNullOrEmpty(subIndex))
        {
            tb.Inlines.Add(new Run(subIndex)
            {
                FontSize = Math.Max(9.5, size - 4),
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
                BaselineAlignment = BaselineAlignment.Subscript
            });
        }
        return tb;
    }

    private static TextBlock CreateUnitNumberBlock(string number, string unit, Brush numberBrush, double size, Thickness? margin = null)
    {
        var tb = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0)
        };
        Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);

        tb.Inlines.Add(new Run(number)
        {
            FontSize = size,
            FontWeight = FontWeights.Bold,
            Foreground = numberBrush
        });
        if (!string.IsNullOrEmpty(unit))
        {
            tb.Inlines.Add(new Run(" " + unit)
            {
                FontSize = Math.Max(9.5, size - 3.5),
                FontWeight = FontWeights.SemiBold,
                Foreground = TelemetryUnitBrush
            });
        }
        return tb;
    }

    private static Border CreateDot(Brush brush, Thickness margin) => new()
    {
        Width = 6,
        Height = 6,
        CornerRadius = new CornerRadius(3),
        Background = brush,
        Margin = margin,
        VerticalAlignment = VerticalAlignment.Center
    };

    private List<MetricChip> BuildItems(OverlaySettings s, OverlayTelemetrySnapshot data)
    {
        var list = new List<MetricChip>();

        if (s.ShowFps)
        {
            string val = data.Fps > 0 ? $"{data.Fps:0}" : "—";
            list.Add(new MetricChip("FPS", val, data.Fps > 0 ? _goodBrush : _valueBrush, _goodBrush, IsHero: true));
        }

        if (s.ShowFrametime)
        {
            string val = data.FrametimeMs > 0 ? $"{data.FrametimeMs:0.0} ms" : "—";
            list.Add(new MetricChip("FT", val, _valueBrush, DotSubtleBrush));
        }

        if (s.ShowOnePercentLow)
        {
            string val = data.Low1PercentFps > 0 ? $"{data.Low1PercentFps:0}" : "—";
            list.Add(new MetricChip("1% LOW", val, _valueBrush, DotSubtleBrush));
        }

        if (s.ShowCpu)
        {
            var tempPart = s.ShowCpuTemp && data.CpuTempC.HasValue ? $" · {data.CpuTempC.Value:0}°C" : "";
            var brush = data.CpuPercent >= 90 ? _warnBrush : _valueBrush;
            list.Add(new MetricChip("CPU", $"{data.CpuPercent:0}%{tempPart}", brush, DotCpuBrush));
        }
        else if (s.ShowCpuTemp)
        {
            list.Add(new MetricChip("CPU °C", data.CpuTempC.HasValue ? $"{data.CpuTempC.Value:0}°C" : "—", _valueBrush, DotCpuBrush));
        }

        if (s.ShowGpu)
        {
            var tempPart = s.ShowGpuTemp && data.GpuTempC.HasValue ? $" · {data.GpuTempC.Value:0}°C" : "";
            var brush = data.GpuPercent >= 95 ? _warnBrush : _valueBrush;
            list.Add(new MetricChip("GPU", $"{data.GpuPercent:0}%{tempPart}", brush, DotGpuBrush));
        }
        else if (s.ShowGpuTemp)
        {
            list.Add(new MetricChip("GPU °C", data.GpuTempC.HasValue ? $"{data.GpuTempC.Value:0}°C" : "—", _valueBrush, DotGpuBrush));
        }

        if (s.ShowRam)
        {
            list.Add(new MetricChip("RAM", $"{data.RamUsedGb:0.0}/{data.RamTotalGb:0} GB ({data.RamPercent}%)", _valueBrush, DotRamBrush));
        }

        return list;
    }

    public void Reposition(OverlaySettings s)
    {
        double w = ActualWidth > 0 ? ActualWidth : DesiredSize.Width;
        double h = ActualHeight > 0 ? ActualHeight : DesiredSize.Height;
        if (w <= 0 || h <= 0)
            return;

        double screenW = SystemParameters.PrimaryScreenWidth;
        double screenH = SystemParameters.PrimaryScreenHeight;
        double shadowPad = s.VisualStyle is OverlayVisualStyle.RoundedGlass or OverlayVisualStyle.GamingBars ? 10 : 0;
        double margin = Math.Max(2, 16 - shadowPad);
        double bottomOffset = Math.Max(2, 56 - shadowPad);

        switch (s.Position)
        {
            case OverlayPosition.TopRight:
                Left = Math.Max(margin, screenW - w - margin);
                Top = margin;
                break;
            case OverlayPosition.BottomLeft:
                Left = margin;
                Top = Math.Max(margin, screenH - h - bottomOffset);
                break;
            case OverlayPosition.BottomRight:
                Left = Math.Max(margin, screenW - w - margin);
                Top = Math.Max(margin, screenH - h - bottomOffset);
                break;
            case OverlayPosition.TopCenter:
                Left = Math.Max(margin, (screenW - w) / 2.0);
                Top = margin;
                break;
            case OverlayPosition.Custom:
                Left = Math.Clamp(s.CustomLeft, 0, Math.Max(0, screenW - w));
                Top = Math.Clamp(s.CustomTop, 0, Math.Max(0, screenH - h));
                break;
            case OverlayPosition.TopLeft:
            default:
                Left = margin;
                Top = margin;
                break;
        }
    }
}
