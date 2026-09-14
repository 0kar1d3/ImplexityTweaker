using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

public partial class LoginWindow : Window
{
    private bool _closed;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public LoginWindow()
    {
        InitializeComponent();
        LogoClip.Rect = new System.Windows.Rect(0, 0, 52, 52);
        Closed += (_, _) => _closed = true;

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

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailBox.Text?.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Введите email и пароль.");
            return;
        }

        LoginBtn.IsEnabled = false;
        try
        {
            var (ok, error) = await AccountService.LoginAsync(email, password);
            if (ok && !_closed)
            {
                DialogResult = true;
                Close();
            }
            else if (!ok && !_closed)
            {
                await Dialogs.ShowErrorAsync("Ошибка", error ?? "Не удалось войти.");
            }
        }
        finally
        {
            if (!_closed)
                LoginBtn.IsEnabled = true;
        }
    }

    /// <summary>Вход через сайт: открывает страницу входа в браузере и опрашивает заявку.</summary>
    private async void SiteLogin_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var (ok, code, error) = await AccountService.BeginDeviceLoginAsync();
            if (!ok || string.IsNullOrEmpty(code))
            {
                var hint = error?.Contains("does not exist", StringComparison.OrdinalIgnoreCase) == true
                    ? LocalizationService.T(" Сначала выполните auth_schema.sql в Supabase.")
                    : "";
                await Dialogs.ShowErrorAsync("Вход через сайт", (error ?? "Не удалось начать вход через сайт.") + hint);
                return;
            }

            StatusText.Text = LocalizationService.T("Открываем страницу входа на сайте…");
            StatusPanel.Visibility = Visibility.Visible;

            try
            {
                Process.Start(new ProcessStartInfo(AccountService.GetAuthPageUrl(code)) { UseShellExecute = true });
            }
            catch
            {
                // Браузер не открылся — продолжаем ждать, пользователь может открыть ссылку вручную.
            }

            StatusText.Text = LocalizationService.T("Ожидание входа на сайте…");

            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (!_closed && DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000);
                if (_closed)
                    return;

                var result = await AccountService.PollDeviceLoginAsync(code);
                if (result is { } t && !string.IsNullOrEmpty(t.AccessToken) && !string.IsNullOrEmpty(t.UserId))
                {
                    _ = AccountService.DeleteDeviceLoginAsync(code);

                    var (done, err) = await AccountService.CompleteDeviceLoginAsync(t.AccessToken, t.RefreshToken, t.UserId);
                    if (done && !_closed)
                    {
                        DialogResult = true;
                        Close();
                    }
                    else if (!done && !_closed)
                    {
                        StatusPanel.Visibility = Visibility.Collapsed;
                        await Dialogs.ShowErrorAsync("Вход через сайт", err ?? "Не удалось войти.");
                    }
                    return;
                }
            }

            if (_closed)
                return;

            StatusPanel.Visibility = Visibility.Collapsed;
            await Dialogs.ShowErrorAsync("Вход через сайт", "Время ожидания вышло. Попробуйте ещё раз.");
        }
        finally
        {
            if (!_closed)
                SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        SiteLoginBtn.IsEnabled = !busy;
        LoginBtn.IsEnabled = !busy;
        CancelBtn.IsEnabled = !busy;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _closed = true;
        DialogResult = false;
        Close();
    }
}
