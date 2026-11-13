using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Windows;

public partial class LoginWindow : Window
{
    private bool _closed;

    public LoginWindow()
    {
        InitializeComponent();
        LogoClip.Rect = new System.Windows.Rect(0, 0, 48, 48);
        Closed += (_, _) => _closed = true;

        UiAnimation.PrepareWindowOpen(this);
        Loaded += (_, _) => UiAnimation.AnimateWindowOpen(this);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        DependencyObject? node = e.OriginalSource as DependencyObject;
        while (node != null)
        {
            if (node is ButtonBase or TextBoxBase or System.Windows.Controls.PasswordBox or Wpf.Ui.Controls.Button)
                return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
        }

        try { DragMove(); } catch { }
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
