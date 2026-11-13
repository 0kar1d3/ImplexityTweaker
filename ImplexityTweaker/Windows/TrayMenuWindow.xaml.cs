using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

/// <summary>
/// Компактное всплывающее меню системного трея в фирменном Fluent-стиле приложения.
/// Позиционируется рядом с курсором и закрывается при потере фокуса или нажатии Esc.
/// </summary>
public partial class TrayMenuWindow : Window
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    private bool _closing;

    public TrayMenuWindow()
    {
        InitializeComponent();
        RefreshStatus();
        ApplyThemeVisuals();

        SourceInitialized += (_, _) => PositionNearCursor();
        Loaded += (_, _) =>
        {
            ApplyThemeVisuals();
            PositionNearCursor();
            UiAnimation.FadeSlideIn(MenuRoot, offsetY: 6, durationMs: 140);
            Activate();
        };
        Deactivated += (_, _) => SafeClose();
        Closed += (_, _) =>
        {
            if (Application.Current?.MainWindow is { IsVisible: false })
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
                    new Action(RamCleanerService.TrimOwnProcessMemory));
            }
        };
    }

    private void ApplyThemeVisuals()
    {
        MenuShadow.Opacity = ThemeApplier.IsLightTheme ? 0.16 : 0.42;
        UpdateAutostartBadge();
    }

    private void RefreshStatus()
    {
        RamPercentText.Text = LocalizationService.TF("ОЗУ: {0}%", RamCleanerService.GetMemoryUsagePercent());

        var features = TrayService.GetActiveFeatureNames();
        ActiveFeaturesText.Text = features.Count > 0
            ? string.Join(" · ", features)
            : LocalizationService.T("Фоновый режим активен");

        UpdateAutostartBadge();
    }

    private void UpdateAutostartBadge()
    {
        bool on = TrayService.GetAutostartEnabled();
        AutostartPillText.Text = on ? LocalizationService.T("Вкл") : LocalizationService.T("Выкл");
        AutostartPill.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, on ? "App.Accent" : "App.SurfaceAlt");
        AutostartPillText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, on ? "App.AccentText" : "App.TextMuted");
    }

    private void PositionNearCursor()
    {
        if (!GetCursorPos(out var pt))
            return;

        double dipX = pt.X;
        double dipY = pt.Y;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
        {
            var m = source.CompositionTarget.TransformFromDevice;
            var transformed = m.Transform(new Point(pt.X, pt.Y));
            dipX = transformed.X;
            dipY = transformed.Y;
        }

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : 300;
        var area = SystemParameters.WorkArea;

        double left = dipX - width + 16;
        double top = dipY - height + 10;

        if (left + width > area.Right - 6)
            left = area.Right - width - 6;
        if (left < area.Left + 6)
            left = area.Left + 6;

        if (top < area.Top + 6)
            top = dipY + 12;
        if (top + height > area.Bottom - 6)
            top = area.Bottom - height - 6;

        Left = left;
        Top = top;
    }

    private void SafeClose()
    {
        if (_closing)
            return;
        _closing = true;
        try { Close(); } catch { }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            SafeClose();
    }

    private void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        TrayService.RestoreMainWindow();
    }

    private async void CleanRam_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        var freed = await RamCleanerService.CleanAsync();
        TrayService.ShowBalloon(
            LocalizationService.T("Очистка ОЗУ завершена"),
            LocalizationService.TF("Освобождено памяти: {0} МБ (текущая загрузка: {1}%)", freed, RamCleanerService.GetMemoryUsagePercent()));
    }

    private void OpenShutdownTimer_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        TrayService.RestoreMainWindow();
        if (Application.Current?.MainWindow is MainWindow mw)
            mw.NavigateByTag("shutdown");
    }

    private void ToggleAutostart_Click(object sender, RoutedEventArgs e)
    {
        TrayService.SetAutostartEnabled(!TrayService.GetAutostartEnabled());
        UpdateAutostartBadge();
    }

    private void ExitApp_Click(object sender, RoutedEventArgs e)
    {
        SafeClose();
        TrayService.ExitApplication();
    }
}
