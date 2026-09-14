using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using ImplexityTweaker.Windows;
using Wpf.Ui.Controls;

namespace ImplexityTweaker.Pages;

public partial class WindowsComponentsPage : Page
{
    private bool _isProgrammaticChange = false;

    public WindowsComponentsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadFeatureStatesAsync();
    }

    private void OptionalFeatures_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("OptionalFeatures") { UseShellExecute = true });
    }

    private void Appwiz_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("appwiz.cpl") { UseShellExecute = true });
    }

    private void OptionalFeaturesSettings_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:optionalfeatures") { UseShellExecute = true });
    }

    // ===================== Состояния компонентов =====================

    private async Task LoadFeatureStatesAsync()
    {
        _isProgrammaticChange = true;
        try
        {
            NetFx3Toggle.IsChecked = await WindowsFeaturesService.IsFeatureEnabledAsync("NetFx3");
            WslToggle.IsChecked = await WindowsFeaturesService.IsFeatureEnabledAsync("Microsoft-Windows-Subsystem-Linux");
            SandboxToggle.IsChecked = await WindowsFeaturesService.IsFeatureEnabledAsync("Containers-DisposableClientVM");
            DirectPlayToggle.IsChecked = await WindowsFeaturesService.IsFeatureEnabledAsync("DirectPlay");
            HyperVToggle.IsChecked = await WindowsFeaturesService.IsFeatureEnabledAsync("Microsoft-Hyper-V-All");
        }
        catch
        {
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    private async void NetFx3Toggle_Toggled(object sender, RoutedEventArgs e)
        => await SetFeatureAsync(NetFx3Toggle, "NetFx3");

    private async void WslToggle_Toggled(object sender, RoutedEventArgs e)
        => await SetFeatureAsync(WslToggle, "Microsoft-Windows-Subsystem-Linux");

    private async void SandboxToggle_Toggled(object sender, RoutedEventArgs e)
        => await SetFeatureAsync(SandboxToggle, "Containers-DisposableClientVM");

    private async void DirectPlayToggle_Toggled(object sender, RoutedEventArgs e)
        => await SetFeatureAsync(DirectPlayToggle, "DirectPlay");

    private async void HyperVToggle_Toggled(object sender, RoutedEventArgs e)
        => await SetFeatureAsync(HyperVToggle, "Microsoft-Hyper-V-All");

    private async Task SetFeatureAsync(ToggleSwitch toggle, string featureName)
    {
        if (!IsLoaded || _isProgrammaticChange)
            return;

        bool enable = toggle.IsChecked == true;
        toggle.IsEnabled = false;

        try
        {
            // Установка/удаление компонентов Windows идёт через DISM и может занимать
            // несколько минут — показываем «бегущую» полосу с возможностью отмены.
            var (ok, error, cancelled) = await ProgressWindow.RunIndeterminateAsync(
                Window.GetWindow(this),
                LocalizationService.T(enable ? "Включение компонента Windows" : "Отключение компонента Windows"),
                LocalizationService.TF("{0}: {1}… это может занять несколько минут.",
                    featureName,
                    LocalizationService.T(enable ? "включение" : "отключение")),
                async (_, token) =>
                {
                    await WindowsFeaturesService.SetFeatureEnabledAsync(featureName, enable, token);
                });

            if (ok)
            {
                await Dialogs.ShowInfoAsync(enable ? "Компонент включён" : "Компонент отключён",
                    LocalizationService.TF("Компонент {0} успешно {1}. Для полного применения может потребоваться перезагрузка Windows.",
                        featureName,
                        LocalizationService.T(enable ? "включён" : "отключён")));
            }
            else if (error != null)
            {
                _isProgrammaticChange = true;
                try
                {
                    toggle.IsChecked = !enable;
                }
                finally
                {
                    _isProgrammaticChange = false;
                }

                await Dialogs.ShowErrorAsync("Ошибка", LocalizationService.T("Не удалось изменить компонент: ") + error);
            }
            else if (cancelled)
            {
                // Пользователь отменил операцию — возвращаем тумблер в исходное состояние.
                _isProgrammaticChange = true;
                try
                {
                    toggle.IsChecked = !enable;
                }
                finally
                {
                    _isProgrammaticChange = false;
                }
            }
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }
}
