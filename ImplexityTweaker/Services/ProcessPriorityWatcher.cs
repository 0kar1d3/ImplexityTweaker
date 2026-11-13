using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ImplexityTweaker.Services;

/// <summary>
/// Лёгкий фоновый наблюдатель: поднимает приоритет выбранному процессу,
/// ограничивает физическими ядрами и запускает автоочистку ОЗУ.
/// Использует единый нативный снимок CreateToolhelp32Snapshot и кэш настроек —
/// практически нулевая нагрузка на CPU и память.
/// </summary>
public static class ProcessPriorityWatcher
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static DispatcherTimer? _timer;
    private static readonly HashSet<uint> AppliedPids = [];
    private static readonly HashSet<uint> CleanedForGamePids = [];
    private static readonly HashSet<uint> SeenPidsScratch = [];

    private static ProcessTweaks? _cachedTweaks;
    private static string? _cachedAutoPriorityExe;
    private static string? _cachedAffinityExe;
    private static string[] _cachedWhitelistExes = [];

    private static IntPtr? _physicalMask;
    private static DateTime _lastAutoRamCleanUtc = DateTime.MinValue;
    private static int _autoCleaningRam;

    public static bool IsRunning => _timer != null;

    /// <summary>Перечитывает настройки, перезапускает наблюдение и синхронизирует системный трей.</summary>
    public static void Refresh(bool notifyTray = false)
    {
        Stop();

        var tweaks = ProcessTweaksStore.Load();
        _cachedTweaks = tweaks;
        _cachedAutoPriorityExe = NormalizeExeName(tweaks.AutoPriorityProcess);
        _cachedAffinityExe = NormalizeExeName(tweaks.AffinityProcess);
        _cachedWhitelistExes = ParseWhitelist(tweaks.AutoRamGameWhitelist);

        if (tweaks.RequiresBackgroundTray)
            Start();

        TrayService.SyncWithSettings(notifyTray);
    }

    public static void Start()
    {
        if (_timer != null)
            return;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    public static void Stop()
    {
        _timer?.Stop();
        _timer = null;
        AppliedPids.Clear();
        CleanedForGamePids.Clear();
        SeenPidsScratch.Clear();
    }

    private static string? NormalizeExeName(string? name)
    {
        var raw = Path.GetFileNameWithoutExtension(name ?? string.Empty).Trim();
        return raw.Length == 0 ? null : raw + ".exe";
    }

    private static string[] ParseWhitelist(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var parts = raw.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var exe = NormalizeExeName(p);
            if (exe != null)
                list.Add(exe);
        }
        return [.. list];
    }

    private static void Tick()
    {
        try
        {
            var tweaks = _cachedTweaks;
            if (tweaks == null || !tweaks.RequiresBackgroundTray)
                return;

            bool needProcessScan =
                (tweaks.AutoPriorityEnabled && _cachedAutoPriorityExe != null) ||
                (tweaks.AffinityEnabled && _cachedAffinityExe != null) ||
                (tweaks.AutoRamOnGameLaunchEnabled && _cachedWhitelistExes.Length > 0);

            if (needProcessScan)
                ScanProcessesNative(tweaks);

            if (tweaks.AutoRamThresholdEnabled)
                _ = CheckRamThresholdAsync(tweaks.AutoRamThresholdPercent);
        }
        catch
        {
            // наблюдатель не должен ломать приложение
        }
    }

    /// <summary>
    /// Проходит по процессам за один нативный вызов CreateToolhelp32Snapshot
    /// без аллокаций массивов Process[] и лишних дескрипторов ядра.
    /// </summary>
    private static void ScanProcessesNative(ProcessTweaks tweaks)
    {
        var snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snap == INVALID_HANDLE_VALUE || snap == IntPtr.Zero)
            return;

        string? launchedGame = null;
        SeenPidsScratch.Clear();

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry))
                return;

            do
            {
                uint pid = entry.th32ProcessID;
                if (pid == 0)
                    continue;

                SeenPidsScratch.Add(pid);
                var exe = entry.szExeFile;
                if (string.IsNullOrEmpty(exe))
                    continue;

                bool matchPriority = tweaks.AutoPriorityEnabled &&
                                     _cachedAutoPriorityExe != null &&
                                     string.Equals(exe, _cachedAutoPriorityExe, StringComparison.OrdinalIgnoreCase);

                bool matchAffinity = tweaks.AffinityEnabled &&
                                     _cachedAffinityExe != null &&
                                     string.Equals(exe, _cachedAffinityExe, StringComparison.OrdinalIgnoreCase);

                if ((matchPriority || matchAffinity) && AppliedPids.Add(pid))
                    ApplyProcessTweaks((int)pid, matchPriority, matchAffinity);

                if (tweaks.AutoRamOnGameLaunchEnabled && Volatile.Read(ref _autoCleaningRam) == 0 && _cachedWhitelistExes.Length > 0)
                {
                    foreach (var target in _cachedWhitelistExes)
                    {
                        if (string.Equals(exe, target, StringComparison.OrdinalIgnoreCase))
                        {
                            if (CleanedForGamePids.Add(pid))
                                launchedGame ??= exe;
                            break;
                        }
                    }
                }
            }
            while (Process32NextW(snap, ref entry));
        }
        finally
        {
            CloseHandle(snap);
        }

        // Чистим завершившиеся PID, чтобы HashSet не рос со временем
        if (AppliedPids.Count > 0)
            AppliedPids.RemoveWhere(id => !SeenPidsScratch.Contains(id));
        if (CleanedForGamePids.Count > 0)
            CleanedForGamePids.RemoveWhere(id => !SeenPidsScratch.Contains(id));
        SeenPidsScratch.Clear();

        if (launchedGame != null)
            _ = TriggerWhitelistRamCleanAsync(launchedGame);
    }

    private static void ApplyProcessTweaks(int pid, bool raisePriority, bool physicalCoresOnly)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (raisePriority)
                process.PriorityClass = ProcessPriorityClass.High;

            if (physicalCoresOnly)
            {
                var mask = CachedPhysicalCoreMask();
                if (mask != IntPtr.Zero)
                    process.ProcessorAffinity = mask;
            }
        }
        catch
        {
            // процесс завершился или недостаточно прав
        }
    }

    private static async Task TriggerWhitelistRamCleanAsync(string launchedGame)
    {
        if (Interlocked.CompareExchange(ref _autoCleaningRam, 1, 0) != 0)
            return;

        _lastAutoRamCleanUtc = DateTime.UtcNow;
        try
        {
            var freedMb = await RamCleanerService.CleanAsync();
            TrayService.ShowBalloon(
                LocalizationService.T("Игровой буст ОЗУ"),
                LocalizationService.TF("Запущен {0} — очищено {1} МБ ОЗУ.", launchedGame, freedMb));
        }
        catch { }
        finally
        {
            Volatile.Write(ref _autoCleaningRam, 0);
            TrimIfHidden();
        }
    }

    private static async Task CheckRamThresholdAsync(int thresholdPercent)
    {
        if (Volatile.Read(ref _autoCleaningRam) != 0)
            return;

        var threshold = Math.Clamp(thresholdPercent, 50, 98);
        if ((DateTime.UtcNow - _lastAutoRamCleanUtc).TotalMinutes < 3)
            return;

        var currentPercent = RamCleanerService.GetMemoryUsagePercent();
        if (currentPercent < threshold)
            return;

        if (Interlocked.CompareExchange(ref _autoCleaningRam, 1, 0) != 0)
            return;

        _lastAutoRamCleanUtc = DateTime.UtcNow;
        try
        {
            var freedMb = await RamCleanerService.CleanAsync();
            if (freedMb > 0)
            {
                TrayService.ShowBalloon(
                    LocalizationService.T("Автоочистка ОЗУ"),
                    LocalizationService.TF("Загрузка памяти превысила {0}%. Освобождено {1} МБ.", threshold, freedMb));
            }
        }
        catch { }
        finally
        {
            Volatile.Write(ref _autoCleaningRam, 0);
            TrimIfHidden();
        }
    }

    private static void TrimIfHidden()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (Application.Current?.MainWindow is { IsVisible: false })
                    RamCleanerService.TrimOwnProcessMemory();
            });
        }
        catch { }
    }

    /// <summary>Маска физических ядер считается один раз за запуск.</summary>
    private static IntPtr CachedPhysicalCoreMask()
        => _physicalMask ??= PhysicalCoreMask();

    private static IntPtr PhysicalCoreMask()
    {
        try
        {
            var logical = Environment.ProcessorCount;
            if (logical <= 0 || logical > 64)
                return IntPtr.Zero;

            long mask = 0;
            if (IsSmtEnabled(logical))
            {
                for (var i = 0; i < logical; i += 2)
                    mask |= 1L << i;
            }
            else
            {
                for (var i = 0; i < logical; i++)
                    mask |= 1L << i;
            }

            return new IntPtr(mask);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private static bool IsSmtEnabled(int logical)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT NumberOfCores FROM Win32_Processor");
            using var results = searcher.Get();
            int totalCores = 0;
            foreach (ManagementObject cpu in results)
            {
                using (cpu)
                {
                    if (cpu["NumberOfCores"] is { } cores &&
                        int.TryParse(cores.ToString(), out var count) && count > 0)
                    {
                        totalCores += count;
                    }
                }
            }
            if (totalCores > 0)
                return logical > totalCores;
        }
        catch
        {
        }

        return logical > 4;
    }
}
