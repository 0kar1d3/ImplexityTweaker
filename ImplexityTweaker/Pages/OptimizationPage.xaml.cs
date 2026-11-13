using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class OptimizationPage : Page
{
    private bool _isProgrammaticChange = false;

    // Кэш настроек: загружаем файл один раз при открытии страницы, а запись
    // откладываем через таймер — переключение тумблеров не перезаписывает конфиг на каждый клик.
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;

    public OptimizationPage()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => FlushSettings();
        LoadCurrentSettings();
        _ = LoadPowerSchemeStateAsync();
        InitializeProcessTweaks();
        InitializeMouseAndInputTweaks();
        _ = LoadMsiDevicesAsync();
        // Параметры запуска CS2 считаются под конкретную систему — заполняем после настроек.
        GenerateLaunchArguments();
        Unloaded += OptimizationPage_Unloaded;
    }

    // Смена DNS-серверов переехала в раздел «Сеть и пинг».

    private void LoadCurrentSettings()
    {
        var s = _settings;
        DisableFsoToggle.IsChecked = s.DisableFso ?? OptimizationService.GetFullScreenOptimizationsDisabled();
        DisableMpoToggle.IsChecked = s.DisableMpo ?? OptimizationService.GetMpoDisabled();
        DisableGameDvrToggle.IsChecked = s.DisableGameDvr ?? OptimizationService.GetGameDvrDisabled();
        DisableNagleToggle.IsChecked = s.DisableNagle ?? NetworkLatencyService.GetTcpNoDelayEnabled();
        SystemResponsivenessToggle.IsChecked = s.DisableSystemResponsiveness ?? NetworkLatencyService.GetSystemResponsivenessEnabled();
        TcpAckFrequencyToggle.IsChecked = s.TcpAckFrequency ?? NetworkLatencyService.GetTcpAckFrequencyEnabled();
        GpuPriorityToggle.IsChecked = s.GpuPriority ?? NetworkLatencyService.GetGpuPriorityEnabled();
        DisableAutoTuningToggle.IsChecked = s.DisableAutoTuning ?? OptimizationService.GetAutoTuningDisabled();
        DisableVisualEffectsToggle.IsChecked = s.DisableVisualEffects ?? OptimizationService.GetVisualEffectsDisabled();
        DisableWindowsAnimationToggle.IsChecked = s.DisableWindowsAnimation ?? OptimizationService.GetWindowAnimationsDisabled();
        HighPriorityToggle.IsChecked = s.HighPriority ?? false;
        PrioritizeBurstToggle.IsChecked = s.PrioritizeBurst ?? false;

        if (s.ProcessorState is int ps && ps >= (int)ProcessorStateSlider.Minimum && ps <= (int)ProcessorStateSlider.Maximum)
        {
            _isProgrammaticChange = true;
            try
            {
                ProcessorStateSlider.Value = ps;
            }
            finally
            {
                _isProgrammaticChange = false;
            }
        }
    }

    private void SaveToSettings()
    {
        var s = _settings;
        s.DisableFso = DisableFsoToggle.IsChecked == true;
        s.DisableMpo = DisableMpoToggle.IsChecked == true;
        s.DisableGameDvr = DisableGameDvrToggle.IsChecked == true;
        s.DisableNagle = DisableNagleToggle.IsChecked == true;
        s.DisableSystemResponsiveness = SystemResponsivenessToggle.IsChecked == true;
        s.TcpAckFrequency = TcpAckFrequencyToggle.IsChecked == true;
        s.GpuPriority = GpuPriorityToggle.IsChecked == true;
        s.DisableAutoTuning = DisableAutoTuningToggle.IsChecked == true;
        s.DisableVisualEffects = DisableVisualEffectsToggle.IsChecked == true;
        s.DisableWindowsAnimation = DisableWindowsAnimationToggle.IsChecked == true;
        s.HighPriority = HighPriorityToggle.IsChecked == true;
        s.PrioritizeBurst = PrioritizeBurstToggle.IsChecked == true;
        s.UltimatePerformance = UltimatePerformanceToggle.IsChecked == true;
        s.ProcessorState = (int)ProcessorStateSlider.Value;

        // Откладываем запись конфига: при быстром переключении нескольких тумблеров
        // файл записывается один раз после паузы в изменениях (~400 мс).
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void FlushSettings()
    {
        _saveTimer.Stop();
        _settings.Save();
    }

    private void OptimizationPage_Unloaded(object sender, RoutedEventArgs e)
    {
        // Если пользователь ушёл со страницы до срабатывания таймера — сохраняем сразу.
        if (_saveTimer.IsEnabled)
            FlushSettings();
    }

    private async Task LoadPowerSchemeStateAsync()
    {
        bool enabled = await OptimizationService.GetUltimatePerformanceEnabledAsync();
        bool? saved = _settings.UltimatePerformance;

        _isProgrammaticChange = true;
        try
        {
            UltimatePerformanceToggle.IsChecked = saved ?? enabled;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    private async void GenerateLaunchArguments()
    {
        string launchArgs;
        try
        {
            // Тяжёлые WMI-запросы выполняем в фоне, чтобы не фризить UI-поток.
            int coreCount = await Task.Run(GetPhysicalCoreCount);
            int refreshRate = await Task.Run(GetMaxMonitorRefreshRate);
            launchArgs = $"-console +rate 786432 -freq {refreshRate} -refresh {refreshRate} -threads {coreCount} -allow_third_party_software";
        }
        catch
        {
            // WMI недоступен или вернул ошибку — используем безопасные значения по умолчанию.
            launchArgs = "-console +rate 786432 -freq 240 -refresh 240 -threads 8 -allow_third_party_software";
        }

        // Показываем рассчитанные параметры, если пользователь ещё не задал свои.
        _generatedCs2Args = launchArgs;
        if (string.IsNullOrWhiteSpace(GameArgsBox.Text))
            GameArgsBox.Text = launchArgs;
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
            try
            {
                toggle.IsChecked = !toggle.IsChecked;
            }
            finally
            {
                _isProgrammaticChange = false;
            }
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
        if (!this.IsLoaded || _isProgrammaticChange || _gameInit) return;

        var processName = ProcessNameOf(HighPriorityProcessCombo);
        try
        {
            await OptimizationService.SetProcessPriorityAsync(processName, HighPriorityToggle.IsChecked ?? false);
            SaveProcessTweaks();

            ShowNotification(HighPriorityToggle.IsChecked == true
                ? LocalizationService.TF("Высокий приоритет включён для {0}.", processName)
                : LocalizationService.TF("Приоритет для {0} возвращён к обычному.", processName));
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
            await NetworkLatencyService.SetTcpNoDelayAsync(DisableNagleToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification(DisableNagleToggle.IsChecked ?? false
                ? LocalizationService.T("Алгоритм Nagle отключён (TCPNoDelay = 1).")
                : LocalizationService.T("Алгоритм Nagle включён обратно (TCPNoDelay = 0)."));
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
            ShowNotification(DisableAutoTuningToggle.IsChecked ?? false
                ? LocalizationService.T("TCP Auto-Tuning отключён.")
                : LocalizationService.T("TCP Auto-Tuning включён обратно."));
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
            ShowNotification(PrioritizeBurstToggle.IsChecked ?? false
                ? LocalizationService.T("Приоритезация трафика (QoS) включена.")
                : LocalizationService.T("Приоритезация трафика (QoS) выключена."));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(PrioritizeBurstToggle, ex);
        }
    }

    // Latency / TCP tweaks
    private async void SystemResponsivenessToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await NetworkLatencyService.SetSystemResponsivenessAsync(SystemResponsivenessToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("SystemResponsiveness " + (SystemResponsivenessToggle.IsChecked ?? false ? "set to 0 (disabled)" : "restored to 20"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(SystemResponsivenessToggle, ex);
        }
    }

    private async void TcpAckFrequencyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await NetworkLatencyService.SetTcpAckFrequencyAsync(TcpAckFrequencyToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification(TcpAckFrequencyToggle.IsChecked ?? false
                ? LocalizationService.T("Мгновенная отправка ACK включена (TcpAckFrequency = 1).")
                : LocalizationService.T("Отправка ACK возвращена к обычной (TcpAckFrequency = 2)."));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(TcpAckFrequencyToggle, ex);
        }
    }

    private async void GpuPriorityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await NetworkLatencyService.SetGpuPriorityAsync(GpuPriorityToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification("GPU Priority " + (GpuPriorityToggle.IsChecked ?? false ? "set to 8 for games" : "restored"));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(GpuPriorityToggle, ex);
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

    private async void UltimatePerformanceToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _isProgrammaticChange) return;
        try
        {
            await OptimizationService.SetUltimatePerformanceAsync(UltimatePerformanceToggle.IsChecked ?? false);
            SaveToSettings();
            ShowNotification(LocalizationService.T("Режим электропитания: ") + (UltimatePerformanceToggle.IsChecked ?? false
                ? LocalizationService.T("Ultra Performance активирован")
                : LocalizationService.T("Высокая производительность")));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(UltimatePerformanceToggle, ex);
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
        try
        {
            ProcessorStateSlider.Value = 10;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
        await SetMinProcessorStateAsync(10);
    }

    private async Task SetMinProcessorStateAsync(int value)
    {
        try
        {
            // PROCTHROTTLEMIN — минимальная частота процессора.
            const string subGroup = "54533251-82be-4824-96c1-47b60b740d00";
            const string minParameter = "893dee8e-2bef-41e0-89c6-b55d0929964c";

            await RunPowerCfgAsync($"/setacvalueindex SCHEME_CURRENT {subGroup} {minParameter} {value}");
            await RunPowerCfgAsync($"/setdcvalueindex SCHEME_CURRENT {subGroup} {minParameter} {value}");
            await RunPowerCfgAsync("/setactive SCHEME_CURRENT");

            SaveToSettings();
            ShowNotification(LocalizationService.TF("Минимальная частота процессора установлена на {0}%", value));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.TF("Ошибка установки минимальной частоты процессора: {0}", ex.Message));
        }
    }

    private async void ApplyMax_Click(object sender, RoutedEventArgs e)
    {
        _isProgrammaticChange = true;
        try
        {
            ProcessorStateSlider.Value = 100;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
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
            ShowNotification(LocalizationService.TF("Максимальная частота процессора установлена на {0}%", value));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.TF("Ошибка установки частоты процессора: {0}", ex.Message));
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
            // Читаем потоки ДО ожидания выхода процесса: если не дренировать
            // буферы stdout/stderr, их заполнение приведёт к взаимной блокировке (deadlock).
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            string stdout = await stdoutTask;
            string error = await stderrTask;

            if (process.ExitCode != 0)
                throw new Exception(string.IsNullOrWhiteSpace(error) ? stdout : error);
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

    private async void CleanRam_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = await Dialogs.ShowConfirmAsync("Очистка памяти",
                "Сбросить рабочие наборы всех процессов текущего пользователя? Активные окна могут на мгновение «подвиснуть».");
            if (!confirm)
                return;

            long freedMb = await RamCleanerService.CleanAsync();
            ShowNotification(LocalizationService.TF("Очистка памяти завершена. Освобождено: {0} МБ", freedMb.ToString("N0")));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка очистки памяти: ") + ex.Message);
        }
    }

    private async void ClearShaderCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = await Dialogs.ShowConfirmAsync("Очистка кэша шейдеров GPU",
                "Удалить кэш шейдеров NVIDIA (DXCache, GLCache), AMD (DxCache) и DirectX (D3DSCache)? Заблокированные файлы будут пропущены.");
            if (!confirm)
                return;

            var result = await ShaderCacheService.ClearAsync();
            ShowNotification(LocalizationService.TF("Кэш шейдеров GPU очищен. Удалено файлов: {0}, освобождено: {1} МБ", result.DeletedFiles, result.FreedMb.ToString("N0")));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка очистки кэша шейдеров: ") + ex.Message);
        }
    }

    private async void ClearDnsCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = await Dialogs.ShowConfirmAsync("Очистка DNS кэша",
                "Сбросить кэш DNS Windows? Это безопасно и не требует перезагрузки.");
            if (!confirm)
                return;

            await OptimizationService.ClearDnsCacheAsync();
            ShowNotification(LocalizationService.T("DNS кэш успешно очищен."));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка очистки DNS кэша: ") + ex.Message);
        }
    }

    private async void ClearThumbnailCache_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = await Dialogs.ShowConfirmAsync("Очистка кэша миниатюр",
                "Удалить закешированные миниатюры файлов и папок? Значки пересоздадутся автоматически.");
            if (!confirm)
                return;

            await OptimizationService.ClearThumbnailCacheAsync();
            ShowNotification(LocalizationService.T("Кэш миниатюр успешно очищен."));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка очистки кэша миниатюр: ") + ex.Message);
        }
    }

    // Launch Arguments
    private void CopyLaunchArgs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(GameArgsBox.Text);
            ShowNotification(LocalizationService.T("Параметры запуска скопированы в буфер обмена."));
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ===================== Твики процессов =====================

    private bool _gameInit;
    private string? _generatedCs2Args;

    private void InitializeProcessTweaks()
    {
        var tweaks = ProcessTweaksStore.Load();

        var running = new List<string>();
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try { running.Add(process.ProcessName + ".exe"); } catch { /* системный процесс */ }
                finally { process.Dispose(); }
            }
        }
        catch
        {
            // список процессов недоступен — останется значение по умолчанию
        }

        var names = running.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        FillProcessCombo(HighPriorityProcessCombo, names, tweaks.HighPriorityProcess);
        FillProcessCombo(AffinityProcessCombo, names, tweaks.AffinityProcess);
        FillProcessCombo(AutoPriorityProcessCombo, names, tweaks.AutoPriorityProcess);

        // Параметры запуска CS2: свои значения пользователя приоритетнее расчётных.
        GameArgsBox.Text = string.IsNullOrWhiteSpace(tweaks.LaunchArgs)
            ? _generatedCs2Args ?? string.Empty
            : tweaks.LaunchArgs;

        _gameInit = true;
        try
        {
            HighPriorityToggle.IsChecked = tweaks.HighPriorityEnabled;
            AffinityToggle.IsChecked = tweaks.AffinityEnabled;
            AutoPriorityToggle.IsChecked = tweaks.AutoPriorityEnabled;

            AutoRamThresholdCombo.Items.Clear();
            int[] thresholds = [70, 75, 80, 85, 90];
            foreach (var th in thresholds)
                AutoRamThresholdCombo.Items.Add(new ThresholdItem(th));

            var currentTh = tweaks.AutoRamThresholdPercent is >= 70 and <= 95 ? tweaks.AutoRamThresholdPercent : 80;
            AutoRamThresholdCombo.SelectedItem = AutoRamThresholdCombo.Items.Cast<ThresholdItem>()
                .FirstOrDefault(x => x.Percent == currentTh) ?? AutoRamThresholdCombo.Items[2];

            AutoRamThresholdToggle.IsChecked = tweaks.AutoRamThresholdEnabled;
            AutoRamGameLaunchToggle.IsChecked = tweaks.AutoRamOnGameLaunchEnabled;
            AutoRamWhitelistBox.Text = string.IsNullOrWhiteSpace(tweaks.AutoRamGameWhitelist)
                ? "cs2.exe, dota2.exe, valorant.exe, r5apex.exe"
                : tweaks.AutoRamGameWhitelist;

            TrayAutostartToggle.IsChecked = TrayService.GetAutostartEnabled();
        }
        finally
        {
            _gameInit = false;
        }

        ProcessPriorityWatcher.Refresh();
        RefreshTrayStatusBanner();
        _ = LoadGameGlobalsAsync();
    }

    /// <summary>Заполняет список процессов, оставляя возможность ввести имя вручную.</summary>
    private static void FillProcessCombo(ComboBox combo, IReadOnlyList<string> names, string? current)
    {
        combo.Items.Clear();

        var selected = string.IsNullOrWhiteSpace(current) ? "cs2.exe" : current!;
        combo.Items.Add(selected);

        foreach (var name in names)
        {
            if (!string.Equals(name, selected, StringComparison.OrdinalIgnoreCase))
                combo.Items.Add(name);
        }

        combo.Text = selected;
    }

    /// <summary>Имя процесса из выпадающего списка (всегда с расширением .exe).</summary>
    private static string ProcessNameOf(ComboBox combo)
    {
        var name = combo.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
            name = "cs2.exe";

        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name += ".exe";

        combo.Text = name;
        return name;
    }

    /// <summary>Сохраняет привязанные к процессам настройки.</summary>
    private void SaveProcessTweaks()
    {
        var tweaks = ProcessTweaksStore.Load();
        tweaks.LaunchArgs = GameArgsBox.Text ?? string.Empty;
        tweaks.HighPriorityProcess = ProcessNameOf(HighPriorityProcessCombo);
        tweaks.HighPriorityEnabled = HighPriorityToggle.IsChecked == true;
        tweaks.AffinityProcess = ProcessNameOf(AffinityProcessCombo);
        tweaks.AffinityEnabled = AffinityToggle.IsChecked == true;
        tweaks.AutoPriorityProcess = ProcessNameOf(AutoPriorityProcessCombo);
        tweaks.AutoPriorityEnabled = AutoPriorityToggle.IsChecked == true;
        tweaks.AutoRamThresholdEnabled = AutoRamThresholdToggle.IsChecked == true;
        tweaks.AutoRamThresholdPercent = (AutoRamThresholdCombo.SelectedItem as ThresholdItem)?.Percent ?? 80;
        tweaks.AutoRamOnGameLaunchEnabled = AutoRamGameLaunchToggle.IsChecked == true;
        tweaks.AutoRamGameWhitelist = AutoRamWhitelistBox.Text?.Trim() ?? string.Empty;
        ProcessTweaksStore.Save(tweaks);
    }

    private void SaveGameProfile_Click(object sender, RoutedEventArgs e)
    {
        SaveProcessTweaks();
        ShowNotification(LocalizationService.T("Параметры сохранены."));
    }

    private void AffinityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _gameInit)
            return;

        bool wasTrayActive = TrayService.HasActiveBackgroundFeatures();
        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh(notifyTray: true);
        RefreshTrayStatusBanner();

        var msg = AffinityToggle.IsChecked == true
            ? LocalizationService.TF("«Только физические ядра» включено для {0}.", ProcessNameOf(AffinityProcessCombo))
            : LocalizationService.T("Ограничение по ядрам выключено.");

        if (!wasTrayActive && TrayService.HasActiveBackgroundFeatures())
            msg += "\n\n" + LocalizationService.T("Активирован фоновый режим в системном трее: при закрытии окна Implexity Tweaker продолжит следить за процессами.");

        ShowNotification(msg);
    }

    private void AutoPriorityToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!this.IsLoaded || _gameInit)
            return;

        bool wasTrayActive = TrayService.HasActiveBackgroundFeatures();
        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh(notifyTray: true);
        RefreshTrayStatusBanner();

        var msg = AutoPriorityToggle.IsChecked == true
            ? LocalizationService.TF("Авто-приоритет включён для {0}.", ProcessNameOf(AutoPriorityProcessCombo))
            : LocalizationService.T("Авто-приоритет выключен.");

        if (!wasTrayActive && TrayService.HasActiveBackgroundFeatures())
            msg += "\n\n" + LocalizationService.T("Активирован фоновый режим в системном трее: при закрытии окна Implexity Tweaker продолжит следить за процессами.");

        ShowNotification(msg);
    }

    // ===================== Автоочистка ОЗУ и Системный трей =====================

    private sealed record ThresholdItem(int Percent)
    {
        public override string ToString() => LocalizationService.TF("{0}% ОЗУ", Percent);
    }

    private void RefreshTrayStatusBanner()
    {
        var active = TrayService.HasActiveBackgroundFeatures();
        if (active)
        {
            var names = string.Join(", ", TrayService.GetActiveFeatureNames());
            TrayStatusTitle.Text = LocalizationService.T("Фоновый режим в трее активен");
            TrayStatusDesc.Text = LocalizationService.TF("Включено: {0}. При закрытии окна твикер свернётся в системный трей.", names);
            TrayAutostartToggle.IsEnabled = true;
        }
        else
        {
            TrayStatusTitle.Text = LocalizationService.T("Фоновый режим в трее выключен");
            TrayStatusDesc.Text = LocalizationService.T("Ни одна фоновая функция не включена — при закрытии окна программа завершается полностью.");
            TrayAutostartToggle.IsEnabled = false;
            _gameInit = true;
            try { TrayAutostartToggle.IsChecked = false; } finally { _gameInit = false; }
        }
    }

    private void TrayAutostartToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _gameInit)
            return;

        if (!TrayService.HasActiveBackgroundFeatures() && TrayAutostartToggle.IsChecked == true)
        {
            _gameInit = true;
            try { TrayAutostartToggle.IsChecked = false; } finally { _gameInit = false; }
            ShowNotification(LocalizationService.T("Сначала включите хотя бы одну фоновую функцию (автоочистку ОЗУ, авто-приоритет или физические ядра)."));
            return;
        }

        TrayService.SetAutostartEnabled(TrayAutostartToggle.IsChecked == true);
        ShowNotification(TrayAutostartToggle.IsChecked == true
            ? LocalizationService.T("Implexity Tweaker будет автоматически запускаться в трее вместе с Windows.")
            : LocalizationService.T("Автозапуск с Windows отключён."));
    }

    private void AutoRamThresholdCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _gameInit)
            return;

        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh();
        RefreshTrayStatusBanner();
    }

    private void AutoRamThresholdToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _gameInit)
            return;

        bool wasTrayActive = TrayService.HasActiveBackgroundFeatures();
        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh(notifyTray: true);
        RefreshTrayStatusBanner();

        var th = (AutoRamThresholdCombo.SelectedItem as ThresholdItem)?.Percent ?? 80;
        var msg = AutoRamThresholdToggle.IsChecked == true
            ? LocalizationService.TF("Автоочистка ОЗУ при превышении {0}% включена.", th)
            : LocalizationService.T("Автоочистка ОЗУ по порогу выключена.");

        if (!wasTrayActive && TrayService.HasActiveBackgroundFeatures())
            msg += "\n\n" + LocalizationService.T("Активирован фоновый режим в системном трее: при закрытии окна Implexity Tweaker продолжит работать в фоне.");

        ShowNotification(msg);
    }

    private void AutoRamGameLaunchToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _gameInit)
            return;

        bool wasTrayActive = TrayService.HasActiveBackgroundFeatures();
        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh(notifyTray: true);
        RefreshTrayStatusBanner();

        var msg = AutoRamGameLaunchToggle.IsChecked == true
            ? LocalizationService.T("Автоочистка ОЗУ при запуске игр из белого списка включена.")
            : LocalizationService.T("Автоочистка ОЗУ при запуске игр выключена.");

        if (!wasTrayActive && TrayService.HasActiveBackgroundFeatures())
            msg += "\n\n" + LocalizationService.T("Активирован фоновый режим в системном трее: при закрытии окна Implexity Tweaker продолжит работать в фоне.");

        ShowNotification(msg);
    }

    private void SaveAutoRamWhitelist_Click(object sender, RoutedEventArgs e)
    {
        SaveProcessTweaks();
        ProcessPriorityWatcher.Refresh();
        ShowNotification(LocalizationService.T("Белый список игр для автоочистки ОЗУ сохранён."));
    }

    // ===================== Мышь 1:1 Raw Input и Очередь ввода =====================

    private sealed record QueueSizeItem(int Size, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record Win32PriorityItem(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private void InitializeMouseAndInputTweaks()
    {
        _isProgrammaticChange = true;
        try
        {
            RawMouseInputToggle.IsChecked = _settings.RawMouseInput ?? MouseTweaksService.GetRawInputEnabled();
            HighPrecisionTimerToggle.IsChecked = _settings.HighPrecisionTimer ?? MouseTweaksService.GetHighPrecisionTimerEnabled();

            InputQueueSizeCombo.Items.Clear();
            var items = new[]
            {
                new QueueSizeItem(16, "16 (" + LocalizationService.T("Минимальная задержка") + ")"),
                new QueueSizeItem(20, "20 (" + LocalizationService.T("Низкая задержка") + ")"),
                new QueueSizeItem(32, "32 (" + LocalizationService.T("Сбалансированно") + ")"),
                new QueueSizeItem(50, "50 (" + LocalizationService.T("Умеренно") + ")"),
                new QueueSizeItem(100, "100 (" + LocalizationService.T("Стандарт Windows") + ")")
            };
            foreach (var it in items)
                InputQueueSizeCombo.Items.Add(it);

            var currentQueue = _settings.InputQueueSize ?? MouseTweaksService.GetInputQueueSize();
            InputQueueSizeCombo.SelectedItem = items.FirstOrDefault(x => x.Size == currentQueue) ?? items[^1];

            Win32PriorityCombo.Items.Clear();
            var prioItems = new[]
            {
                new Win32PriorityItem(2, "0x02 · " + LocalizationService.T("Стандарт Windows")),
                new Win32PriorityItem(38, "0x26 · " + LocalizationService.T("Минимальный инпут-лаг")),
                new Win32PriorityItem(40, "0x28 · " + LocalizationService.T("Стабильный FPS"))
            };
            foreach (var p in prioItems)
                Win32PriorityCombo.Items.Add(p);

            int curPrio = _settings.Win32PrioritySeparation ?? MouseTweaksService.GetWin32PrioritySeparation();
            Win32PriorityCombo.SelectedItem = prioItems.FirstOrDefault(x => x.Value == curPrio) ?? prioItems[0];

            UpdateTimerResolutionStatusText();
        }
        finally
        {
            _isProgrammaticChange = false;
        }

        _ = LoadAsyncInputTweaksStateAsync();
    }

    private async Task LoadAsyncInputTweaksStateAsync()
    {
        try
        {
            bool dynTickDisabled = await MouseTweaksService.GetDynamicTickDisabledAsync();

            _isProgrammaticChange = true;
            try
            {
                DisableDynamicTickToggle.IsChecked = _settings.DisableDynamicTick ?? dynTickDisabled;
            }
            finally
            {
                _isProgrammaticChange = false;
            }
        }
        catch { }
    }

    private void UpdateTimerResolutionStatusText()
    {
        double ms = MouseTweaksService.GetCurrentTimerResolutionMs();
        TimerResolutionStatusText.Text = LocalizationService.TF("Текущее разрешение таймера: {0:0.00} мс", ms);
    }

    private async void RawMouseInputToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isProgrammaticChange)
            return;

        try
        {
            bool enable = RawMouseInputToggle.IsChecked == true;
            await MouseTweaksService.SetRawInputAsync(enable);
            _settings.RawMouseInput = enable;
            SaveToSettings();
            ShowNotification(enable
                ? LocalizationService.T("Точный ввод мыши 1:1 включён, акселерация указателя отключена.")
                : LocalizationService.T("Параметры кривой и ускорения мыши возвращены к стандартным Windows."));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(RawMouseInputToggle, ex);
        }
    }

    private async void ApplyInputQueueSize_Click(object sender, RoutedEventArgs e)
    {
        if (InputQueueSizeCombo.SelectedItem is not QueueSizeItem item)
            return;

        try
        {
            await MouseTweaksService.SetInputQueueSizeAsync(item.Size);
            _settings.InputQueueSize = item.Size;
            SaveToSettings();
            ShowNotification(LocalizationService.TF(
                "Размер очереди мыши и клавиатуры установлен на {0}. Перезагрузите ПК для применения.", item.Size));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка: ") + ex.Message);
        }
    }

    private async void HighPrecisionTimerToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isProgrammaticChange)
            return;

        try
        {
            bool enable = HighPrecisionTimerToggle.IsChecked == true;
            await MouseTweaksService.SetHighPrecisionTimerAsync(enable);
            _settings.HighPrecisionTimer = enable;
            SaveToSettings();
            UpdateTimerResolutionStatusText();
            ShowNotification(enable
                ? LocalizationService.T("Включён точный системный таймер 0.5 мс и GlobalTimerResolutionRequests.")
                : LocalizationService.T("Системный таймер возвращён в стандартный режим."));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(HighPrecisionTimerToggle, ex);
        }
    }

    private async void DisableDynamicTickToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isProgrammaticChange)
            return;

        try
        {
            bool disable = DisableDynamicTickToggle.IsChecked == true;
            await MouseTweaksService.SetDynamicTickDisabledAsync(disable);
            _settings.DisableDynamicTick = disable;
            SaveToSettings();
            ShowNotification(disable
                ? LocalizationService.T("Dynamic Tick отключён (HPET очищен). Перезагрузите ПК для применения.")
                : LocalizationService.T("Dynamic Tick возвращён в стандартное состояние. Перезагрузите ПК."));
        }
        catch (Exception ex)
        {
            RevertToggleOnFailure(DisableDynamicTickToggle, ex);
        }
    }

    private async void ApplyWin32Priority_Click(object sender, RoutedEventArgs e)
    {
        if (Win32PriorityCombo.SelectedItem is not Win32PriorityItem item)
            return;

        try
        {
            await MouseTweaksService.SetWin32PrioritySeparationAsync(item.Value);
            _settings.Win32PrioritySeparation = item.Value;
            SaveToSettings();
            ShowNotification(LocalizationService.TF("Приоритет активного окна изменён: {0}.", item.Label));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка: ") + ex.Message);
        }
    }

    // ===================== MSI Mode и Interrupt Priority =====================

    private sealed record PriorityOption(MsiModeService.InterruptPriority Priority)
    {
        public override string ToString() => MsiModeService.PriorityLabel(Priority);
    }

    private async void RefreshMsiDevices_Click(object sender, RoutedEventArgs e)
        => await LoadMsiDevicesAsync();

    private async Task LoadMsiDevicesAsync()
    {
        MsiDevicesHost.Children.Clear();
        MsiDevicesHost.Children.Add(new TextBlock
        {
            Text = LocalizationService.T("Поиск видеокарт, сетевых и USB-контроллеров…"),
            Style = (Style)FindResource("ItemDescriptionText")
        });

        try
        {
            var devices = await MsiModeService.GetDevicesAsync();
            var affinityOptions = MsiModeService.GetAffinityPresets();
            MsiDevicesHost.Children.Clear();

            if (devices.Count == 0)
            {
                MsiDevicesHost.Children.Add(new TextBlock
                {
                    Text = LocalizationService.T("Подходящих PCI-устройств не обнаружено."),
                    Style = (Style)FindResource("ItemDescriptionText")
                });
                return;
            }

            var priorityOptions = new[]
            {
                new PriorityOption(MsiModeService.InterruptPriority.Default),
                new PriorityOption(MsiModeService.InterruptPriority.Low),
                new PriorityOption(MsiModeService.InterruptPriority.Normal),
                new PriorityOption(MsiModeService.InterruptPriority.High)
            };

            foreach (var dev in devices)
            {
                var rowBorder = new Border
                {
                    Style = (Style)Application.Current.Resources["FluentBlockBorder"],
                    Padding = new Thickness(14, 12, 14, 12),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                var titleRow = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

                var catBadge = new Border
                {
                    Background = (System.Windows.Media.Brush)Application.Current.Resources["App.AccentSoft"],
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = dev.Category switch
                        {
                            "GPU" => LocalizationService.T("Видеокарта"),
                            "USB" => LocalizationService.T("USB-контроллер"),
                            _ => LocalizationService.T("Сеть")
                        },
                        FontSize = 11,
                        FontWeight = FontWeights.Bold
                    }
                };
                titleRow.Children.Add(catBadge);

                titleRow.Children.Add(new TextBlock
                {
                    Text = dev.Name,
                    Style = (Style)FindResource("ItemTitleText"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                });

                leftStack.Children.Add(titleRow);

                Grid.SetColumn(leftStack, 0);
                grid.Children.Add(leftStack);

                var controlsStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var msiLabel = new TextBlock
                {
                    Text = "MSI:",
                    Style = (Style)FindResource("ItemDescriptionText"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var msiToggle = new Wpf.Ui.Controls.ToggleSwitch
                {
                    IsChecked = dev.MsiSupported,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                    ToolTip = LocalizationService.T("Переключает режим прерываний устройства (MSI)")
                };

                var prioCombo = new ComboBox
                {
                    Width = 128,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = LocalizationService.T("Приоритет обработки прерываний устройства")
                };
                foreach (var opt in priorityOptions)
                    prioCombo.Items.Add(opt);
                prioCombo.SelectedItem = priorityOptions.FirstOrDefault(o => o.Priority == dev.Priority) ?? priorityOptions[0];

                var affCombo = new ComboBox
                {
                    Width = 148,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    ToolTip = LocalizationService.T("Выделенное ядро CPU для прерываний устройства")
                };
                foreach (var aff in affinityOptions)
                    affCombo.Items.Add(aff);
                affCombo.SelectedItem = affinityOptions.FirstOrDefault(a => a.Mask == dev.AffinityMask) ?? affinityOptions[0];

                var applyBtn = new Wpf.Ui.Controls.Button
                {
                    Content = LocalizationService.T("Применить"),
                    Appearance = Wpf.Ui.Common.ControlAppearance.Primary,
                    Padding = new Thickness(14, 6, 14, 6),
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var pnpId = dev.PnpDeviceId;
                var devName = dev.Name;
                applyBtn.Click += async (_, _) =>
                {
                    var selectedPrio = (prioCombo.SelectedItem as PriorityOption)?.Priority ?? MsiModeService.InterruptPriority.Default;
                    var selectedAff = (affCombo.SelectedItem as MsiModeService.AffinityOption)?.Mask ?? 0UL;
                    var enableMsi = msiToggle.IsChecked == true;
                    applyBtn.IsEnabled = false;
                    try
                    {
                        await MsiModeService.ApplyDeviceSettingsAsync(pnpId, enableMsi, selectedPrio, selectedAff);
                        ShowNotification(LocalizationService.TF(
                            "Параметры прерываний для «{0}» сохранены (MSI: {1}, Приоритет: {2}). Перезагрузите ПК для применения.",
                            devName,
                            enableMsi ? LocalizationService.T("Вкл") : LocalizationService.T("Выкл"),
                            MsiModeService.PriorityLabel(selectedPrio)));
                    }
                    catch (Exception ex)
                    {
                        ShowNotification(LocalizationService.T("Ошибка применения MSI Mode: ") + ex.Message);
                    }
                    finally
                    {
                        applyBtn.IsEnabled = true;
                    }
                };

                controlsStack.Children.Add(msiLabel);
                controlsStack.Children.Add(msiToggle);
                controlsStack.Children.Add(prioCombo);
                controlsStack.Children.Add(affCombo);
                controlsStack.Children.Add(applyBtn);

                Grid.SetColumn(controlsStack, 1);
                grid.Children.Add(controlsStack);

                rowBorder.Child = grid;
                MsiDevicesHost.Children.Add(rowBorder);
            }
        }
        catch (Exception ex)
        {
            MsiDevicesHost.Children.Clear();
            MsiDevicesHost.Children.Add(new TextBlock
            {
                Text = ex.Message,
                Style = (Style)FindResource("ItemDescriptionText")
            });
        }
    }

    private async Task LoadGameGlobalsAsync()
    {
        _gameInit = true;
        try
        {
            HagsToggle.IsChecked = GamingService.GetHagsEnabled() ?? false;
            GameModeToggle.IsChecked = GamingService.GetGameModeEnabled();
            CoreParkingToggle.IsChecked = await GamingService.GetCoreParkingDisabledAsync();
        }
        catch
        {
            // состояние не критично для работы страницы
        }
        finally
        {
            _gameInit = false;
        }
    }

    private async void HagsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_gameInit)
            return;

        try
        {
            await GamingService.SetHagsAsync(HagsToggle.IsChecked == true);
            ShowNotification(LocalizationService.T("Изменение HAGS применится после перезагрузки Windows."));
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка: ") + ex.Message);
        }
    }

    private async void GameModeToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_gameInit)
            return;

        try
        {
            await GamingService.SetGameModeAsync(GameModeToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка: ") + ex.Message);
        }
    }

    private async void CoreParkingToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_gameInit)
            return;

        try
        {
            await GamingService.SetCoreParkingAsync(CoreParkingToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            ShowNotification(LocalizationService.T("Ошибка: ") + ex.Message);
        }
    }

    private void ShowNotification(string message)
    {
        MessageBox.Show(LocalizationService.T(message), LocalizationService.T("Готово"), MessageBoxButton.OK, MessageBoxImage.Information);
    }
}