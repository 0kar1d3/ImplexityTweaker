using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;

namespace ImplexityTweaker
{
    public partial class MainWindow : UiWindow
    {
        private static readonly (string Tag, string Label)[] NavItems =
        [
            ("explorer", "Проводник и Рабочий стол"),
            ("wupdate", "Windows Update"),
            ("systemrecovery", "Система и восстановление"),
            ("security", "Безопасность"),
            ("uwp", "Удаление UWP приложений"),
            ("personalization", "Персонализация"),
            ("contextmenu", "Контекстное меню"),
            ("shutdown", "Таймер выключения"),
            ("wincomponents", "Компоненты Windows"),
            ("activation", "Активация Windows"),
            ("apps", "Установка приложений"),
            ("optimization", "Игровая оптимизация"),
            ("network", "Сетевые настройки"),
            ("firstsetup", "Быстрая настройка Windows"),
            ("processes", "Управление процессами"),
            ("autostart", "Автозагрузка"),
            ("pcinfo", "Информация о ПК"),
            ("funsettings", "Фановые настройки")
        ];

        private string _currentTag = "firstsetup";

        public MainWindow()
        {
            InitializeComponent();
            BuildNavigation();
            RefreshStaticText();
            RefreshAccountWidget();
            LocalizationService.LanguageChanged += (_, _) => SafeDispatch(RefreshLanguage);
            AccountService.AccountChanged += (_, _) => SafeDispatch(RefreshAccountWidget);
            Loaded += (_, _) => NavigateByTag("firstsetup");
        }

        // ===================== Виджет аккаунта =====================

        private void RefreshAccountWidget()
        {
            var account = AccountService.Current;
            if (account == null)
            {
                AccountWidget.Visibility = Visibility.Collapsed;
                LoginButton.Visibility = Visibility.Visible;
                return;
            }

            AccountWidget.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Collapsed;

            AccountName.Text = account.DisplayName;
            AccountName.Foreground = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));

            var hasPrefix = !string.IsNullOrWhiteSpace(account.Prefix);
            AccountPrefixBadge.Visibility = hasPrefix ? Visibility.Visible : Visibility.Collapsed;
            if (hasPrefix)
            {
                AccountPrefix.Text = "⚡ " + account.Prefix;
                AccountPrefix.Foreground = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));
                AccountPrefixBadge.Background = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x1A);
                AccountPrefixBadge.BorderBrush = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x33);
                AccountPrefixBadge.BorderThickness = new Thickness(1);
            }

            AccountNumber.Text = account.AccountNumber;
            AccountAvatarBrush.ImageSource = null;

            _ = LoadAvatarAsync(account.AvatarUrl);
        }

        private static SolidColorBrush MakeBrush(string hex, byte alpha = 0xFF)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex)!;
            c.A = alpha;
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }

        /// <summary>Асинхронно выполняет действие на UI-потоке, не падая при завершении приложения.</summary>
        private static void SafeDispatch(Action action)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(action);
            }
            catch
            {
                // Приложение закрывается — пропускаем.
            }
        }

        private async Task LoadAvatarAsync(string? url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    return;

                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var bytes = await client.GetByteArrayAsync(url);
                using var ms = new MemoryStream(bytes);

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();

                // Аватар мог смениться/аккаунт мог выйти, пока шла загрузка, — не подставляем чужой.
                if (AccountService.Current?.AvatarUrl == url)
                    AccountAvatarBrush.ImageSource = bi;
            }
            catch
            {
                // Оставляем иконку-заглушку.
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            var login = new LoginWindow { Owner = this };
            login.ShowDialog();
        }

        private void AccountWidget_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AccountService.Current is { } account)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(AccountService.GetProfileUrl(account)) { UseShellExecute = true });
                }
                catch
                {
                    /* ignore */
                }
            }
        }

        private void BuildNavigation()
        {
            NavButtonHost.Children.Clear();
            foreach (var (tag, label) in NavItems)
            {
                var btn = new Wpf.Ui.Controls.Button
                {
                    Tag = tag,
                    Content = LocalizationService.T(label),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 10),
                    Appearance = ControlAppearance.Transparent,
                    Padding = new Thickness(12, 10, 12, 10)
                };
                btn.Click += NavButton_Click;
                NavButtonHost.Children.Add(btn);
            }
        }

        /// <summary>Переводит статичные тексты нижней панели и подзаголовка сайдбара.</summary>
        private void RefreshStaticText()
        {
            try
            {
                SidebarSubtitle.Text = LocalizationService.T("Гибкая настройка для Windows 10 / 11");
                RestartExplorerBtn.Content = "⟲  " + LocalizationService.T("Перезапустить проводник");
                OpenSettingsBtn.Content = "⚙  " + LocalizationService.T("Настройки / О программе");
                LoginButtonText.Text = LocalizationService.T("Войти");
                AccountWidget.ToolTip = LocalizationService.T("Открыть профиль на сайте");
            }
            catch
            {
                // Элементы ещё не инициализированы — пропускаем.
            }
        }

        /// <summary>Перестраивает навигацию и обновляет текущую страницу при смене языка.</summary>
        private void RefreshLanguage()
        {
            BuildNavigation();
            RefreshStaticText();
            SetActiveNav(_currentTag);
            NavigateByTag(_currentTag);
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el)
                NavigateByTag(el.Tag?.ToString());
        }

        public void NavigateByTag(string? tag)
        {
            tag ??= "firstsetup";
            _currentTag = tag;
            SetActiveNav(tag);

            Page page = tag switch
            {
                "explorer" => new Pages.ExplorerPage(),
                "wupdate" => new Pages.WindowsUpdatePage(),
                "systemrecovery" => new Pages.SystemRecoveryPage(),
                "security" => new Pages.SecurityPage(),
                "uwp" => new Pages.UwpAppsPage(),
                "personalization" => new Pages.PersonalizationPage(),
                "contextmenu" => new Pages.ContextMenuPage(),
                "shutdown" => new Pages.ShutdownTimerPage(),
                "wincomponents" => new Pages.WindowsComponentsPage(),
                "activation" => new Pages.ActivationPage(),
                "apps" => new Pages.AppInstallPage(),
                "firstsetup" => new Pages.FirstSetupPage(),
                "processes" => new Pages.ProcessesPage(),
                "autostart" => new Pages.AutostartPage(),
                "pcinfo" => new Pages.PcInfoPage(),
                "funsettings" => new Pages.FunSettingsPage(),
                "settings" => new Pages.SettingsPage(),
                "optimization" => new Pages.OptimizationPage(),
                "network" => new Pages.NetworkPage(),
                _ => new Pages.FirstSetupPage()
            };

            RootFrame.Navigate(page);

            // Чистим журнал навигации: страницы создаются заново при каждом
            // переключении вкладки, поэтому старые экземпляры не должны
            // оставаться в памяти (иначе каждая навигация их накапливает).
            while (RootFrame.CanGoBack)
                RootFrame.RemoveBackEntry();
        }

        private void SetActiveNav(string tag)
        {
            var isSettings = string.Equals(tag, "settings", System.StringComparison.OrdinalIgnoreCase);
            foreach (var child in NavButtonHost.Children)
            {
                if (child is not Wpf.Ui.Controls.Button navBtn || navBtn.Tag == null)
                    continue;

                if (isSettings)
                {
                    navBtn.Appearance = ControlAppearance.Transparent;
                    continue;
                }

                var active = string.Equals(navBtn.Tag.ToString(), tag, System.StringComparison.OrdinalIgnoreCase);
                navBtn.Appearance = active ? ControlAppearance.Primary : ControlAppearance.Transparent;
            }
        }

        private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(() =>
            {
                foreach (var process in Process.GetProcessesByName("explorer"))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch { }
                }
                Process.Start("explorer.exe");
            });

            await Dialogs.ShowInfoAsync("Готово", "Проводник успешно перезапущен.");
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            NavigateByTag("settings");
        }
    }
}
