using System.Diagnostics;
using System.Windows.Controls;

namespace ImplexityTweaker.Pages;

public partial class WindowsComponentsPage : Page
{
    public WindowsComponentsPage()
    {
        InitializeComponent();
    }

    private void OptionalFeatures_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("OptionalFeatures") { UseShellExecute = true });
    }

    private void Appwiz_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });
    }
}
