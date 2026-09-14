using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class NetworkPage : Page
{
    private static readonly (string Host, string Title)[] PingTargets =
    [
        ("1.1.1.1", "Cloudflare"),
        ("8.8.8.8", "Google"),
        ("9.9.9.9", "Quad9"),
        ("77.88.8.8", "Яндекс"),
    ];

    private bool _init;

    public NetworkPage()
    {
        InitializeComponent();
        InitializeDns();
        Loaded += async (_, _) => await LoadStateAsync();
    }

    private async Task LoadStateAsync()
    {
        _init = true;
        try
        {
            Ipv6Toggle.IsChecked = await NetworkToolsService.GetIpv6EnabledAsync();
            HostsToggle.IsChecked = HostsService.IsBlocked();
            LoadNetworkTweaks();
        }
        catch
        {
            // состояние не критично для работы страницы
        }
        finally
        {
            _init = false;
        }
    }

    private async void Ping_Click(object sender, RoutedEventArgs e)
    {
        PingHost.Children.Clear();
        PingHost.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Идёт тест…"),
            Style = (Style)FindResource("ItemDescriptionText")
        });

        var results = new List<string>();
        foreach (var (host, name) in PingTargets)
        {
            var title = LocalizationService.T(name);
            var r = await NetworkToolsService.PingAsync(host, 8);
            results.Add(r == null
                ? LocalizationService.TF("{0}: нет ответа", title)
                : LocalizationService.TF("{0}: {1} мс · джиттер {2} мс · потери {3}", title, r.Avg, r.Jitter, r.Loss));
        }

        PingHost.Children.Clear();
        foreach (var line in results)
        {
            PingHost.Children.Add(new TextBlock
            {
                Text = line,
                Style = (Style)FindResource("ItemDescriptionText"),
                Margin = new Thickness(0, 2, 0, 2)
            });
        }
    }

    private async void ResetStack_Click(object sender, RoutedEventArgs e)
    {
        var confirm = await Dialogs.ShowConfirmAsync(LocalizationService.T("Сброс сетевого стека"),
            LocalizationService.T("Сбросить Winsock, TCP/IP, IPv6, DNS и ARP?\n\nРекомендуется перезагрузка."));
        if (!confirm)
            return;

        try
        {
            await NetworkToolsService.ResetStackAsync();
            await Dialogs.ShowInfoAsync(LocalizationService.T("Готово"),
                LocalizationService.T("Сетевой стек сброшен. Перезагрузите компьютер для полного применения."));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"), ex.Message);
        }
    }

    private async void Ipv6Toggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_init)
            return;

        try
        {
            await NetworkToolsService.SetIpv6EnabledAsync(Ipv6Toggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"), ex.Message);
        }
    }

    private async void HostsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_init)
            return;

        try
        {
            if (HostsToggle.IsChecked == true)
                HostsService.Enable();
            else
                HostsService.Disable();

            await Dialogs.ShowInfoAsync(LocalizationService.T("Готово"),
                HostsToggle.IsChecked == true
                    ? LocalizationService.T("Домены телеметрии добавлены в hosts.")
                    : LocalizationService.T("Блокировка телеметрии убрана из hosts."));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"),
                LocalizationService.T("Не удалось изменить файл hosts. Запустите твикер от администратора.") + " " + ex.Message);
        }
    }

    private async void Wifi_Click(object sender, RoutedEventArgs e)
    {
        WifiHost.Children.Clear();
        WifiHost.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Чтение профилей…"),
            Style = (Style)FindResource("ItemDescriptionText")
        });

        var profiles = await NetworkToolsService.GetWifiProfilesAsync();

        WifiHost.Children.Clear();
        if (profiles.Count == 0)
        {
            WifiHost.Children.Add(new TextBlock
            {
                Text = LocalizationService.T("Сохранённых Wi-Fi сетей не найдено."),
                Style = (Style)FindResource("ItemDescriptionText")
            });
            return;
        }

        foreach (var profile in profiles)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = profile.Name, Style = (Style)FindResource("ItemTitleText") });
            info.Children.Add(new TextBlock
            {
                Text = string.IsNullOrEmpty(profile.Password)
                    ? LocalizationService.T("Пароль: нет данных")
                    : LocalizationService.TF("Пароль: {0}", profile.Password),
                Style = (Style)FindResource("ItemDescriptionText")
            });
            Grid.SetColumn(info, 0);
            grid.Children.Add(info);

            var copy = new Wpf.Ui.Controls.Button
            {
                Content = LocalizationService.T("Копировать"),
                Appearance = Wpf.Ui.Common.ControlAppearance.Secondary,
                Padding = new Thickness(14, 6, 14, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            var password = profile.Password;
            copy.Click += (_, _) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(password))
                        Clipboard.SetText(password);
                }
                catch
                {
                    // буфер обмена может быть занят другим процессом
                }
            };
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);

            WifiHost.Children.Add(grid);
        }
    }

    // ===================== DNS =====================

    private void InitializeDns()
    {
        DnsCombo.Items.Clear();
        foreach (var provider in DnsService.Providers)
            DnsCombo.Items.Add(new DnsItem(provider));
        DnsCombo.SelectedIndex = 0;

        _ = RefreshDnsStateAsync();
    }

    private async Task RefreshDnsStateAsync()
    {
        try
        {
            var current = await DnsService.GetCurrentAsync();
            var provider = DnsService.MatchProvider(current);

            if (string.IsNullOrWhiteSpace(current))
            {
                DnsCurrentText.Text = LocalizationService.TF("Сейчас: {0}",
                    LocalizationService.T("Автоматически (DHCP)"));
            }
            else if (provider != null)
            {
                DnsCurrentText.Text = LocalizationService.TF("Сейчас: {0}", provider.Title);
                foreach (DnsItem item in DnsCombo.Items)
                {
                    if (item.Provider.Id == provider.Id)
                    {
                        DnsCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            else
            {
                DnsCurrentText.Text = LocalizationService.TF("Сейчас: {0}", current);
            }
        }
        catch
        {
            DnsCurrentText.Text = string.Empty;
        }
    }

    private async void ApplyDns_Click(object sender, RoutedEventArgs e)
    {
        if (DnsCombo.SelectedItem is not DnsItem item)
            return;

        DnsCombo.IsEnabled = false;
        try
        {
            var adapter = await DnsService.ApplyAsync(item.Provider);
            await RefreshDnsStateAsync();
            await Dialogs.ShowInfoAsync(LocalizationService.T("DNS изменён"),
                LocalizationService.TF("{0} → {1}", item.Provider.Title, adapter));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка смены DNS"), ex.Message);
        }
        finally
        {
            DnsCombo.IsEnabled = true;
        }
    }

    private async void ResetDns_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var adapter = await DnsService.ResetAsync();
            await RefreshDnsStateAsync();
            await Dialogs.ShowInfoAsync(LocalizationService.T("DNS изменён"),
                LocalizationService.TF("{0} → {1}", LocalizationService.T("Автоматически (DHCP)"), adapter));
        }
        catch (Exception ex)
        {
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка смены DNS"), ex.Message);
        }
    }

    private async void RefreshDns_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDnsStateAsync();
    }

    private sealed class DnsItem
    {
        public DnsItem(DnsService.DnsProvider provider) => Provider = provider;

        public DnsService.DnsProvider Provider { get; }

        public override string ToString() => $"{Provider.Title} ({Provider.ServersText})";
    }

    // ===================== Сетевые твики =====================

    private void LoadNetworkTweaks()
    {
        var s = AppSettings.Load();
        DisableNagleToggle.IsChecked = s.DisableNagle ?? NetworkLatencyService.GetTcpNoDelayEnabled();
        DisableAutoTuningToggle.IsChecked = s.DisableAutoTuning ?? OptimizationService.GetAutoTuningDisabled();
        PrioritizeBurstToggle.IsChecked = s.PrioritizeBurst ?? false;
        TcpAckFrequencyToggle.IsChecked = s.TcpAckFrequency ?? NetworkLatencyService.GetTcpAckFrequencyEnabled();
    }

    private void SaveNetworkTweaks()
    {
        var s = AppSettings.Load();
        s.DisableNagle = DisableNagleToggle.IsChecked == true;
        s.DisableAutoTuning = DisableAutoTuningToggle.IsChecked == true;
        s.PrioritizeBurst = PrioritizeBurstToggle.IsChecked == true;
        s.TcpAckFrequency = TcpAckFrequencyToggle.IsChecked == true;
        s.Save();
    }

    /// <summary>Применяет сетевой твик и возвращает переключатель назад при ошибке.</summary>
    private async Task ApplyNetworkTweakAsync(
        Wpf.Ui.Controls.ToggleSwitch toggle, Func<bool, Task> apply, string turnedOn, string turnedOff)
    {
        if (_init || !IsLoaded)
            return;

        var enabled = toggle.IsChecked == true;
        try
        {
            await apply(enabled);
            SaveNetworkTweaks();
            await Dialogs.ShowInfoAsync(LocalizationService.T("Готово"), enabled ? turnedOn : turnedOff);
        }
        catch (Exception ex)
        {
            _init = true;
            try { toggle.IsChecked = !enabled; } finally { _init = false; }
            await Dialogs.ShowErrorAsync(LocalizationService.T("Ошибка"), ex.Message);
        }
    }

    private async void DisableNagleToggle_Toggled(object sender, RoutedEventArgs e) =>
        await ApplyNetworkTweakAsync(DisableNagleToggle,
            enabled => NetworkLatencyService.SetTcpNoDelayAsync(enabled),
            LocalizationService.T("Алгоритм Nagle отключён (TCPNoDelay = 1)."),
            LocalizationService.T("Алгоритм Nagle включён обратно (TCPNoDelay = 0)."));

    private async void DisableAutoTuningToggle_Toggled(object sender, RoutedEventArgs e) =>
        await ApplyNetworkTweakAsync(DisableAutoTuningToggle,
            enabled => OptimizationService.SetAutoTuningDisabledAsync(enabled),
            LocalizationService.T("TCP Auto-Tuning отключён."),
            LocalizationService.T("TCP Auto-Tuning включён обратно."));

    private async void PrioritizeBurstToggle_Toggled(object sender, RoutedEventArgs e) =>
        await ApplyNetworkTweakAsync(PrioritizeBurstToggle,
            enabled => OptimizationService.SetNetworkQoSAsync(enabled),
            LocalizationService.T("Приоритезация трафика (QoS) включена."),
            LocalizationService.T("Приоритезация трафика (QoS) выключена."));

    private async void TcpAckFrequencyToggle_Toggled(object sender, RoutedEventArgs e) =>
        await ApplyNetworkTweakAsync(TcpAckFrequencyToggle,
            enabled => NetworkLatencyService.SetTcpAckFrequencyAsync(enabled),
            LocalizationService.T("Мгновенная отправка ACK включена (TcpAckFrequency = 1)."),
            LocalizationService.T("Отправка ACK возвращена к обычной (TcpAckFrequency = 2)."));

}
