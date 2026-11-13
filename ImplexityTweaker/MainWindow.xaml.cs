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
        private sealed record NavEntry(string Tag, string Label, SymbolRegular Icon, string Group);

        private static readonly NavEntry[] NavItems =
        [
            // Главное
            new("firstsetup", "Быстрая настройка Windows", SymbolRegular.Home24, "Главное"),
            new("presets", "Пресеты", SymbolRegular.Flash24, "Главное"),

            // Система
            new("pcinfo", "Информация о ПК", SymbolRegular.System24, "Система"),
            new("benchmark", "Бенчмарк", SymbolRegular.ArrowTrendingSettings24, "Система"),
            new("systemrecovery", "Система и восстановление", SymbolRegular.ArrowSyncCheckmark24, "Система"),
            new("wincomponents", "Компоненты Windows", SymbolRegular.Cube24, "Система"),

            // Производительность и сеть
            new("optimization", "Игровая оптимизация", SymbolRegular.Games24, "Производительность"),
            new("overlay", "ImplexityOverlay", SymbolRegular.Desktop32, "Производительность"),
            new("network", "Сетевые настройки", SymbolRegular.Wifi124, "Производительность"),
            new("processes", "Управление процессами", SymbolRegular.TaskListLtr24, "Производительность"),
            new("autostart", "Автозагрузка", SymbolRegular.Rocket24, "Производительность"),

            // Персонализация
            new("explorer", "Проводник и Рабочий стол", SymbolRegular.FolderOpen24, "Оформление"),
            new("personalization", "Персонализация", SymbolRegular.Wallpaper24, "Оформление"),
            new("contextmenu", "Контекстное меню", SymbolRegular.AppFolder32, "Оформление"),
            new("funsettings", "Фановые настройки", SymbolRegular.Color16, "Оформление"),

            // Безопасность и обновления
            new("security", "Безопасность", SymbolRegular.ShieldKeyhole24, "Защита и ПО"),
            new("wupdate", "Windows Update", SymbolRegular.CloudSync24, "Защита и ПО"),
            new("uwp", "Удаление UWP приложений", SymbolRegular.AppsListDetail24, "Защита и ПО"),

            // Утилиты
            new("apps", "Установка приложений", SymbolRegular.CloudArrowDown24, "Утилиты"),
            new("activation", "Активация Windows", SymbolRegular.LockClosed24, "Утилиты"),
            new("shutdown", "Таймер выключения", SymbolRegular.Clock32, "Утилиты"),
        ];

        private sealed class NavVisual
        {
            public required System.Windows.Controls.Button Button { get; init; }
            public required Border Indicator { get; init; }
            public required SymbolIcon Icon { get; init; }
            public required TextBlock Text { get; init; }
            public required NavEntry Entry { get; init; }
        }

        private readonly Dictionary<string, NavVisual> _navVisuals = new(StringComparer.OrdinalIgnoreCase);

        private string _currentTag = "firstsetup";
        private string? _highlightPresetId;
        private string? _activeNavTag;
        private bool _notifiedTrayMinimize;

        public MainWindow()
        {
            var startMinimized = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase))
                && TrayService.HasActiveBackgroundFeatures();

            InitializeComponent();
            BuildNavigation();
            RefreshStaticText();
            if (!startMinimized)
                RefreshAccountWidget();
            LocalizationService.LanguageChanged += (_, _) => SafeDispatch(RefreshLanguage);
            AccountService.AccountChanged += (_, _) => SafeDispatch(RefreshAccountWidget);
            ThemeApplier.ThemeChanged += (_, _) => SafeDispatch(() =>
            {
                SetActiveNav(_currentTag);
                ApplyWindowChromeForTheme();
            });
            ThemeApplier.UiStyleChanged += (_, _) => SafeDispatch(() =>
            {
                BuildNavigation();
                SetActiveNav(_currentTag);
                ApplyWindowChromeForTheme();
            });
            ApplyWindowChromeForTheme();
            Closing += MainWindow_Closing;

            Loaded += (_, _) =>
            {
                TrayService.SyncWithSettings();

                if (startMinimized)
                {
                    EnterBackgroundTrayMode();
                    return;
                }

                NavigateByTag("firstsetup");
            };

            UiAnimation.PrepareWindowOpen(this);
            Loaded += (_, _) => UiAnimation.AnimateWindowOpen(this, withScale: false);
        }

        private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!TrayService.ForceExit && TrayService.HasActiveBackgroundFeatures())
            {
                e.Cancel = true;
                TrayService.SyncWithSettings();

                var activeList = string.Join(", ", TrayService.GetActiveFeatureNames());

                if (!_notifiedTrayMinimize)
                {
                    _notifiedTrayMinimize = true;
                    await Dialogs.ShowInfoAsync(
                        LocalizationService.T("Работа в системном трее"),
                        LocalizationService.TF(
                            "Implexity Tweaker свернётся в системный трей, так как сейчас включены фоновые функции:\n• {0}\n\nЧтобы полностью закрыть программу, нажмите правой кнопкой по иконке в трее и выберите «Выйти полностью».",
                            string.Join("\n• ", TrayService.GetActiveFeatureNames())));
                }

                EnterBackgroundTrayMode();
                TrayService.ShowBalloon(
                    LocalizationService.T("Implexity Tweaker в трее"),
                    LocalizationService.TF("Фоновые функции активны ({0}). ПКМ по иконке — меню и выход.", activeList));
                return;
            }

            OverlayService.ShutdownAll();
            TrayService.RemoveTrayIcon();
        }

        /// <summary>
        /// Выгружает активную страницу и сжимает рабочий набор процесса,
        /// чтобы фоновый режим в трее потреблял минимум ОЗУ и CPU.
        /// </summary>
        public void EnterBackgroundTrayMode()
        {
            try
            {
                RootFrame.Content = null;
                while (RootFrame.CanGoBack)
                    RootFrame.RemoveBackEntry();

                AccountAvatarBrush.ImageSource = null;
            }
            catch { }

            Hide();
            RamCleanerService.TrimOwnProcessMemory();
            _ = Task.Delay(750).ContinueWith(_ =>
            {
                SafeDispatch(() =>
                {
                    if (!IsVisible)
                        RamCleanerService.TrimOwnProcessMemory();
                });
            });
        }

        /// <summary>Восстанавливает страницу при открытии окна из системного трея.</summary>
        public void RestoreFromBackgroundTrayMode()
        {
            if (RootFrame.Content == null)
                NavigateByTag(_currentTag);

            if (AccountAvatarBrush.ImageSource == null)
                RefreshAccountWidget();
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

            var wasHidden = AccountWidget.Visibility != Visibility.Visible;

            AccountWidget.Visibility = Visibility.Visible;
            LoginButton.Visibility = Visibility.Collapsed;

            if (wasHidden)
                UiAnimation.FadeSlideIn(AccountWidget, offsetY: 6, durationMs: 200);

            AccountName.Text = account.DisplayName;
            AccountName.Foreground = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));

            var hasPrefix = !string.IsNullOrWhiteSpace(account.Prefix);
            AccountPrefixBadge.Visibility = hasPrefix ? Visibility.Visible : Visibility.Collapsed;
            if (hasPrefix)
            {
                var fgBrush = MakeBrush(AccountService.GetPrefixColorHex(account.PrefixColor));
                AccountPrefix.Text = account.Prefix;
                AccountPrefix.Foreground = fgBrush;
                AccountPrefixIcon.Foreground = fgBrush;
                AccountPrefixBadge.Background = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x1A);
                AccountPrefixBadge.BorderBrush = MakeBrush(AccountService.GetPrefixBadgeColorHex(account.PrefixColor), 0x33);
                AccountPrefixBadge.BorderThickness = new Thickness(1);
            }

            AccountNumber.Text = ThemeApplier.IsAppleStyle
                ? LocalizationService.T("Учетная запись Implexity")
                : account.AccountNumber;
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

        private static void SafeDispatch(Action action)
        {
            try
            {
                Application.Current?.Dispatcher.BeginInvoke(action);
            }
            catch
            {
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

                if (AccountService.Current?.AvatarUrl == url)
                    AccountAvatarBrush.ImageSource = bi;
            }
            catch
            {
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
                }
            }
        }

        // ===================== Навигация =====================

        private void BuildNavigation()
        {
            NavButtonHost.Children.Clear();
            _navVisuals.Clear();

            var navStyle = (Style)FindResource("NavItemButtonStyle");
            string? currentGroup = null;

            foreach (var entry in NavItems)
            {
                if (!string.Equals(currentGroup, entry.Group, StringComparison.Ordinal))
                {
                    var isFirst = currentGroup == null;
                    currentGroup = entry.Group;
                    if (ThemeApplier.IsAppleStyle && !isFirst)
                    {
                        var sep = new Border
                        {
                            Height = 1,
                            Margin = new Thickness(8, 10, 8, 6)
                        };
                        sep.SetResourceReference(Border.BackgroundProperty, "App.BorderSubtle");
                        NavButtonHost.Children.Add(sep);
                    }
                    else
                    {
                        NavButtonHost.Children.Add(new TextBlock
                        {
                            Text = LocalizationService.T(entry.Group),
                            Style = (Style)FindResource("SectionCaptionText"),
                            Margin = new Thickness(10, isFirst ? 2 : 12, 6, 4)
                        });
                    }
                }

                var isApple = ThemeApplier.IsAppleStyle;
                var (appleHex, appleSymbol) = isApple
                    ? GetAppleBadgeInfo(entry.Tag, entry.Icon)
                    : ("#0A84FF", entry.Icon);

                var indicator = new Border
                {
                    Width = isApple ? 0 : 3,
                    Height = 16,
                    CornerRadius = new CornerRadius(1.5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = isApple ? new Thickness(0) : new Thickness(0, 0, 8, 0),
                    Opacity = 0
                };
                indicator.SetResourceReference(Border.BackgroundProperty, "App.Accent");

                var icon = new SymbolIcon
                {
                    Symbol = appleSymbol,
                    Filled = isApple,
                    FontSize = isApple ? 14 : 15,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                UIElement iconHost;
                if (isApple)
                {
                    icon.Foreground = Brushes.White;
                    var badgeBase = (Color)ColorConverter.ConvertFromString(appleHex)!;
                    var badgeTop = Color.FromArgb(255,
                        (byte)Math.Min(255, badgeBase.R + 30),
                        (byte)Math.Min(255, badgeBase.G + 30),
                        (byte)Math.Min(255, badgeBase.B + 30));
                    var badgeGrad = new LinearGradientBrush(badgeTop, badgeBase, new Point(0, 0), new Point(0, 1));
                    badgeGrad.Freeze();
                    iconHost = new Border
                    {
                        Width = 26,
                        Height = 26,
                        CornerRadius = new CornerRadius(7),
                        Background = badgeGrad,
                        BorderBrush = MakeBrush("#48FFFFFF", 0x48),
                        BorderThickness = new Thickness(0.8),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = icon
                    };
                }
                else
                {
                    icon.SetResourceReference(SymbolIcon.ForegroundProperty, "App.TextMuted");
                    iconHost = icon;
                }

                var label = new TextBlock
                {
                    Text = LocalizationService.T(entry.Label),
                    FontSize = 13,
                    Margin = new Thickness(10, 0, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, "App.Text");

                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                Grid.SetColumn(indicator, 0);
                Grid.SetColumn(iconHost, 1);
                Grid.SetColumn(label, 2);
                row.Children.Add(indicator);
                row.Children.Add(iconHost);
                row.Children.Add(label);

                var btn = new System.Windows.Controls.Button
                {
                    Tag = entry.Tag,
                    Style = navStyle,
                    Content = row,
                    ToolTip = LocalizationService.T(entry.Label)
                };
                btn.Click += NavButton_Click;

                NavButtonHost.Children.Add(btn);
                _navVisuals[entry.Tag] = new NavVisual
                {
                    Button = btn,
                    Indicator = indicator,
                    Icon = icon,
                    Text = label,
                    Entry = entry
                };
            }
        }

        private void RefreshStaticText()
        {
            try
            {
                SidebarSubtitle.Text = LocalizationService.T("Настройка Windows 10 / 11");
                SearchBtnText.Text = LocalizationService.T("Поиск");
                RestartExplorerText.Text = LocalizationService.T("Перезапустить проводник");
                OpenSettingsText.Text = LocalizationService.T("Настройки / О программе");
                LoginButtonText.Text = LocalizationService.T("Войти");
                AccountWidget.ToolTip = LocalizationService.T("Открыть профиль на сайте");
                OsBadgeText.Text = WindowsVersionService.DescribeSystem();
                if (AccountService.Current is { } acc)
                {
                    AccountNumber.Text = ThemeApplier.IsAppleStyle
                        ? LocalizationService.T("Учетная запись Implexity")
                        : acc.AccountNumber;
                }
                UpdateBreadcrumb(_currentTag);
            }
            catch
            {
            }
        }

        private void UpdateBreadcrumb(string tag)
        {
            if (string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase))
            {
                BreadcrumbCategoryText.Text = LocalizationService.T("Параметры");
                BreadcrumbSectionText.Text = LocalizationService.T("Настройки / О программе");
                return;
            }

            if (_navVisuals.TryGetValue(tag, out var vis))
            {
                BreadcrumbCategoryText.Text = LocalizationService.T(vis.Entry.Group);
                BreadcrumbSectionText.Text = LocalizationService.T(vis.Entry.Label);
            }
        }

        private void RefreshLanguage()
        {
            BuildNavigation();
            RefreshStaticText();
            SetActiveNav(_currentTag);
            NavigateByTag(_currentTag, force: true);
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement el)
                NavigateByTag(el.Tag?.ToString());
        }

        public void NavigateByTag(string? tag, bool force = false)
        {
            tag ??= "firstsetup";
            if (!force
                && string.Equals(_currentTag, tag, StringComparison.OrdinalIgnoreCase)
                && RootFrame.Content != null
                && !string.Equals(tag, "presets", StringComparison.OrdinalIgnoreCase))
                return;

            _currentTag = tag;

            if (!string.Equals(tag, "presets", StringComparison.OrdinalIgnoreCase))
                _highlightPresetId = null;

            SetActiveNav(tag);
            UpdateBreadcrumb(tag);

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
                "presets" => new Pages.PresetsPage(_highlightPresetId),
                "processes" => new Pages.ProcessesPage(),
                "autostart" => new Pages.AutostartPage(),
                "pcinfo" => new Pages.PcInfoPage(),
                "benchmark" => new Pages.BenchmarkPage(),
                "funsettings" => new Pages.FunSettingsPage(),
                "settings" => new Pages.SettingsPage(),
                "optimization" => new Pages.OptimizationPage(),
                "overlay" => new Pages.OverlayPage(),
                "network" => new Pages.NetworkPage(),
                _ => new Pages.FirstSetupPage()
            };

            RootFrame.Navigate(page);
            UiAnimation.AnimatePageEntrance(page);

            while (RootFrame.CanGoBack)
                RootFrame.RemoveBackEntry();
        }

        private void SetActiveNav(string tag)
        {
            var isSettings = string.Equals(tag, "settings", StringComparison.OrdinalIgnoreCase);
            var isApple = ThemeApplier.IsAppleStyle;
            _activeNavTag = tag;

            foreach (var (itemTag, vis) in _navVisuals)
            {
                var active = !isSettings && string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase);
                vis.Indicator.Opacity = !isApple && active ? 1 : 0;
                if (active)
                    vis.Button.SetResourceReference(Control.BackgroundProperty, isApple ? "App.PrimaryButtonBrush" : "App.NavActiveBg");
                else
                    vis.Button.ClearValue(Control.BackgroundProperty);

                if (isApple)
                {
                    vis.Text.SetResourceReference(TextBlock.ForegroundProperty, active ? "App.AccentText" : "App.Text");
                }
                else
                {
                    vis.Icon.SetResourceReference(SymbolIcon.ForegroundProperty, active ? "App.IconAccent" : "App.TextMuted");
                    vis.Text.SetResourceReference(TextBlock.ForegroundProperty, "App.Text");
                }
                vis.Text.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }

            if (SettingsNavPill != null && OpenSettingsBtn != null && SettingsNavIcon != null && OpenSettingsText != null)
            {
                SettingsNavPill.Opacity = !isApple && isSettings ? 1 : 0;
                if (isSettings)
                    OpenSettingsBtn.SetResourceReference(Control.BackgroundProperty, isApple ? "App.PrimaryButtonBrush" : "App.NavActiveBg");
                else
                    OpenSettingsBtn.ClearValue(Control.BackgroundProperty);

                if (isApple)
                {
                    SettingsNavIcon.Foreground = Brushes.White;
                    OpenSettingsText.SetResourceReference(TextBlock.ForegroundProperty, isSettings ? "App.AccentText" : "App.Text");
                }
                else
                {
                    SettingsNavIcon.SetResourceReference(SymbolIcon.ForegroundProperty, isSettings ? "App.IconAccent" : "App.TextMuted");
                    OpenSettingsText.SetResourceReference(TextBlock.ForegroundProperty, "App.Text");
                }
                OpenSettingsText.FontWeight = isSettings ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private static (string Hex, SymbolRegular Icon) GetAppleBadgeInfo(string tag, SymbolRegular fallback) => tag switch
        {
            "firstsetup" => ("#FF9500", SymbolRegular.Flash24),
            "presets" => ("#AF52DE", SymbolRegular.Sparkle24),
            "pcinfo" => ("#8E8E93", SymbolRegular.Settings24),
            "benchmark" => ("#30D158", SymbolRegular.TopSpeed24),
            "systemrecovery" => ("#007AFF", SymbolRegular.ArrowSync24),
            "wincomponents" => ("#5E5CE6", SymbolRegular.Cube24),
            "optimization" => ("#FF2D55", SymbolRegular.Games24),
            "overlay" => ("#32ADE6", SymbolRegular.Desktop24),
            "network" => ("#007AFF", SymbolRegular.Wifi124),
            "processes" => ("#30D158", SymbolRegular.Pulse24),
            "autostart" => ("#FF9F0A", SymbolRegular.Rocket24),
            "explorer" => ("#0A84FF", SymbolRegular.Folder24),
            "personalization" => ("#32ADE6", SymbolRegular.Image24),
            "contextmenu" => ("#5856D6", SymbolRegular.LineHorizontal320),
            "funsettings" => ("#FF375F", SymbolRegular.Color24),
            "security" => ("#0A84FF", SymbolRegular.Shield24),
            "wupdate" => ("#8E8E93", SymbolRegular.ArrowClockwise24),
            "uwp" => ("#FF453A", SymbolRegular.Delete24),
            "apps" => ("#007AFF", SymbolRegular.CloudArrowDown24),
            "activation" => ("#8E8E93", SymbolRegular.Key24),
            "shutdown" => ("#5E5CE6", SymbolRegular.Clock24),
            _ => ("#8E8E93", fallback)
        };

        public void OpenPreset(string presetId)
        {
            _highlightPresetId = presetId;
            NavigateByTag("presets");
        }

        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.K)
                return;

            if (!System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control))
                return;

            e.Handled = true;
            OpenSearch();
        }

        private void OpenSearch()
        {
            try
            {
                var search = new SearchWindow { Owner = this, Host = this };
                search.ShowDialog();
            }
            catch (Exception ex)
            {
                _ = Dialogs.ShowErrorAsync("Поиск недоступен", ex.Message);
            }
        }

        private void OpenSearch_Click(object sender, RoutedEventArgs e) => OpenSearch();

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

        private void OpenSettings_Click(object sender, RoutedEventArgs e) => NavigateByTag("settings");

        private void ApplyWindowChromeForTheme()
        {
            var isApple = ThemeApplier.IsAppleStyle;
            var isMacChrome = ThemeApplier.IsIosTheme || isApple;

            if (SidebarColumn != null)
                SidebarColumn.Width = new GridLength(isApple ? 268 : 280);
            if (SidebarBorder != null)
            {
                if (isApple)
                {
                    SidebarBorder.Margin = new Thickness(12, 12, 0, 12);
                    SidebarBorder.CornerRadius = new CornerRadius(22);
                    SidebarBorder.BorderThickness = new Thickness(1);
                    SidebarBorder.SetResourceReference(Border.BackgroundProperty, "App.GlassSidebarBrush");
                    SidebarBorder.SetResourceReference(Border.BorderBrushProperty, "App.GlassRimBrush");
                    SidebarBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        ShadowDepth = 6,
                        BlurRadius = 24,
                        Opacity = 0.28,
                        Color = Colors.Black
                    };
                }
                else
                {
                    SidebarBorder.Margin = new Thickness(0);
                    SidebarBorder.CornerRadius = new CornerRadius(0);
                    SidebarBorder.BorderThickness = new Thickness(0, 0, 1, 0);
                    SidebarBorder.SetResourceReference(Border.BackgroundProperty, "App.Sidebar");
                    SidebarBorder.SetResourceReference(Border.BorderBrushProperty, "App.BorderSubtle");
                    SidebarBorder.Effect = null;
                }
            }
            if (SearchShortcutBadge != null)
                SearchShortcutBadge.Visibility = isApple ? Visibility.Collapsed : Visibility.Visible;
            if (BrandHeaderStack != null)
                BrandHeaderStack.Visibility = isApple ? Visibility.Collapsed : Visibility.Visible;
            if (MacTrafficLights != null)
                MacTrafficLights.Visibility = isMacChrome ? Visibility.Visible : Visibility.Collapsed;
            if (AppTitleBar != null)
                AppTitleBar.Visibility = isMacChrome ? Visibility.Collapsed : Visibility.Visible;
            if (HeaderDragBar != null)
            {
                HeaderDragBar.Height = isApple ? 60 : 44;
                HeaderDragBar.Padding = isMacChrome ? new Thickness(28, isApple ? 10 : 0, 28, 0) : new Thickness(28, 0, 152, 0);
                HeaderDragBar.BorderThickness = isApple ? new Thickness(0) : new Thickness(0, 0, 0, 1);
                if (isApple)
                    HeaderDragBar.Background = Brushes.Transparent;
                else
                    HeaderDragBar.SetResourceReference(Border.BackgroundProperty, "App.Background");
            }
            if (SettingsNavPill != null)
            {
                SettingsNavPill.Width = isApple ? 0 : 3;
                SettingsNavPill.Margin = isApple ? new Thickness(0) : new Thickness(0, 0, 8, 0);
            }
            if (RestartNavPill != null)
            {
                RestartNavPill.Width = isApple ? 0 : 3;
                RestartNavPill.Margin = isApple ? new Thickness(0) : new Thickness(0, 0, 8, 0);
            }
            if (SettingsIconBadge != null && SettingsNavIcon != null)
            {
                SettingsIconBadge.Width = isApple ? 26 : double.NaN;
                SettingsIconBadge.Height = isApple ? 26 : double.NaN;
                SettingsIconBadge.CornerRadius = new CornerRadius(isApple ? 7 : 6);
                SettingsIconBadge.Background = isApple ? MakeBrush("#8E8E93") : Brushes.Transparent;
                SettingsNavIcon.FontSize = isApple ? 14 : 16;
                SettingsNavIcon.Filled = isApple;
            }
            if (RestartIconBadge != null && RestartNavIcon != null)
            {
                RestartIconBadge.Width = isApple ? 26 : double.NaN;
                RestartIconBadge.Height = isApple ? 26 : double.NaN;
                RestartIconBadge.CornerRadius = new CornerRadius(isApple ? 7 : 6);
                RestartIconBadge.Background = isApple ? MakeBrush("#5856D6") : Brushes.Transparent;
                RestartNavIcon.FontSize = isApple ? 14 : 15;
                RestartNavIcon.Filled = isApple;
                if (isApple)
                    RestartNavIcon.Foreground = Brushes.White;
                else
                    RestartNavIcon.SetResourceReference(SymbolIcon.ForegroundProperty, "App.TextMuted");
            }
            if (BreadcrumbCategoryPanel != null)
                BreadcrumbCategoryPanel.Visibility = isApple ? Visibility.Collapsed : Visibility.Visible;
            if (BreadcrumbSectionText != null)
            {
                BreadcrumbSectionText.FontSize = isApple ? 19 : 12;
                BreadcrumbSectionText.FontWeight = isApple ? FontWeights.Bold : FontWeights.SemiBold;
            }
            if (AccountCardBorder != null && TopSidebarStack != null && BottomSidebarStack != null)
            {
                var targetParent = isApple ? TopSidebarStack : BottomSidebarStack;
                if (AccountCardBorder.Parent is Panel currentParent && currentParent != targetParent)
                {
                    currentParent.Children.Remove(AccountCardBorder);
                    targetParent.Children.Add(AccountCardBorder);
                }
                if (isApple)
                {
                    AccountCardBorder.Background = Brushes.Transparent;
                    AccountCardBorder.BorderThickness = new Thickness(0);
                    AccountCardBorder.Padding = new Thickness(0);
                    AccountCardBorder.Margin = new Thickness(0, 10, 0, 2);
                }
                else
                {
                    AccountCardBorder.SetResourceReference(Border.BackgroundProperty, "App.Surface");
                    AccountCardBorder.BorderThickness = new Thickness(1);
                    AccountCardBorder.Padding = new Thickness(6);
                    AccountCardBorder.Margin = new Thickness(0);
                }
            }
            if (AccountAvatarBox != null && AccountAvatarFallback != null)
            {
                AccountAvatarBox.Width = isApple ? 40 : 36;
                AccountAvatarBox.Height = isApple ? 40 : 36;
                AccountAvatarFallback.CornerRadius = new CornerRadius(isApple ? 20 : 18);
            }
            if (AccountName != null)
                AccountName.FontSize = isApple ? 13.5 : 12.5;
            if (AccountOpenIcon != null)
                AccountOpenIcon.Visibility = isApple ? Visibility.Collapsed : Visibility.Visible;
            if (AccountNumber != null && AccountService.Current is { } currentAcc)
            {
                AccountNumber.Text = isApple
                    ? LocalizationService.T("Учетная запись Implexity")
                    : currentAcc.AccountNumber;
            }
        }

        private void HeaderDragBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                MacMaximize_Click(sender, e);
            else if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void MacClose_Click(object sender, RoutedEventArgs e) => Close();

        private void MacMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MacMaximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
}
