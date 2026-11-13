using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

/// <summary>Окно выбора видимости при публикации конфига на сайт.</summary>
public partial class PublishConfigWindow : Window
{
    /// <summary>true = приватно, false = публично, null = отменено.</summary>
    public bool? ResultIsPrivate { get; private set; }

    public PublishConfigWindow(string configFileName)
    {
        InitializeComponent();
        ConfigNameText.Text = configFileName;

        UiAnimation.PrepareWindowOpen(this);
        Loaded += (_, _) => UiAnimation.AnimateWindowOpen(this);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ResultIsPrivate = null;
            DialogResult = false;
        }
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null)
        {
            if (node is ButtonBase or Wpf.Ui.Controls.Button)
                return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        try { DragMove(); } catch { }
    }

    private void Public_Click(object sender, RoutedEventArgs e)
    {
        ResultIsPrivate = false;
        DialogResult = true;
    }

    private void Private_Click(object sender, RoutedEventArgs e)
    {
        ResultIsPrivate = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ResultIsPrivate = null;
        DialogResult = false;
    }
}
