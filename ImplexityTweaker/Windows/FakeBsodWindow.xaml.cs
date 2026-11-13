using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

public partial class FakeBsodWindow : Window
{
    private readonly DispatcherTimer _timer;
    private int _percent;

    public FakeBsodWindow(string? stopCode = null)
    {
        InitializeComponent();

        MainMessageText.Text = LocalizationService.T(
            "На вашем ПК возникла проблема, и его необходимо перезагрузить. Мы лишь собираем некоторые сведения об ошибке, а затем будет выполнена перезагрузка.");
        SupportText.Text = LocalizationService.T(
            "Дополнительные сведения об этой проблеме и возможных способах ее решения см. на странице https://www.windows.com/stopcode");
        CallerText.Text = LocalizationService.T(
            "Если вы обратитесь в службу поддержки, передайте ей следующие сведения:");

        var code = string.IsNullOrWhiteSpace(stopCode) ? "CRITICAL_PROCESS_DIED" : stopCode.Trim();
        StopCodeText.Text = LocalizationService.TF("Код остановки: {0}", code);
        ProgressText.Text = LocalizationService.TF("{0}% завершено", 0);

        QrImage.Source = BuildAuthenticQrBitmap();

        // Неравномерные «рывки» счётчика, как на реальном BSOD.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(950) };
        _timer.Tick += (_, _) =>
        {
            if (_percent >= 100)
            {
                _timer.Stop();
                return;
            }

            _percent = Math.Min(100, _percent + Random.Shared.Next(7, 22));
            ProgressText.Text = LocalizationService.TF("{0}% завершено", _percent);
            _timer.Interval = TimeSpan.FromMilliseconds(Random.Shared.Next(550, 1450));
        };

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
            _timer.Start();
        };
        Closed += (_, _) => _timer.Stop();
    }

    /// <summary>
    /// Генерирует чёткую 25x25 QR-матрицу с настоящими поисковыми узорами (7x7 в 3 углах),
    /// паттерном выравнивания (5x5), линиями синхронизации и плотной сеткой модулей.
    /// </summary>
    private static BitmapSource BuildAuthenticQrBitmap()
    {
        const int n = 25;
        var m = new bool[n, n];
        var reserved = new bool[n, n];

        void SetFinder(int r0, int c0)
        {
            for (int r = -1; r <= 7; r++)
            {
                for (int c = -1; c <= 7; c++)
                {
                    int rr = r0 + r, cc = c0 + c;
                    if (rr < 0 || rr >= n || cc < 0 || cc >= n)
                        continue;

                    reserved[rr, cc] = true;
                    bool on = r >= 0 && r <= 6 && c >= 0 && c <= 6 &&
                              (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                    m[rr, cc] = on;
                }
            }
        }

        SetFinder(0, 0);
        SetFinder(0, n - 7);
        SetFinder(n - 7, 0);

        // Alignment pattern (5x5) в позиции (16, 16) для Version 2 (25x25).
        for (int r = 16; r <= 20; r++)
        {
            for (int c = 16; c <= 20; c++)
            {
                reserved[r, c] = true;
                m[r, c] = r == 16 || r == 20 || c == 16 || c == 20 || (r == 18 && c == 18);
            }
        }

        // Timing patterns (строка 6 и столбец 6).
        for (int i = 8; i <= n - 9; i++)
        {
            reserved[6, i] = true;
            m[6, i] = (i % 2) == 0;
            reserved[i, 6] = true;
            m[i, 6] = (i % 2) == 0;
        }

        // Детерминированное заполнение области данных (https://www.windows.com/stopcode).
        uint seed = 0x5A17C931;
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                if (reserved[r, c])
                    continue;
                seed ^= seed << 13;
                seed ^= seed >> 17;
                seed ^= seed << 5;
                m[r, c] = (seed & 1) == 1;
            }
        }

        // Рендер 25x25 в 1-bpp/32-bpp без сглаживания (чёткие пиксели).
        var pixels = new byte[n * n * 4];
        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
            {
                int idx = (r * n + c) * 4;
                bool dark = m[r, c];
                // BGRA: либо белый, либо синий цвет BSOD (#0078D7 -> B=215, G=120, R=0)
                pixels[idx + 0] = dark ? (byte)215 : (byte)255;
                pixels[idx + 1] = dark ? (byte)120 : (byte)255;
                pixels[idx + 2] = dark ? (byte)0 : (byte)255;
                pixels[idx + 3] = 255;
            }
        }

        var bmp = BitmapSource.Create(n, n, 96, 96, PixelFormats.Bgra32, null, pixels, n * 4);
        bmp.Freeze();
        return bmp;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Close();
}
