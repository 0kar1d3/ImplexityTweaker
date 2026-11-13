using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ImplexityTweaker.Windows;

namespace ImplexityTweaker.Services;

public sealed record OverlayGpuTelemetry(
    int Index,
    double UtilizationPercent,
    double? TempC,
    double? VramUsedMb,
    double? VramTotalMb,
    double? CoreClockMhz,
    double? MemClockMhz,
    double? PowerWatts);

public sealed record OverlayTelemetrySnapshot(
    double Fps,
    double FrametimeMs,
    double Low1PercentFps,
    string ProcessName,
    double CpuPercent,
    double? CpuTempC,
    double GpuPercent,
    double? GpuTempC,
    double RamUsedGb,
    double RamTotalGb,
    int RamPercent,
    double[]? CpuCoreLoads = null,
    OverlayGpuTelemetry[]? GpuDetails = null,
    double[]? FramerateHistory = null);

/// <summary>
/// Сервис внутриигрового оверлея ImplexityOverlay:
/// лёгкий сбор метрик (GetSystemTimes, GetPerformanceInfo, NVML, ETW FPS, Ping),
/// управление OverlayWindow и глобальная горячая клавиша переключения в игре.
/// </summary>
public static class OverlayService
{
    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID = 0x494F; // "IO"

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PerformanceInformation
    {
        public int Cb;
        public nint CommitTotal;
        public nint CommitLimit;
        public nint CommitPeak;
        public nint PhysicalTotal;
        public nint PhysicalAvailable;
        public nint SystemCache;
        public nint KernelTotal;
        public nint KernelPaged;
        public nint KernelNonpaged;
        public nint PageSize;
        public int HandlesCount;
        public int ProcessCount;
        public int ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemProcessorPerformanceInfo
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
        private uint _reserved;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(
        int systemInformationClass,
        [Out] SystemProcessorPerformanceInfo[] systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(out PerformanceInformation pPerformanceInformation, int cb);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static OverlaySettings _settings = OverlaySettings.Load();
    private static OverlayWindow? _window;
    private static FpsEtwTracker? _fpsTracker;
    private static CancellationTokenSource? _loopCts;
    private static HwndSource? _hotkeySink;

    private static ulong _prevIdle;
    private static ulong _prevKernel;
    private static ulong _prevUser;
    private static bool _hasCpuPrev;

    private static SystemProcessorPerformanceInfo[]? _currCoreBuf;
    private static SystemProcessorPerformanceInfo[]? _prevCoreBuf;
    private static double[]? _coreLoadsCache;
    private static bool _hasCorePrev;

    private static double? _cachedCpuTemp;
    private static DateTime _lastCpuTempUtc = DateTime.MinValue;
    private static double _cachedWmiGpu;
    private static double? _cachedAltGpuTemp;
    private static OverlayGpuTelemetry[]? _cachedWmiGpus;
    private static DateTime _lastWmiGpuUtc = DateTime.MinValue;
    private static OverlayTelemetrySnapshot? _lastSnapshot;
    private static readonly object _fpsLock = new();
    private static volatile bool _isStarted;

    public static OverlaySettings CurrentSettings => _settings;
    public static bool IsRunning => _isStarted && _settings.Enabled;
    public static bool IsDragMode { get; private set; }

    public static void SetDragMode(bool enabled)
    {
        IsDragMode = enabled;
        if (enabled && !_settings.Enabled)
        {
            _settings.Enabled = true;
            OverlaySettings.Save(_settings);
            Start();
            ProcessPriorityWatcher.Refresh();
            OverlayStateChanged?.Invoke(null, true);
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _window?.SetDragMode(IsDragMode);
        });
    }

    public static event EventHandler<bool>? OverlayStateChanged;

    public static void InitializeAtStartup()
    {
        _settings = OverlaySettings.Load();
        RegisterConfiguredHotkey();

        if (_settings.AutoStartWithTweaker || _settings.Enabled)
        {
            _settings.Enabled = true;
            OverlaySettings.Save(_settings);
            Start();
        }
    }

    public static void ApplySettings(OverlaySettings settings)
    {
        _settings = settings;
        OverlaySettings.Save(settings);
        RegisterConfiguredHotkey();

        if (settings.Enabled)
        {
            if (_window == null)
                Start();
            else
                RefreshWindowImmediate();
        }
        else
        {
            Stop();
        }

        ProcessPriorityWatcher.Refresh();
    }

    public static void Toggle()
    {
        _settings.Enabled = !_settings.Enabled;
        OverlaySettings.Save(_settings);

        if (_settings.Enabled)
            Start();
        else
            Stop();

        ProcessPriorityWatcher.Refresh();
        OverlayStateChanged?.Invoke(null, _settings.Enabled);
    }

    public static void Start()
    {
        _isStarted = true;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_isStarted)
                return;

            if (_window == null)
            {
                _window = new OverlayWindow();
                _window.Show();
            }

            lock (_fpsLock)
            {
                if (NeedsEtw(_settings) && _fpsTracker == null)
                {
                    _fpsTracker = new FpsEtwTracker();
                    _fpsTracker.Start();
                }
            }

            if (_loopCts == null)
            {
                _loopCts = new CancellationTokenSource();
                _ = TelemetryLoopAsync(_loopCts.Token);
            }

            RefreshWindowImmediate();
        });
    }

    public static void Stop()
    {
        _isStarted = false;
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
        }
        catch { }
        _loopCts = null;

        lock (_fpsLock)
        {
            try { _fpsTracker?.Dispose(); } catch { }
            _fpsTracker = null;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (!_isStarted && _window != null)
            {
                try { _window.Close(); } catch { }
                _window = null;
            }
        });
    }

    public static void ShutdownAll()
    {
        Stop();
        UnregisterHotkeySink();
    }

    private static void RefreshWindowImmediate()
    {
        if (_window == null)
            return;

        var snap = _lastSnapshot ?? new OverlayTelemetrySnapshot(0, 0, 0, "", 0, _cachedCpuTemp, 0, null, 0, 0, 0);
        _window.ApplySettingsAndRender(_settings, snap);
        _window.EnsureTopmost();
    }

    private static bool NeedsEtw(OverlaySettings s) =>
        s.ShowFps || s.ShowFrametime || s.ShowOnePercentLow || s.ShowProcessName ||
        (s.VisualStyle == OverlayVisualStyle.TelemetryText && s.ShowFramerateGraph);

    private static async Task TelemetryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var s = _settings;
            try
            {
                // 1. ETW FPS / Frametime (только если нужен)
                bool needEtw = NeedsEtw(s);
                FpsEtwTracker.FrameMetrics frame;
                lock (_fpsLock)
                {
                    if (ct.IsCancellationRequested)
                        break;

                    if (needEtw && _fpsTracker == null)
                    {
                        _fpsTracker = new FpsEtwTracker();
                        _fpsTracker.Start();
                    }
                    else if (!needEtw && _fpsTracker != null)
                    {
                        _fpsTracker.Dispose();
                        _fpsTracker = null;
                    }

                    frame = _fpsTracker != null
                        ? _fpsTracker.Sample()
                        : new FpsEtwTracker.FrameMetrics(0, 0, 0, string.Empty);
                }

                // 2. CPU % (нативный вызов ядра, <0.02 мс; по ядрам только в стиле TelemetryText)
                bool isTelemetryStyle = s.VisualStyle == OverlayVisualStyle.TelemetryText;
                double cpuPct = 0;
                double[]? coreLoads = null;
                if (isTelemetryStyle && s.ShowCpu && s.ShowPerCoreCpu)
                {
                    (cpuPct, coreLoads) = ReadPerCoreCpuNative();
                }
                else if (s.ShowCpu)
                {
                    cpuPct = ReadCpuPercentNative();
                }

                // 3. RAM (нативный GetPerformanceInfo)
                var (ramUsed, ramTotal, ramPct) = s.ShowRam ? ReadRamNative() : (0, 0, 0);

                // 4. GPU % и температура (+ подробные GPU-метрики только в стиле TelemetryText)
                double gpuPct = 0;
                double? gpuTemp = null;
                OverlayGpuTelemetry[]? gpuDetails = null;
                bool wantDetailedGpu = isTelemetryStyle && s.ShowDetailedGpu && (s.ShowGpu || s.ShowGpuTemp);

                if (s.ShowGpu || s.ShowGpuTemp)
                {
                    if (wantDetailedGpu)
                    {
                        var nvmlAll = NvmlGpuReader.TryReadAll();
                        if (nvmlAll.Count > 0)
                        {
                            gpuPct = nvmlAll[0].UtilizationPercent;
                            gpuTemp = nvmlAll[0].TemperatureCelsius;

                            var list = new List<OverlayGpuTelemetry>(nvmlAll.Count + 1);
                            // Если есть iGPU (WMI phys count > NVML count), обновим её раз в 2 сек
                            if ((DateTime.UtcNow - _lastWmiGpuUtc).TotalSeconds >= 2)
                            {
                                _lastWmiGpuUtc = DateTime.UtcNow;
                                _cachedWmiGpus = await Task.Run(ReadWmiGpusDetailed, ct);
                            }

                            if (_cachedWmiGpus is { Length: > 1 })
                            {
                                // Добавляем не-NVML адаптер (например iGPU) в начало или оставляем все по порядку
                                int extraCount = _cachedWmiGpus.Length - nvmlAll.Count;
                                for (int k = 0; k < extraCount; k++)
                                {
                                    var w = _cachedWmiGpus[k];
                                    list.Add(w with { Index = list.Count + 1 });
                                }
                            }

                            foreach (var n in nvmlAll)
                            {
                                list.Add(new OverlayGpuTelemetry(
                                    list.Count + 1,
                                    n.UtilizationPercent,
                                    n.TemperatureCelsius > 0 ? n.TemperatureCelsius : null,
                                    n.VramUsedGb > 0 ? Math.Round(n.VramUsedGb * 1024.0, 0) : null,
                                    n.VramTotalGb > 0 ? Math.Round(n.VramTotalGb * 1024.0, 0) : null,
                                    n.CoreClockMhz,
                                    n.MemClockMhz,
                                    n.PowerWatts));
                            }
                            gpuDetails = list.ToArray();
                        }
                        else
                        {
                            if ((DateTime.UtcNow - _lastWmiGpuUtc).TotalSeconds >= 2)
                            {
                                _lastWmiGpuUtc = DateTime.UtcNow;
                                _cachedWmiGpus = await Task.Run(ReadWmiGpusDetailed, ct);
                                var alt = await Task.Run(AmdIntelGpuReader.TryRead, ct);
                                if (alt != null)
                                {
                                    _cachedAltGpuTemp = alt.TemperatureCelsius;
                                    if (_cachedWmiGpus.Length > 0)
                                    {
                                        var first = _cachedWmiGpus[0];
                                        _cachedWmiGpus[0] = first with
                                        {
                                            UtilizationPercent = first.UtilizationPercent > 0 ? first.UtilizationPercent : (alt.UtilizationPercent ?? 0),
                                            TempC = alt.TemperatureCelsius ?? first.TempC,
                                            CoreClockMhz = alt.CoreClockMhz ?? first.CoreClockMhz,
                                            MemClockMhz = alt.MemClockMhz ?? first.MemClockMhz,
                                            PowerWatts = alt.PowerWatts ?? first.PowerWatts
                                        };
                                    }
                                    else
                                    {
                                        _cachedWmiGpus =
                                        [
                                            new OverlayGpuTelemetry(1, alt.UtilizationPercent ?? 0, alt.TemperatureCelsius, null, null, alt.CoreClockMhz, alt.MemClockMhz, alt.PowerWatts)
                                        ];
                                    }
                                }
                                _cachedWmiGpu = _cachedWmiGpus.Length > 0 ? _cachedWmiGpus[0].UtilizationPercent : 0;
                            }
                            gpuPct = _cachedWmiGpu;
                            gpuTemp = _cachedAltGpuTemp;
                            gpuDetails = _cachedWmiGpus;
                        }
                    }
                    else
                    {
                        var nvml = NvmlGpuReader.TryRead();
                        if (nvml != null)
                        {
                            gpuPct = nvml.UtilizationPercent;
                            gpuTemp = nvml.TemperatureCelsius;
                        }
                        else if ((DateTime.UtcNow - _lastWmiGpuUtc).TotalSeconds >= 2)
                        {
                            _lastWmiGpuUtc = DateTime.UtcNow;
                            var alt = await Task.Run(AmdIntelGpuReader.TryRead, ct);
                            _cachedAltGpuTemp = alt?.TemperatureCelsius;
                            _cachedWmiGpu = s.ShowGpu
                                ? (alt?.UtilizationPercent ?? await Task.Run(ReadWmiGpuPercent, ct))
                                : 0;
                            gpuPct = _cachedWmiGpu;
                            gpuTemp = _cachedAltGpuTemp;
                        }
                        else
                        {
                            gpuPct = _cachedWmiGpu;
                            gpuTemp = _cachedAltGpuTemp;
                        }
                    }
                }

                // 5. Температура CPU (не чаще раза в 4 секунды)
                if (s.ShowCpuTemp && (DateTime.UtcNow - _lastCpuTempUtc).TotalSeconds >= 4)
                {
                    _lastCpuTempUtc = DateTime.UtcNow;
                    _cachedCpuTemp = await MonitoringService.GetCpuTemperatureAsync();
                }

                var snap = new OverlayTelemetrySnapshot(
                    frame.Fps,
                    frame.FrametimeMs,
                    frame.Low1PercentFps,
                    frame.ProcessName,
                    cpuPct,
                    s.ShowCpuTemp ? _cachedCpuTemp : null,
                    gpuPct,
                    s.ShowGpuTemp ? gpuTemp : null,
                    ramUsed,
                    ramTotal,
                    ramPct,
                    coreLoads,
                    gpuDetails,
                    isTelemetryStyle && s.ShowFramerateGraph ? frame.FramerateHistory : null);
                _lastSnapshot = snap;

                var disp = Application.Current?.Dispatcher;
                if (disp != null && !ct.IsCancellationRequested)
                {
                    await disp.BeginInvoke(DispatcherPriority.Background, () =>
                    {
                        if (_window != null && _settings.Enabled)
                        {
                            _window.ApplySettingsAndRender(_settings, snap);
                            _window.EnsureTopmost();
                        }
                    });
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Overlay", "TelemetryLoop iteration failed", ex);
            }

            try
            {
                int delay = Math.Clamp(s.RefreshIntervalMs, 400, 3000);
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private static double ReadCpuPercentNative()
    {
        if (!GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
            return 0;

        ulong idle = ToUInt64(idleFt);
        ulong kernel = ToUInt64(kernelFt);
        ulong user = ToUInt64(userFt);

        if (!_hasCpuPrev)
        {
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasCpuPrev = true;
            return 0;
        }

        ulong idleDiff = idle - _prevIdle;
        ulong totalDiff = (kernel - _prevKernel) + (user - _prevUser);

        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;

        if (totalDiff == 0)
            return 0;

        double busy = (double)(totalDiff - Math.Min(totalDiff, idleDiff)) / totalDiff * 100.0;
        return Math.Clamp(Math.Round(busy, 0), 0, 100);
    }

    private static ulong ToUInt64(FILETIME ft)
        => ((ulong)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    private static (double UsedGb, double TotalGb, int Percent) ReadRamNative()
    {
        if (!GetPerformanceInfo(out var pi, Marshal.SizeOf<PerformanceInformation>()) || (long)pi.PhysicalTotal <= 0)
            return (0, 0, 0);

        long totalBytes = (long)pi.PhysicalTotal * (long)pi.PageSize;
        long availBytes = (long)pi.PhysicalAvailable * (long)pi.PageSize;
        long usedBytes = Math.Max(0, totalBytes - availBytes);

        double totalGb = totalBytes / 1073741824.0;
        double usedGb = usedBytes / 1073741824.0;
        int pct = (int)Math.Clamp(usedBytes * 100L / totalBytes, 0L, 100L);
        return (usedGb, totalGb, pct);
    }

    private static (double TotalPercent, double[] CoreLoads) ReadPerCoreCpuNative()
    {
        int count = Math.Clamp(Environment.ProcessorCount, 1, 64);
        int structSize = Marshal.SizeOf<SystemProcessorPerformanceInfo>();

        if (_currCoreBuf == null || _currCoreBuf.Length != count)
        {
            _currCoreBuf = new SystemProcessorPerformanceInfo[count];
            _prevCoreBuf = new SystemProcessorPerformanceInfo[count];
            _coreLoadsCache = new double[count];
            _hasCorePrev = false;
        }

        if (NtQuerySystemInformation(8, _currCoreBuf, count * structSize, out int retLen) != 0)
            return (0, _coreLoadsCache!);

        int actualCount = Math.Min(count, Math.Max(1, retLen / structSize));
        if (!_hasCorePrev)
        {
            Array.Copy(_currCoreBuf, _prevCoreBuf!, actualCount);
            _hasCorePrev = true;
            return (0, _coreLoadsCache!);
        }

        double sum = 0;
        for (int i = 0; i < actualCount; i++)
        {
            long idleDiff = _currCoreBuf[i].IdleTime - _prevCoreBuf![i].IdleTime;
            long totalDiff = (_currCoreBuf[i].KernelTime - _prevCoreBuf[i].KernelTime)
                           + (_currCoreBuf[i].UserTime - _prevCoreBuf[i].UserTime);

            double busy = totalDiff > 0
                ? (double)(totalDiff - Math.Min(totalDiff, idleDiff)) / totalDiff * 100.0
                : 0;
            double rounded = Math.Clamp(Math.Round(busy, 0), 0, 100);
            _coreLoadsCache![i] = rounded;
            sum += rounded;
        }

        Array.Copy(_currCoreBuf, _prevCoreBuf!, actualCount);
        double avg = Math.Clamp(Math.Round(sum / actualCount, 0), 0, 100);
        return (avg, _coreLoadsCache!);
    }

    private static readonly Regex PhysRegex = new(@"phys_(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static OverlayGpuTelemetry[] ReadWmiGpusDetailed()
    {
        var loadByPhys = new Dictionary<int, double>();
        var memByPhys = new Dictionary<int, double>();

        try
        {
            using var engQuery = new ManagementObjectSearcher(
                @"\\.\root\cimv2",
                "SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            using var engResults = engQuery.Get();
            foreach (ManagementObject item in engResults)
            {
                using (item)
                {
                    var name = item["Name"]?.ToString() ?? "";
                    if (!name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var m = PhysRegex.Match(name);
                    int phys = m.Success && int.TryParse(m.Groups[1].Value, out int p) ? p : 0;
                    if (double.TryParse(item["UtilizationPercentage"]?.ToString(), out var v))
                    {
                        loadByPhys.TryGetValue(phys, out double prev);
                        loadByPhys[phys] = Math.Clamp(prev + v, 0, 100);
                    }
                }
            }
        }
        catch { }

        try
        {
            using var memQuery = new ManagementObjectSearcher(
                @"\\.\root\cimv2",
                "SELECT DedicatedUsage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUAdapterMemory");
            using var memResults = memQuery.Get();
            foreach (ManagementObject item in memResults)
            {
                using (item)
                {
                    var name = item["Name"]?.ToString() ?? "";
                    var m = PhysRegex.Match(name);
                    int phys = m.Success && int.TryParse(m.Groups[1].Value, out int p) ? p : 0;
                    if (ulong.TryParse(item["DedicatedUsage"]?.ToString(), out ulong bytes))
                    {
                        double mb = Math.Round(bytes / 1048576.0, 0);
                        memByPhys.TryGetValue(phys, out double prev);
                        if (mb > prev) memByPhys[phys] = mb;
                    }
                }
            }
        }
        catch { }

        var keys = loadByPhys.Keys.Union(memByPhys.Keys).OrderBy(k => k).ToList();
        if (keys.Count == 0)
            return Array.Empty<OverlayGpuTelemetry>();

        var result = new List<OverlayGpuTelemetry>(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            int phys = keys[i];
            loadByPhys.TryGetValue(phys, out double util);
            memByPhys.TryGetValue(phys, out double vramMb);
            result.Add(new OverlayGpuTelemetry(i + 1, Math.Round(util, 0), null, vramMb > 0 ? vramMb : null, null, null, null, null));
        }
        return result.ToArray();
    }

    private static double ReadWmiGpuPercent()
    {
        try
        {
            using var query = new ManagementObjectSearcher(
                @"\\.\root\cimv2",
                "SELECT UtilizationPercentage, Name FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
            double sum = 0;
            using var results = query.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var name = item["Name"]?.ToString() ?? "";
                    if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                    {
                        if (double.TryParse(item["UtilizationPercentage"]?.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
                            sum += v;
                    }
                }
            }
            return Math.Clamp(Math.Round(sum, 0), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    // ===================== Глобальная горячая клавиша =====================

    private static void RegisterConfiguredHotkey()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            EnsureHotkeySink();
            if (_hotkeySink == null)
                return;

            UnregisterHotKey(_hotkeySink.Handle, HOTKEY_ID);

            var (mods, vk) = ParseHotkey(_settings.Hotkey);
            if (vk == 0)
                return;

            RegisterHotKey(_hotkeySink.Handle, HOTKEY_ID, mods | MOD_NOREPEAT, vk);
        });
    }

    private static void EnsureHotkeySink()
    {
        if (_hotkeySink != null)
            return;

        var p = new HwndSourceParameters("ImplexityOverlayHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hotkeySink = new HwndSource(p);
        _hotkeySink.AddHook(HotkeyWndProc);
    }

    private static void UnregisterHotkeySink()
    {
        try
        {
            if (_hotkeySink != null)
            {
                UnregisterHotKey(_hotkeySink.Handle, HOTKEY_ID);
                _hotkeySink.RemoveHook(HotkeyWndProc);
                _hotkeySink.Dispose();
                _hotkeySink = null;
            }
        }
        catch { }
    }

    private static IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            Toggle();
        }
        return IntPtr.Zero;
    }

    private static (uint Mods, uint Vk) ParseHotkey(string? hotkey)
    {
        return (hotkey ?? "F9").Trim().ToUpperInvariant() switch
        {
            "F8" => (0, 0x77),
            "F9" => (0, 0x78),
            "F10" => (0, 0x79),
            "F11" => (0, 0x7A),
            "ALT+O" => (MOD_ALT, 0x4F),
            "CTRL+SHIFT+O" => (MOD_CONTROL | MOD_SHIFT, 0x4F),
            _ => (0, 0)
        };
    }
}
