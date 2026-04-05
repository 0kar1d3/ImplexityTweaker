using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using Wpf.Ui.Controls;
using Wpf.Ui.Common;

namespace ImplexityTweaker
{
    public partial class MainWindow : UiWindow
    {
        private static readonly (string Tag, string Label)[] NavItems =
        [
            ("explorer", "Проводник и Рабочий стол"),
            ("wupdate", "Windows Update"),
            ("systemrecovery", "Система и восстановление"),
            ("uwp", "Удаление UWP приложений"),
            ("personalization", "Персонализация"),
            ("contextmenu", "Контекстное меню"),
            ("shutdown", "Таймер выключения"),
            ("wincomponents", "Компоненты Windows"),
            ("activation", "Активация Windows"),
            ("apps", "Установка приложений"),
            ("firstsetup", "Быстрая настройка Windows"),
            ("processes", "Управление процессами"),
            ("pcinfo", "Информация о ПК")
        ];

        public MainWindow()
        {
            InitializeComponent();
            BuildNavigation();
            Loaded += (_, _) => NavigateByTag("firstsetup");
        }

        private void BuildNavigation()
        {
            NavButtonHost.Children.Clear();
            foreach (var (tag, label) in NavItems)
            {
                var btn = new Wpf.Ui.Controls.Button
                {
                    Tag = tag,
                    Content = label,
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

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el)
                NavigateByTag(el.Tag?.ToString());
        }

        public void NavigateByTag(string? tag)
        {
            tag ??= "firstsetup";
            SetActiveNav(tag);

            Page page = tag switch
            {
                "explorer" => new Pages.ExplorerPage(),
                "wupdate" => new Pages.WindowsUpdatePage(),
                "systemrecovery" => new Pages.SystemRecoveryPage(),
                "uwp" => new Pages.UwpAppsPage(),
                "personalization" => new Pages.PersonalizationPage(),
                "contextmenu" => new Pages.ContextMenuPage(),
                "shutdown" => new Pages.ShutdownTimerPage(),
                "wincomponents" => new Pages.WindowsComponentsPage(),
                "activation" => new Pages.ActivationPage(),
                "apps" => new Pages.AppInstallPage(),
                "firstsetup" => new Pages.FirstSetupPage(),
                "processes" => new Pages.ProcessesPage(),
                "pcinfo" => new Pages.PcInfoPage(),
                "settings" => new Pages.SettingsPage(),
                _ => new Pages.FirstSetupPage()
            };

            RootFrame.Navigate(page);
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
            await CommandRunner.RunPowerShellAsync("taskkill /f /im explorer.exe; Start-Process explorer.exe");
            await Dialogs.ShowInfoAsync("Готово", "Проводник перезапущен.");
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            NavigateByTag("settings");
        }
    }
}
