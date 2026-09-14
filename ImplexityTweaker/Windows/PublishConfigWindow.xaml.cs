using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ImplexityTweaker.Windows;

/// <summary>Окно выбора видимости при публикации конфига на сайт.</summary>
public partial class PublishConfigWindow : Window
{
    // Windows 10 1809+ использует атрибут 19, 1903+ — 20. Применяем оба для совместимости.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>true = приватно, false = публично, null = отменено.</summary>
    public bool? ResultIsPrivate { get; private set; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public PublishConfigWindow(string configFileName)
    {
        InitializeComponent();
        ConfigNameText.Text = configFileName;

        // Тёмная панель заголовка (как в главном окне).
        SourceInitialized += (_, _) => SetDarkTitleBar();
    }

    /// <summary>Включает тёмный режим системного заголовка окна (DWM).</summary>
    private void SetDarkTitleBar()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            var value = 1;
            foreach (var attr in new[] { DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, DWMWA_USE_IMMERSIVE_DARK_MODE })
                DwmSetWindowAttribute(hwnd, attr, ref value, sizeof(int));
        }
        catch
        {
            // DWM недоступен — остаётся системный заголовок.
        }
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
