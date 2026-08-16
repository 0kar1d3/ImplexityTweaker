using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class FunSettingsPage : Page
{
    // Ключ реестра, где хранится название процессора, отображаемое в диспетчере задач.
    private const string CpuKeyPath = @"HARDWARE\DESCRIPTION\System\CentralProcessor\0";
    private const string CpuValueName = "ProcessorNameString";

    public FunSettingsPage()
    {
        InitializeComponent();
        LoadCurrentValues();
    }

    private void LoadCurrentValues()
    {
        // Текущее название процессора.
        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(CpuKeyPath, false);
            var cpuName = cpuKey?.GetValue(CpuValueName) as string;
            CurrentCpuText.Text = string.IsNullOrWhiteSpace(cpuName)
                ? "Не удалось определить название процессора."
                : $"Текущее название: {cpuName}";
        }
        catch
        {
            CurrentCpuText.Text = "Не удалось прочитать название процессора.";
        }
    }

    private async void ApplyCpu_Click(object sender, RoutedEventArgs e)
    {
        var name = CpuNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            await Dialogs.ShowErrorAsync("Ошибка", "Введите название процессора.");
            return;
        }

        var ok = await TryWriteRegistryAsync(CpuKeyPath, CpuValueName, name);
        if (ok)
        {
            CurrentCpuText.Text = $"Текущее название: {name}";
            await Dialogs.ShowInfoAsync("Готово",
                "Название процессора изменено. Оно вступит в силу сразу и сбросится после перезагрузки ПК.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось изменить название процессора. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    private async void ResetCpu_Click(object sender, RoutedEventArgs e)
    {
        var ok = await TryResetRegistryAsync(CpuKeyPath, CpuValueName);
        if (ok)
        {
            CpuNameBox.Text = string.Empty;
            LoadCurrentValues();
            await Dialogs.ShowInfoAsync("Готово", "Название процессора сброшено.");
        }
        else
        {
            await Dialogs.ShowErrorAsync("Ошибка",
                "Не удалось сбросить название процессора. Убедитесь, что приложение запущено от имени администратора.");
        }
    }

    // Запись строкового значения в реестр (HKLM). Требует прав администратора.
    private static Task<bool> TryWriteRegistryAsync(string keyPath, string valueName, string value)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.CreateSubKey(keyPath, true);
                key?.SetValue(valueName, value, RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    // Удаление значения из реестра (HKLM), чтобы вернуть исходное название.
    private static Task<bool> TryResetRegistryAsync(string keyPath, string valueName)
    {
        return Task.Run(() =>
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key == null)
                    return true;

                if (key.GetValue(valueName) != null)
                    key.DeleteValue(valueName, false);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}
