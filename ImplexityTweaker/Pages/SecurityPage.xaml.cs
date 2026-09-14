using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;
using Wpf.Ui.Controls;

namespace ImplexityTweaker.Pages;

public partial class SecurityPage : Page
{

    /// <summary>
    /// Значения, которые приложение само записало в тумблеры при загрузке состояния.
    /// Событие Checked/Unchecked от такой установки приходит позже (когда блок
    /// подавления уже закончился), поэтому его нельзя считать действием пользователя.
    /// </summary>
    private readonly Dictionary<ToggleSwitch, bool> _appliedState = new();

    public SecurityPage()
    {
        InitializeComponent();
        Loaded += SecurityPage_Loaded;
    }

    /// <summary>Записывает состояние программно, чтобы отличить его от переключения пользователем.</summary>
    private void SetToggleState(ToggleSwitch toggle, bool value)
    {
        _appliedState[toggle] = value;
        toggle.IsChecked = value;
    }

    /// <summary>True, если событие вызвано программной установкой, а не пользователем.</summary>
    private bool IsProgrammaticEvent(ToggleSwitch toggle, bool value)
        => _appliedState.TryGetValue(toggle, out var applied) && applied == value;

    private async void SecurityPage_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetToggleState(DefenderToggle, await SecurityService.GetDefenderFullyDisabledAsync());
            SetToggleState(SmartScreenToggle, SecurityService.GetSmartScreenDisabled());
            SetToggleState(FirewallToggle, SecurityService.GetFirewallDisabled());
            SetToggleState(UacToggle, SecurityService.GetUacDisabled());
        }
        catch
        {
            // состояние не критично для работы страницы
        }
    }

    private void OpenSecurityCenter_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private async void DefenderToggle_Toggled(object sender, RoutedEventArgs e)
        => await ApplyToggleAsync(DefenderToggle, "Windows Defender",
            "Отключение Windows Defender полностью уберёт антивирус и приложение «Безопасность Windows».",
            SecurityService.SetDefenderFullyDisabledAsync);

    private async void SmartScreenToggle_Toggled(object sender, RoutedEventArgs e)
        => await ApplyToggleAsync(SmartScreenToggle, "SmartScreen",
            "Отключение SmartScreen снижает защиту от вредоносных файлов из интернета.",
            SecurityService.SetSmartScreenDisabledAsync);

    private async void FirewallToggle_Toggled(object sender, RoutedEventArgs e)
        => await ApplyToggleAsync(FirewallToggle, "Брандмауэр Windows",
            "Отключение брандмауэра сделает сетевой трафик нефильтруемым.",
            SecurityService.SetFirewallDisabledAsync);

    private async void UacToggle_Toggled(object sender, RoutedEventArgs e)
        => await ApplyToggleAsync(UacToggle, "Контроль учётных записей (UAC)",
            "Отключение UAC снизит безопасность системы и уберёт запросы повышения прав.",
            SecurityService.SetUacDisabledAsync);

    private async Task ApplyToggleAsync(ToggleSwitch toggle, string name, string warning, Func<bool, Task> action)
    {
        if (!IsLoaded)
            return;

        var enable = toggle.IsChecked == true;

        // Событие пришло от программной установки состояния (загрузка страницы) — не действие пользователя.
        if (IsProgrammaticEvent(toggle, enable))
            return;

        var confirm = await Dialogs.ShowConfirmAsync(name,
            LocalizationService.T(warning) + "\n\n" + LocalizationService.T(enable ? "Отключить?" : "Включить?"));
        if (!confirm)
        {
            _appliedState[toggle] = !enable;
            toggle.IsChecked = !enable;
            return;
        }

        toggle.IsEnabled = false;
        try
        {
            await Task.Run(async () => await action(enable));
            _appliedState[toggle] = enable;

            var msg = LocalizationService.T(name) + " " + LocalizationService.T(enable ? "отключён" : "включён") + ".";
            if (enable)
                msg += LocalizationService.T("\nДля полного применения может потребоваться перезагрузка.");
            await Dialogs.ShowInfoAsync("Готово", msg);
        }
        catch (Exception ex)
        {
            _appliedState[toggle] = !enable;
            toggle.IsChecked = !enable;

            await Dialogs.ShowErrorAsync("Ошибка", LocalizationService.T("Не удалось изменить параметр: ") + ex.Message);
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    // ===================== Целостность памяти (HVCI) =====================

    private void MemoryIntegrityToggle_Loaded(object sender, RoutedEventArgs e)
    {
        SetToggleState(MemoryIntegrityToggle, SecurityPostureService.GetMemoryIntegrityEnabled());
    }

    private async void MemoryIntegrityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        var enable = MemoryIntegrityToggle.IsChecked == true;

        // Программная установка состояния при загрузке страницы — не действие пользователя.
        if (IsProgrammaticEvent(MemoryIntegrityToggle, enable))
            return;

        try
        {
            await SecurityPostureService.SetMemoryIntegrityAsync(enable);
            _appliedState[MemoryIntegrityToggle] = enable;
            await Dialogs.ShowInfoAsync("Готово",
                "Изменение «Целостности памяти» применится после перезагрузки Windows.");
        }
        catch (Exception ex)
        {
            _appliedState[MemoryIntegrityToggle] = !enable;
            MemoryIntegrityToggle.IsChecked = !enable;

            await Dialogs.ShowErrorAsync("Ошибка", ex.Message);
        }
    }
}
