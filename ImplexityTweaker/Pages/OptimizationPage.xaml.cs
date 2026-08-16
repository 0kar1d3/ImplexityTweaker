using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class OptimizationPage : Page
{
    private bool _isProgrammaticChange = false;

    public OptimizationPage()
    {
        InitializeComponent();
        LoadCurrentSettings();
        GenerateLaunchArguments();
    }

    private void LoadCurrentSettings()
    {
        var s = AppSettings.Load();
        DisableFsoToggle.IsChecked = s.DisableFso ?? OptimizationService.GetFullScreenOptimizationsDisabled();
        DisableMpoToggle.IsChecked = s.DisableMpo ?? OptimizationService.GetMpoDisabled();
        DisableGameDvrToggle.IsChecked = s.DisableGameDvr ?? OptimizationService.GetGameDvrDisabled();
        DisableNagleToggle.IsChecked = s.DisableNagle ?? OptimizationService.GetNagleDisabled();
        DisableAutoTuningToggle.IsChecked = s.DisableAutoTuning ?? OptimizationService.GetAutoTuningDisabled();
        DisableVisualEffectsToggle.IsChecked = s.DisableVisualEffects ?? OptimizationService.GetVisualEffectsDisabled();
        DisableWindowsAnimationToggle.IsChecked = s.DisableWindowsAnimation ?? OptimizationService.GetWindowAnimationsDisabled();
        HighPriorityToggle.IsChecked = s.HighPriority ?? false;
        PrioritizeBurstToggle.IsChecked = s.PrioritizeBurst ?? false;

        if (s.ProcessorState is int ps && ps >= (int)ProcessorStateSlider.Minimum && ps <= (int)ProcessorStateSlider.Maximum)
        {
            _isProgrammaticChange = true;
            ProcessorStateSlider.Value = ps;
            _isProgrammaticChange = false;
        }
    }

    private void SaveToSettings()
    {
        var s = AppSettings.Load();
        s.DisableFso = DisableFsoToggle.IsChecked == true;
        s.DisableMpo = DisableMpoToggle.IsChecked == true;
        s.DisableGameDvr = DisableGameDvrToggle.IsChecked == true;
        s.DisableNagle = DisableNagleToggle.IsChecked == true;
        s.DisableAutoTuning = DisableAutoTuningToggle.IsChecked == true;
        s.DisableVisualEffects = DisableVisualEffectsToggle.IsChecked == true;
        s.DisableWindowsAnimation = DisableWindowsAnimationToggle.IsChecked == true;
        s.HighPriority = HighPriorityToggle.IsChecked == true;
        s.PrioritizeBurst = PrioritizeBurstToggle.IsChecked == true;
        s.ProcessorState = (int)ProcessorStateSlider.Value;
        s.Save();
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
                    return cores;
            }
            return Environment.ProcessorCount;
        }
        catch { return 8; }
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
                    if (refreshRate > maxRefreshRate) maxRefreshRate = refreshRate;
            }
            if (maxRefreshRate > 0) return maxRefreshRate;

            // Fallback: если WMI не вернул частоту, используем разумное значение по умолчанию.
            return 144;
        }
        catch { return 144; }
    }
        private void RevertToggleOnFailure(object toggleObj, Exception ex)
    {
        MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        if (toggleObj is Wpf.Ui.Controls.ToggleSwitch toggle)
        {
            _isProgrammaticChange = true;
            toggle.IsChecked = !toggle.IsChecked;
            _isProgrammaticChange = false;
        }
    }

    // Graphics
    private async void DisableFsoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetFullScreenOptimizationsAsync(DisableFsoToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Full Screen Optimizations " + (DisableFsoToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableFsoToggle, ex);
        }
    }

    private async void DisableMpoToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetMpoDisabledAsync(DisableMpoToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Multi-Plane Overlay " + (DisableMpoToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableMpoToggle, ex);
        }
    }

    private async void DisableGameDvrToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetGameDvrDisabledAsync(DisableGameDvrToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Game DVR " + (DisableGameDvrToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableGameDvrToggle, ex);
        }
    }

    private async void HighPriorityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            if (HighPriorityToggle.IsChecked ?? false)
            {
                await OptimizationService.SetProcessPriorityAsync("cs2.exe", true);
                ShowNotification("High priority for CS2 set (requires app restart)");
            }
            else
            {
                await OptimizationService.SetProcessPriorityAsync("cs2.exe", false);
                ShowNotification("CS2 priority restored");
            }
            SaveToSettings();
        }
                catch (Exception ex)
        {
            RevertToggleOnFailure(HighPriorityToggle, ex);
        }
    }

    // Network Settings
    private async void DisableNagleToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetNagleDisabledAsync(DisableNagleToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Nagle algorithm " + (DisableNagleToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableNagleToggle, ex);
        }
    }

    private async void DisableAutoTuningToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetAutoTuningDisabledAsync(DisableAutoTuningToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("TCP Auto-Tuning " + (DisableAutoTuningToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableAutoTuningToggle, ex);
        }
    }

    private async void PrioritizeBurstToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetNetworkQoSAsync(PrioritizeBurstToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("QoS prioritization " + (PrioritizeBurstToggle.IsChecked ?? false ? "enabled" : "disabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(PrioritizeBurstToggle, ex);
        }
    }

    // System Performance
    private async void DisableVisualEffectsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetVisualEffectsDisabledAsync(DisableVisualEffectsToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Visual effects " + (DisableVisualEffectsToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableVisualEffectsToggle, ex);
        }
    }

        private async void DisableWindowsAnimationToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetWindowAnimationsDisabledAsync(DisableWindowsAnimationToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("Window animations " + (DisableWindowsAnimationToggle.IsChecked ?? false ? "disabled" : "enabled"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableWindowsAnimationToggle, ex);
        }
    }

    // Processor Power State
    private async void ApplyProcessorState_Click(object sender, RoutedEventArgs e)
    {
        int value = (int)ProcessorStateSlider.Value;
        await SetMaxProcessorStateAsync(value);
    }

    private async void ApplyMin_Click(object sender, RoutedEventArgs e)
    {
        _isProgrammaticChange = true;
        ProcessorStateSlider.Value = 10;
        _isProgrammaticChange = false;
        int value = (int)ProcessorStateSlider.Value;
        await SetMaxProcessorStateAsync(value);
    }

    private async void ApplyMax_Click(object sender, RoutedEventArgs e)
    {
        _isProgrammaticChange = true;
        ProcessorStateSlider.Value = 100;
        _isProgrammaticChange = false;
        int value = (int)ProcessorStateSlider.Value;
        await SetMaxProcessorStateAsync(value);
    }

    private void ProcessorStateSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ProcessorStatePercentText != null)
            ProcessorStatePercentText.Text = $"{(int)e.NewValue}%";
    }

    private async void ProcessorStateSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        int value = (int)ProcessorStateSlider.Value;
        await SetMaxProcessorStateAsync(value);
    }

    private async Task SetMaxProcessorStateAsync(int value)
    {
        try
        {
            const string subGroup = "54533251-82be-4824-96c1-47b60b740d00";
            const string parameter = "bc5038f7-23e0-4960-96da-33abaf5935ec";
            string scheme = "SCHEME_CURRENT";

            await RunPowerCfgAsync($"/setacvalueindex {scheme} {subGroup} {parameter} {value}");
            await RunPowerCfgAsync($"/setdcvalueindex {scheme} {subGroup} {parameter} {value}");
            await RunPowerCfgAsync($"/setactive {scheme}");

            SaveToSettings();
            ShowNotification($"Максимальная частота процессора установлена на {value}%");
        }
        catch (Exception ex)
        {
            ShowNotification($"Ошибка установки частоты процессора: {ex.Message}");
        }
    }

    private async Task RunPowerCfgAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync();
                throw new Exception(error);
            }
        }
    }

    // Maintenance
    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show("This may take some time. Continue?", "Cache Cleanup",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await OptimizationService.ClearGameCacheAsync();
                ShowNotification("Shader cache cleared successfully!");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error clearing cache: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ClearTempFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show("This may take some time. Continue?", "Temp Cleanup",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await OptimizationService.ClearTempFilesAsync();
                ShowNotification("Temporary files cleared successfully!");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error clearing files: " + ex.Message, "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Launch Arguments
    private void CopyLaunchArgs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(LaunchArgsBox.Text);
            ShowNotification("Launch arguments copied to clipboard!");
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowNotification(string message)
    {
        MessageBox.Show(message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}