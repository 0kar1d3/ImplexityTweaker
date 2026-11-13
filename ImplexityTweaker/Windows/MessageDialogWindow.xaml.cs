using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ImplexityTweaker.Services;
using Wpf.Ui.Common;

namespace ImplexityTweaker.Windows;

public enum DialogKind
{
    Info,
    Error,
    Confirm
}

public partial class MessageDialogWindow : Window
{
    private readonly bool _isConfirm;
    public bool Confirmed { get; private set; }

    public MessageDialogWindow(string title, string message, DialogKind kind)
    {
        InitializeComponent();
        _isConfirm = kind == DialogKind.Confirm;

        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        switch (kind)
        {
            case DialogKind.Error:
                KindIcon.Symbol = SymbolRegular.DismissCircle24;
                KindIcon.Foreground = TryBrush("App.Danger", Brushes.IndianRed);
                IconBadge.Background = TryBrush("App.DangerSoft", Brushes.Transparent);
                PrimaryBtn.Content = LocalizationService.T("ОК");
                break;

            case DialogKind.Confirm:
                KindIcon.Symbol = SymbolRegular.Shield24;
                KindIcon.Foreground = TryBrush("App.IconAccent", Brushes.CornflowerBlue);
                IconBadge.Background = TryBrush("App.SurfaceAlt", Brushes.Transparent);
                PrimaryBtn.Content = LocalizationService.T("Да");
                SecondaryBtn.Content = LocalizationService.T("Отмена");
                SecondaryBtn.Visibility = Visibility.Visible;
                break;

            case DialogKind.Info:
            default:
                KindIcon.Symbol = SymbolRegular.Info24;
                KindIcon.Foreground = TryBrush("App.IconAccent", Brushes.CornflowerBlue);
                IconBadge.Background = TryBrush("App.SurfaceAlt", Brushes.Transparent);
                PrimaryBtn.Content = LocalizationService.T("ОК");
                break;
        }

        Loaded += (_, _) =>
        {
            UiAnimation.FadeSlideIn(DialogRoot, offsetY: 6, durationMs: 160);
            PrimaryBtn.Focus();
        };
    }

    private static Brush TryBrush(string key, Brush fallback)
        => Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            try { DragMove(); } catch { }
    }

    private void PrimaryBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private void SecondaryBtn_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Confirmed = !_isConfirm;
            DialogResult = !_isConfirm;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirmed = true;
            DialogResult = true;
            Close();
        }
    }
}
