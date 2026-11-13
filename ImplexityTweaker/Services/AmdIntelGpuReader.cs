using System;
using System.Globalization;
using System.Management;

namespace ImplexityTweaker.Services;

/// <summary>
/// Чтение расширенных датчиков GPU для видеокарт AMD и Intel (температура, частоты ядра/памяти,
/// энергопотребление, загрузка) через WMI-интерфейс LibreHardwareMonitor / OpenHardwareMonitor,
/// если они запущены в фоне (используется как fallback при отсутствии NVIDIA NVML).
/// </summary>
public static class AmdIntelGpuReader
{
    public sealed record GpuSensorStats(
        double? UtilizationPercent,
        double? TemperatureCelsius,
        double? CoreClockMhz,
        double? MemClockMhz,
        double? PowerWatts);

    private static readonly string[] Namespaces =
    [
        @"\\.\root\LibreHardwareMonitor",
        @"\\.\root\OpenHardwareMonitor"
    ];

    public static GpuSensorStats? TryRead()
    {
        foreach (var ns in Namespaces)
        {
            var stats = TryReadFromNamespace(ns);
            if (stats != null)
                return stats;
        }
        return null;
    }

    private static GpuSensorStats? TryReadFromNamespace(string ns)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(ns),
                new ObjectQuery("SELECT Name, SensorType, Value, Identifier FROM Sensor"));
            using var results = searcher.Get();

            double? util = null;
            double? temp = null;
            double? coreClock = null;
            double? memClock = null;
            double? power = null;
            bool anyGpu = false;

            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var id = obj["Identifier"]?.ToString() ?? string.Empty;
                    if (!IsGpuIdentifier(id))
                        continue;

                    anyGpu = true;
                    var sensorType = obj["SensorType"]?.ToString() ?? string.Empty;
                    var name = obj["Name"]?.ToString() ?? string.Empty;
                    if (!double.TryParse(obj["Value"]?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
                        continue;

                    switch (sensorType)
                    {
                        case "Temperature":
                            if (val is > 5 and < 120 &&
                                (temp == null || name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("GPU", StringComparison.OrdinalIgnoreCase)))
                            {
                                temp = Math.Round(val, 0);
                            }
                            break;

                        case "Load":
                            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("D3D 3D", StringComparison.OrdinalIgnoreCase))
                            {
                                util = Math.Clamp(Math.Round(val, 0), 0, 100);
                            }
                            break;

                        case "Clock":
                            if (name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                                coreClock = Math.Round(val, 0);
                            else if (name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                                memClock = Math.Round(val, 0);
                            break;

                        case "Power":
                            if (val > 0 && (power == null ||
                                name.Contains("Package", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Total", StringComparison.OrdinalIgnoreCase) ||
                                name.Contains("Core", StringComparison.OrdinalIgnoreCase)))
                            {
                                power = Math.Round(val, 1);
                            }
                            break;
                    }
                }
            }

            return anyGpu && (util != null || temp != null || coreClock != null || power != null)
                ? new GpuSensorStats(util, temp, coreClock, memClock, power)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGpuIdentifier(string id)
        => id.Contains("/gpu-", StringComparison.OrdinalIgnoreCase)
        || id.Contains("/atigpu", StringComparison.OrdinalIgnoreCase)
        || id.Contains("/amdgpu", StringComparison.OrdinalIgnoreCase)
        || id.Contains("/intelgpu", StringComparison.OrdinalIgnoreCase)
        || id.Contains("/nvidiagpu", StringComparison.OrdinalIgnoreCase);
}
