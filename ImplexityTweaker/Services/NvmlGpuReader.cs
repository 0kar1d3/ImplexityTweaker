using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ImplexityTweaker.Services;

/// <summary>
/// Чтение загрузки GPU, температуры и видеопамяти напрямую через NVIDIA NVML (nvml.dll)
/// без запуска дочерних процессов (0% нагрузки на CPU). При отсутствии NVIDIA/NVML
/// возвращает null, и сервис использует системные счётчики WMI.
/// </summary>
public static class NvmlGpuReader
{
    private const int NvmlSuccess = 0;
    private const uint NvmlTempGpu = 0;
    private const uint NvmlClockGraphics = 0;
    private const uint NvmlClockMem = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlInitDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlShutdownDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetCountDelegate(out uint deviceCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetHandleByIndexDelegate(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetUtilizationRatesDelegate(IntPtr device, out NvmlUtilization utilization);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetTemperatureDelegate(IntPtr device, uint sensorType, out uint temp);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetMemoryInfoDelegate(IntPtr device, out NvmlMemory memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetClockInfoDelegate(IntPtr device, uint clockType, out uint clockMhz);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvmlDeviceGetPowerUsageDelegate(IntPtr device, out uint powerMilliWatts);

    public sealed record NvmlStats(
        double UtilizationPercent,
        double TemperatureCelsius,
        double VramUsedGb,
        double VramTotalGb,
        double? CoreClockMhz = null,
        double? MemClockMhz = null,
        double? PowerWatts = null,
        double? MemUtilPercent = null);

    private static bool _probed;
    private static bool _available;
    private static IntPtr _lib;
    private static readonly System.Collections.Generic.List<IntPtr> _devices = new();
    private static NvmlShutdownDelegate? _shutdown;
    private static NvmlDeviceGetUtilizationRatesDelegate? _getUtil;
    private static NvmlDeviceGetTemperatureDelegate? _getTemp;
    private static NvmlDeviceGetMemoryInfoDelegate? _getMem;
    private static NvmlDeviceGetClockInfoDelegate? _getClock;
    private static NvmlDeviceGetPowerUsageDelegate? _getPower;

    private static void EnsureInitialized()
    {
        if (_probed)
            return;
        _probed = true;

        try
        {
            string? path = null;
            var sysCandidate = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            if (File.Exists(sysCandidate))
            {
                path = sysCandidate;
            }
            else
            {
                var pfCandidate = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "NVIDIA Corporation", "NVSMI", "nvml.dll");
                if (File.Exists(pfCandidate))
                    path = pfCandidate;
            }

            if (path == null || !NativeLibrary.TryLoad(path, out _lib))
                return;

            var init = Marshal.GetDelegateForFunctionPointer<NvmlInitDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlInit_v2"));
            _shutdown = Marshal.GetDelegateForFunctionPointer<NvmlShutdownDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlShutdown"));
            var getHandle = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetHandleByIndexDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlDeviceGetHandleByIndex_v2"));
            _getUtil = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetUtilizationRatesDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlDeviceGetUtilizationRates"));
            _getTemp = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetTemperatureDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlDeviceGetTemperature"));
            _getMem = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetMemoryInfoDelegate>(
                NativeLibrary.GetExport(_lib, "nvmlDeviceGetMemoryInfo"));

            if (NativeLibrary.TryGetExport(_lib, "nvmlDeviceGetClockInfo", out var clkPtr))
                _getClock = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetClockInfoDelegate>(clkPtr);

            if (NativeLibrary.TryGetExport(_lib, "nvmlDeviceGetPowerUsage", out var pwrPtr))
                _getPower = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetPowerUsageDelegate>(pwrPtr);

            uint count = 1;
            if (init() != NvmlSuccess)
                return;

            if (NativeLibrary.TryGetExport(_lib, "nvmlDeviceGetCount_v2", out var cntPtr))
            {
                var getCount = Marshal.GetDelegateForFunctionPointer<NvmlDeviceGetCountDelegate>(cntPtr);
                if (getCount(out uint c) == NvmlSuccess && c > 0)
                    count = Math.Min(c, 4u);
            }

            for (uint i = 0; i < count; i++)
            {
                if (getHandle(i, out var dev) == NvmlSuccess && dev != IntPtr.Zero)
                    _devices.Add(dev);
            }

            if (_devices.Count == 0)
            {
                _shutdown?.Invoke();
                return;
            }

            _available = true;
        }
        catch
        {
            _available = false;
        }
    }

    public static NvmlStats? TryRead()
    {
        EnsureInitialized();
        if (!_available || _devices.Count == 0)
            return null;

        return ReadDevice(_devices[0], includeExtras: false);
    }

    public static System.Collections.Generic.IReadOnlyList<NvmlStats> TryReadAll()
    {
        EnsureInitialized();
        if (!_available || _devices.Count == 0)
            return Array.Empty<NvmlStats>();

        var result = new System.Collections.Generic.List<NvmlStats>(_devices.Count);
        foreach (var dev in _devices)
        {
            var stats = ReadDevice(dev, includeExtras: true);
            if (stats != null)
                result.Add(stats);
        }
        return result;
    }

    private static NvmlStats? ReadDevice(IntPtr dev, bool includeExtras)
    {
        try
        {
            double util = 0;
            double? memUtil = null;
            if (_getUtil != null && _getUtil(dev, out var u) == NvmlSuccess)
            {
                util = Math.Clamp(u.Gpu, 0, 100);
                memUtil = Math.Clamp(u.Memory, 0, 100);
            }

            double temp = 0;
            if (_getTemp != null && _getTemp(dev, NvmlTempGpu, out var t) == NvmlSuccess)
                temp = t;

            double usedGb = 0, totalGb = 0;
            if (_getMem != null && _getMem(dev, out var mem) == NvmlSuccess && mem.Total > 0)
            {
                usedGb = Math.Round(mem.Used / 1073741824.0, 2);
                totalGb = Math.Round(mem.Total / 1073741824.0, 1);
            }

            double? coreClock = null, memClock = null, powerW = null;
            if (includeExtras)
            {
                if (_getClock != null)
                {
                    if (_getClock(dev, NvmlClockGraphics, out uint gc) == NvmlSuccess && gc > 0)
                        coreClock = gc;
                    if (_getClock(dev, NvmlClockMem, out uint mc) == NvmlSuccess && mc > 0)
                        memClock = mc;
                }

                if (_getPower != null && _getPower(dev, out uint mw) == NvmlSuccess && mw > 0)
                    powerW = Math.Round(mw / 1000.0, 1);
            }

            return new NvmlStats(util, temp, usedGb, totalGb, coreClock, memClock, powerW, memUtil);
        }
        catch
        {
            return null;
        }
    }
}
