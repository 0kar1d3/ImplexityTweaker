using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class OverlayPage : Page
{
    private bool _init;
    private OverlayVisualStyle _selectedVisualStyle = OverlayVisualStyle.Classic;

    private sealed record ChoiceItem<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    public OverlayPage()
    {
        InitializeComponent();
        LoadFromSettings();
        OverlayService.OverlayStateChanged += OnOverlayStateChanged;
        Unloaded += (_, _) =>
        {
            OverlayService.OverlayStateChanged -= OnOverlayStateChanged;
            if (OverlayService.IsDragMode)
                OverlayService.SetDragMode(false);
        };
    }

    private void OnOverlayStateChanged(object? sender, bool enabled)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _init = true;
            try { OverlayEnabledToggle.IsChecked = enabled; }
            finally { _init = false; }
        });
    }

    private void LoadFromSettings()
    {
        _init = true;
        try
        {
            var s = OverlayService.CurrentSettings;

            OverlayEnabledToggle.IsChecked = s.Enabled;
            AutoStartToggle.IsChecked = s.AutoStartWithTweaker;

            HotkeyCombo.Items.Clear();
            var hotkeys = new[]
            {
                new ChoiceItem<string>("F9", "F9"),
                new ChoiceItem<string>("F10", "F10"),
                new ChoiceItem<string>("F11", "F11"),
                new ChoiceItem<string>("F8", "F8"),
                new ChoiceItem<string>("Alt+O", "Alt + O"),
                new ChoiceItem<string>("Ctrl+Shift+O", "Ctrl + Shift + O"),
                new ChoiceItem<string>("None", LocalizationService.T("Отключена"))
            };
            foreach (var h in hotkeys) HotkeyCombo.Items.Add(h);
            HotkeyCombo.SelectedItem = hotkeys.FirstOrDefault(x => string.Equals(x.Value, s.Hotkey, StringComparison.OrdinalIgnoreCase)) ?? hotkeys[0];

            ShowFpsToggle.IsChecked = s.ShowFps;
            ShowFrametimeCheck.IsChecked = s.ShowFrametime;
            ShowOnePercentCheck.IsChecked = s.ShowOnePercentLow;
            ShowCpuToggle.IsChecked = s.ShowCpu;
            ShowCpuTempCheck.IsChecked = s.ShowCpuTemp;
            ShowGpuToggle.IsChecked = s.ShowGpu;
            ShowGpuTempCheck.IsChecked = s.ShowGpuTemp;
            ShowRamToggle.IsChecked = s.ShowRam;
            ShowProcessToggle.IsChecked = s.ShowProcessName;
            ShowPerCoreCpuCheck.IsChecked = s.ShowPerCoreCpu;
            ShowDetailedGpuCheck.IsChecked = s.ShowDetailedGpu;
            ShowFramerateGraphCheck.IsChecked = s.ShowFramerateGraph;

            PositionCombo.Items.Clear();
            var positions = new[]
            {
                new ChoiceItem<OverlayPosition>(OverlayPosition.TopLeft, LocalizationService.T("Верхний левый угол")),
                new ChoiceItem<OverlayPosition>(OverlayPosition.TopRight, LocalizationService.T("Верхний правый угол")),
                new ChoiceItem<OverlayPosition>(OverlayPosition.TopCenter, LocalizationService.T("Сверху по центру")),
                new ChoiceItem<OverlayPosition>(OverlayPosition.BottomLeft, LocalizationService.T("Нижний левый угол")),
                new ChoiceItem<OverlayPosition>(OverlayPosition.BottomRight, LocalizationService.T("Нижний правый угол")),
                new ChoiceItem<OverlayPosition>(OverlayPosition.Custom, LocalizationService.T("Свободная (мышью)"))
            };
            foreach (var p in positions) PositionCombo.Items.Add(p);
            PositionCombo.SelectedItem = positions.FirstOrDefault(x => x.Value == s.Position) ?? positions[0];
            DragPositionToggle.IsChecked = OverlayService.IsDragMode;

            LayoutCombo.Items.Clear();
            var layouts = new[]
            {
                new ChoiceItem<OverlayLayoutMode>(OverlayLayoutMode.Horizontal, LocalizationService.T("Горизонтальная строка")),
                new ChoiceItem<OverlayLayoutMode>(OverlayLayoutMode.Vertical, LocalizationService.T("Вертикальная карточка"))
            };
            foreach (var l in layouts) LayoutCombo.Items.Add(l);
            LayoutCombo.SelectedItem = layouts.FirstOrDefault(x => x.Value == s.Layout) ?? layouts[0];

            ScaleCombo.Items.Clear();
            var scales = new[]
            {
                new ChoiceItem<OverlayScale>(OverlayScale.Compact, LocalizationService.T("Компактный")),
                new ChoiceItem<OverlayScale>(OverlayScale.Standard, LocalizationService.T("Стандартный")),
                new ChoiceItem<OverlayScale>(OverlayScale.Large, LocalizationService.T("Крупный"))
            };
            foreach (var sc in scales) ScaleCombo.Items.Add(sc);
            ScaleCombo.SelectedItem = scales.FirstOrDefault(x => x.Value == s.Scale) ?? scales[1];

            IntervalCombo.Items.Clear();
            var intervals = new[]
            {
                new ChoiceItem<int>(500, LocalizationService.T("0.5 сек (Плавно)")),
                new ChoiceItem<int>(1000, LocalizationService.T("1 сек (Оптимально)")),
                new ChoiceItem<int>(2000, LocalizationService.T("2 сек (Экономно)"))
            };
            foreach (var iv in intervals) IntervalCombo.Items.Add(iv);
            IntervalCombo.SelectedItem = intervals.FirstOrDefault(x => x.Value == s.RefreshIntervalMs) ?? intervals[1];

            UseAppThemeToggle.IsChecked = s.UseAppTheme;
            OpacitySlider.Value = Math.Clamp(s.BackgroundOpacityPercent, 20, 95);
            OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";

            _selectedVisualStyle = s.VisualStyle;
            UpdateStyleCardSelection();
        }
        finally
        {
            _init = false;
        }
    }

    private void CommitSettings()
    {
        if (_init || !IsLoaded)
            return;

        var s = new OverlaySettings
        {
            Enabled = OverlayEnabledToggle.IsChecked == true,
            AutoStartWithTweaker = AutoStartToggle.IsChecked == true,
            Hotkey = (HotkeyCombo.SelectedItem as ChoiceItem<string>)?.Value ?? "F9",

            ShowFps = ShowFpsToggle.IsChecked == true,
            ShowFrametime = ShowFrametimeCheck.IsChecked == true,
            ShowOnePercentLow = ShowOnePercentCheck.IsChecked == true,
            ShowCpu = ShowCpuToggle.IsChecked == true,
            ShowCpuTemp = ShowCpuTempCheck.IsChecked == true,
            ShowGpu = ShowGpuToggle.IsChecked == true,
            ShowGpuTemp = ShowGpuTempCheck.IsChecked == true,
            ShowRam = ShowRamToggle.IsChecked == true,
            ShowProcessName = ShowProcessToggle.IsChecked == true,
            ShowPerCoreCpu = ShowPerCoreCpuCheck.IsChecked == true,
            ShowDetailedGpu = ShowDetailedGpuCheck.IsChecked == true,
            ShowFramerateGraph = ShowFramerateGraphCheck.IsChecked == true,

            VisualStyle = _selectedVisualStyle,
            Position = (PositionCombo.SelectedItem as ChoiceItem<OverlayPosition>)?.Value ?? OverlayPosition.TopLeft,
            CustomLeft = OverlayService.CurrentSettings.CustomLeft,
            CustomTop = OverlayService.CurrentSettings.CustomTop,
            Layout = (LayoutCombo.SelectedItem as ChoiceItem<OverlayLayoutMode>)?.Value ?? OverlayLayoutMode.Horizontal,
            Scale = (ScaleCombo.SelectedItem as ChoiceItem<OverlayScale>)?.Value ?? OverlayScale.Standard,
            RefreshIntervalMs = (IntervalCombo.SelectedItem as ChoiceItem<int>)?.Value ?? 1000,
            UseAppTheme = UseAppThemeToggle.IsChecked == true,
            BackgroundOpacityPercent = (int)Math.Round(OpacitySlider.Value)
        };

        OverlayService.ApplySettings(s);
    }

    private void UpdateStyleCardSelection()
    {
        Border[] cards = [StyleCardClassic, StyleCardTelemetry, StyleCardRounded, StyleCardBars];
        foreach (var card in cards)
        {
            bool selected = Enum.TryParse<OverlayVisualStyle>(card.Tag?.ToString(), out var st) && st == _selectedVisualStyle;
            card.SetResourceReference(Border.BorderBrushProperty, selected ? "App.Accent" : "App.Border");
            card.SetResourceReference(Border.BackgroundProperty, selected ? "App.NavActiveBg" : "App.SurfaceAlt");
        }

        bool isTelemetry = _selectedVisualStyle == OverlayVisualStyle.TelemetryText;
        TelemetryDetailsRow.IsEnabled = isTelemetry;
        TelemetryDetailsRow.Opacity = isTelemetry ? 1.0 : 0.48;
        LayoutCombo.IsEnabled = !isTelemetry;
        LayoutTelemetryHint.Visibility = isTelemetry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StyleCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && Enum.TryParse<OverlayVisualStyle>(b.Tag?.ToString(), out var style))
        {
            _selectedVisualStyle = style;
            UpdateStyleCardSelection();
            CommitSettings();
        }
    }

    private void AnySettingChanged(object sender, RoutedEventArgs e) => CommitSettings();

    private void AnySelectionChanged(object sender, SelectionChangedEventArgs e) => CommitSettings();

    private void DragPositionToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_init || !IsLoaded)
            return;

        bool enable = DragPositionToggle.IsChecked == true;
        OverlayService.SetDragMode(enable);
        if (!enable)
        {
            _init = true;
            try
            {
                var pos = OverlayService.CurrentSettings.Position;
                foreach (ChoiceItem<OverlayPosition> item in PositionCombo.Items)
                {
                    if (item.Value == pos)
                    {
                        PositionCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _init = false;
            }
        }
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel != null)
            OpacityLabel.Text = $"{(int)Math.Round(e.NewValue)}%";
        CommitSettings();
    }
}
