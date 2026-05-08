using System;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class OptimizationPage : Page
{
    public OptimizationPage()
    {
        InitializeComponent();
        LoadCurrentSettings();
        GenerateLaunchArguments();
    }

    private void LoadCurrentSettings()
    {
        DisableFsoToggle.IsChecked = OptimizationService.GetFullScreenOptimizationsDisabled();
        DisableMpoToggle.IsChecked = OptimizationService.GetMpoDisabled();
        DisableGameDvrToggle.IsChecked = OptimizationService.GetGameDvrDisabled();
        DisableNagleToggle.IsChecked = OptimizationService.GetNagleDisabled();
        DisableAutoTuningToggle.IsChecked = OptimizationService.GetAutoTuningDisabled();
        DisableVisualEffectsToggle.IsChecked = OptimizationService.GetVisualEffectsDisabled();
        DisableWindowsAnimationToggle.IsChecked = OptimizationService.GetWindowAnimationsDisabled();
        DisableBackgroundAppsToggle.IsChecked = OptimizationService.GetBackgroundAppsDisabled();
    }

    private void GenerateLaunchArguments()
    {
        try
        {
            int coreCount = GetPhysicalCoreCount();
            int refreshRate = GetMaxMonitorRefreshRate();

            string launchArgs = $"-console +rate 786432 -freq {refreshRate} -refresh {refreshRate} -threads {coreCount} -allow_third_party_software";

            LaunchArgsBox.Text = launchArgs;
        }
        catch
        {
            LaunchArgsBox.Text = "-console +rate 786432 -freq 240 -refresh 240 -threads 8 -allow_third_party_software";
        }
    }

    private int GetPhysicalCoreCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            foreach (ManagementObject processor in searcher.Get())
            {
                var coresObj = processor["NumberOfCores"];
                if (coresObj != null && int.TryParse(coresObj.ToString(), out int cores) && cores > 0)
                {
                    return cores;
                }
            }

            return Environment.ProcessorCount;
        }
        catch
        {
            return 8;
        }
    }

    private int GetMaxMonitorRefreshRate()
    {
        try
        {
            int maxRefreshRate = 0;

            
            using var searcher = new ManagementObjectSearcher("SELECT CurrentRefreshRate FROM Win32_VideoController");
            foreach (ManagementObject videoController in searcher.Get())
            {
                var refreshRateObj = videoController["CurrentRefreshRate"];
                if (refreshRateObj != null && int.TryParse(refreshRateObj.ToString(), out int refreshRate) && refreshRate > 0)
                {
                    if (refreshRate > maxRefreshRate)
                    {
                        maxRefreshRate = refreshRate;
                    }
                }
            }

           
            if (maxRefreshRate > 0)
            {
                return maxRefreshRate;
            }

            using var displaySearcher = new ManagementObjectSearcher("SELECT ScreenWidth, ScreenHeight FROM Win32_DesktopMonitor");
            foreach (ManagementObject monitor in displaySearcher.Get())
            {
                return 144;
            }

            return 120;
        }
        catch
        {
            return 120;
        }
    }

    private async void DisableFsoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetFullScreenOptimizationsAsync(DisableFsoToggle.IsChecked ?? false);
            ShowNotification("Full Screen Optimizations " + (DisableFsoToggle.IsChecked ?? false ? "отключены" : "включены"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableFsoToggle.IsChecked = !DisableFsoToggle.IsChecked;
        }
    }

    private async void DisableMpoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetMpoDisabledAsync(DisableMpoToggle.IsChecked ?? false);
            ShowNotification("Multi-Plane Overlay " + (DisableMpoToggle.IsChecked ?? false ? "отключен" : "включен"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableMpoToggle.IsChecked = !DisableMpoToggle.IsChecked;
        }
    }

    private async void DisableGameDvrToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetGameDvrDisabledAsync(DisableGameDvrToggle.IsChecked ?? false);
            ShowNotification("Game DVR " + (DisableGameDvrToggle.IsChecked ?? false ? "отключен" : "включен"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableGameDvrToggle.IsChecked = !DisableGameDvrToggle.IsChecked;
        }
    }

    private async void HighPriorityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            if (HighPriorityToggle.IsChecked ?? false)
            {
                await OptimizationService.SetProcessPriorityAsync("cs2.exe", true);
                ShowNotification("Высокий приоритет для CS2 установлен (требует перезагрузки приложения)");
            }
            else
            {
                await OptimizationService.SetProcessPriorityAsync("cs2.exe", false);
                ShowNotification("Приоритет CS2 восстановлен");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            HighPriorityToggle.IsChecked = !HighPriorityToggle.IsChecked;
        }
    }

    // Network Settings
    private async void DisableNagleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetNagleDisabledAsync(DisableNagleToggle.IsChecked ?? false);
            ShowNotification("Алгоритм Nagle " + (DisableNagleToggle.IsChecked ?? false ? "отключен" : "включен"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableNagleToggle.IsChecked = !DisableNagleToggle.IsChecked;
        }
    }

    private async void DisableAutoTuningToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetAutoTuningDisabledAsync(DisableAutoTuningToggle.IsChecked ?? false);
            ShowNotification("TCP Auto-Tuning " + (DisableAutoTuningToggle.IsChecked ?? false ? "отключен" : "включен"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableAutoTuningToggle.IsChecked = !DisableAutoTuningToggle.IsChecked;
        }
    }

    private async void PrioritizeBurstToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetNetworkQoSAsync(PrioritizeBurstToggle.IsChecked ?? false);
            ShowNotification("QoS приоритизация " + (PrioritizeBurstToggle.IsChecked ?? false ? "включена" : "отключена"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            PrioritizeBurstToggle.IsChecked = !PrioritizeBurstToggle.IsChecked;
        }
    }

    // System Performance
    private async void DisableVisualEffectsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetVisualEffectsDisabledAsync(DisableVisualEffectsToggle.IsChecked ?? false);
            ShowNotification("Визуальные эффекты " + (DisableVisualEffectsToggle.IsChecked ?? false ? "отключены" : "включены"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableVisualEffectsToggle.IsChecked = !DisableVisualEffectsToggle.IsChecked;
        }
    }

    private async void DisableWindowsAnimationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetWindowAnimationsDisabledAsync(DisableWindowsAnimationToggle.IsChecked ?? false);
            ShowNotification("Анимации окон " + (DisableWindowsAnimationToggle.IsChecked ?? false ? "отключены" : "включены"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableWindowsAnimationToggle.IsChecked = !DisableWindowsAnimationToggle.IsChecked;
        }
    }

    private async void DisableBackgroundAppsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded) return;

        try
        {
            await OptimizationService.SetBackgroundAppsDisabledAsync(DisableBackgroundAppsToggle.IsChecked ?? false);
            ShowNotification("Фоновые приложения " + (DisableBackgroundAppsToggle.IsChecked ?? false ? "отключены" : "включены"));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            DisableBackgroundAppsToggle.IsChecked = !DisableBackgroundAppsToggle.IsChecked;
        }
    }

    // Maintenance
    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show("Это может занять некоторое время. Продолжить?", "Очистка кэша",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await OptimizationService.ClearGameCacheAsync();
                ShowNotification("Кэш шейдеров успешно очищен!");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при очистке кэша: {ex.Message}", "Ошибка", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ClearTempFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show("Это может занять некоторое время. Продолжить?", "Очистка временных файлов",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await OptimizationService.ClearTempFilesAsync();
                ShowNotification("Временные файлы успешно очищены!");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка при очистке файлов: {ex.Message}", "Ошибка", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Launch Arguments
    private void CopyLaunchArgs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(LaunchArgsBox.Text);
            ShowNotification("Параметры скопированы в буфер обмена!");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowNotification(string message)
    {
        MessageBox.Show(message, "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}