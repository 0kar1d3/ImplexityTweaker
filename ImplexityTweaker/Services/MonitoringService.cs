using System.Globalization;
using System.IO;
using System.Management;

namespace ImplexityTweaker.Services;

/// <summary>
/// Мониторинг системы: загрузка CPU/GPU/диска/сети, память и температуры.
/// Данные берутся напрямую через WMI (быстро, без запуска внешних процессов).
/// </summary>
public static class MonitoringService
{
    public sealed record Snapshot(
        double CpuPercent,
        double RamUsedGb,
        double RamTotalGb,
        double GpuPercent,
        double DiskPercent,
        double NetMbps);

    private static readonly ManagementScope CimScope = new(@"\\.\root\cimv2");
    private static readonly ManagementScope WmiScope = new(@"\\.\root\wmi");

    /// <summary>Данные для мини-графиков. null — WMI недоступен.</summary>
    public static Task<Snapshot?> GetSnapshotAsync() => Task.Run(ReadSnapshot);

    private static Snapshot? ReadSnapshot()
    {
        try
        {
            var cpu = SumPerf("Win32_PerfFormattedData_PerfOS_Processor", "PercentProcessorTime", "Name", "_Total");

            double ramUsed = 0;
            double ramTotal = 0;
            using (var query = Query(CimScope,
                       "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem"))
            using (var results = query.Get())
            {
                foreach (ManagementObject os in results)
                {
                    using (os)
                    {
                        var totalKb = ToDouble(os["TotalVisibleMemorySize"]);
                        var freeKb = ToDouble(os["FreePhysicalMemory"]);
                        ramTotal = Math.Round(totalKb / 1048576.0, 2);
                        ramUsed = Math.Round(Math.Max(0, totalKb - freeKb) / 1048576.0, 2);
                    }
                }
            }

            var gpu = SumWhere("Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine",
                "UtilizationPercentage", "Name", "engtype_3D");

            var disk = SumPerf("Win32_PerfFormattedData_PerfDisk_PhysicalDisk", "PercentDiskTime", "Name", "_Total");

            double netMbps = 0;
            using (var query = Query(CimScope,
                       "SELECT BytesReceivedPersec, BytesSentPersec FROM Win32_PerfFormattedData_Tcpip_NetworkInterface"))
            using (var results = query.Get())
            {
                double bytes = 0;
                foreach (ManagementObject nic in results)
                {
                    using (nic)
                        bytes += ToDouble(nic["BytesReceivedPersec"]) + ToDouble(nic["BytesSentPersec"]);
                }

                netMbps = Math.Round(bytes * 8 / 1000000.0, 2);
            }

            return new Snapshot(
                Clamp(cpu),
                Math.Max(0, ramUsed),
                Math.Max(0, ramTotal),
                Clamp(gpu),
                Clamp(disk),
                Math.Max(0, netMbps));
        }
        catch
        {
            return null;
        }
    }
    /// <summary>
    /// Температура CPU. Сначала пробуем сенсоры программы мониторинга
    /// (LibreHardwareMonitor / OpenHardwareMonitor, если она запущена),
    /// затем — ACPI-зону. null — датчик недоступен или отдал мусор.
    /// </summary>
    public static Task<double?> GetCpuTemperatureAsync() => Task.Run(() =>
        ReadHardwareMonitorCpuTemp() ?? ReadAcpiCpuTemp());

    /// <summary>Правдоподобный диапазон температуры процессора, °C. Всё вне — мусор датчика.</summary>
    private const double MinCpuTempCelsius = 20.0;
    private const double MaxCpuTempCelsius = 115.0;

    /// <summary>
    /// Пространства имён программ мониторинга: значения там уже в градусах Цельсия.
    /// Существуют только пока соответствующая программа запущена.
    /// </summary>
    private static readonly string[] MonitorNamespaces =
    [
        @"root\LibreHardwareMonitor",
        @"root\OpenHardwareMonitor"
    ];

    /// <summary>Температура CPU из WMI-сенсоров программы мониторинга. null — недоступно.</summary>
    private static double? ReadHardwareMonitorCpuTemp()
    {
        foreach (var ns in MonitorNamespaces)
        {
            try
            {
                var scope = new ManagementScope($@"\\.\{ns}");
                using var query = Query(scope, "SELECT Name, Value FROM Sensor WHERE SensorType='Temperature'");
                using var results = query.Get();

                double? package = null;
                double? single = null;

                foreach (ManagementObject sensor in results)
                {
                    using (sensor)
                    {
                        var name = sensor["Name"]?.ToString() ?? string.Empty;
                        if (!name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var value = ToDouble(sensor["Value"]);
                        if (value is < MinCpuTempCelsius or > MaxCpuTempCelsius)
                            continue;

                        // «CPU Package» — самый показательный сенсор, берём его при наличии.
                        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            package = package is null ? value : Math.Max(package.Value, value);
                        else
                            single = single is null ? value : Math.Max(single.Value, value);
                    }
                }

                var result = package ?? single;
                if (result.HasValue)
                    return Math.Round(result.Value, 1);
            }
            catch
            {
                // пространства имён нет — значит программа мониторинга не запущена
            }
        }

        return null;
    }

    /// <summary>
    /// Температура CPU из ACPI-зон (MSAcpi_ThermalZoneTemperature).
    /// Значение приходит в десятых долях Кельвина: °C = raw / 10 − 273.15.
    /// Зон на машине обычно несколько, и первая по порядку — часто не процессор
    /// (корпус, батарея, чипсет), из-за чего показывались заниженные значения.
    /// Поэтому перебираем все активные зоны и берём самую горячую правдоподобную.
    /// </summary>
    private static double? ReadAcpiCpuTemp()
    {
        try
        {
            using var query = Query(WmiScope,
                "SELECT InstanceName, Active, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = query.Get();

            var zones = new List<(string Name, bool Active, double RawTenthKelvin)>();

            foreach (ManagementObject zone in results)
            {
                using (zone)
                {
                    // Неактивные зоны Windows отдаёт «замороженными» — их отсекает выбор ниже.
                    var active = zone["Active"] is not bool isActive || isActive;
                    zones.Add((
                        zone["InstanceName"]?.ToString() ?? string.Empty,
                        active,
                        ToDouble(zone["CurrentTemperature"])));
                }
            }

            return PickCpuTemperatureFromZones(zones);
        }
        catch
        {
            // класс ACPI-зон есть не на всех системах — это нормально
            return null;
        }
    }

    /// <summary>
    /// Выбор температуры процессора из ACPI-зон: активные зоны, десятые доли
    /// Кельвина → °C, отбрасываем неправдоподобное и берём самый горячий
    /// показатель (если прошивка назвала зону «CPU» — доверяем ей).
    /// Выделено отдельно, чтобы логику можно было проверить на тестовых данных.
    /// </summary>
    internal static double? PickCpuTemperatureFromZones(IEnumerable<(string Name, bool Active, double RawTenthKelvin)> zones)
    {
        var readings = new List<(string Name, double Celsius)>();

        foreach (var (name, active, raw) in zones)
        {
            if (!active || raw <= 0)
                continue;

            var celsius = Math.Round(raw / 10.0 - 273.15, 1);
            if (celsius is < MinCpuTempCelsius or > MaxCpuTempCelsius)
                continue;

            readings.Add((name, celsius));
        }

        if (readings.Count == 0)
            return null;

        // Если прошивка назвала зону процессором — доверяем ей, иначе берём самую горячую.
        var cpuZones = readings
            .Where(r => r.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var pool = cpuZones.Count > 0 ? cpuZones : readings;
        return pool.Max(r => r.Celsius);
    }

    /// <summary>Температура GPU через nvidia-smi (только NVIDIA). null — недоступно.</summary>
    public static async Task<double?> GetGpuTemperatureAsync()
    {
        try
        {
            var smi = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvidia-smi.exe");

            if (!File.Exists(smi))
                smi = "nvidia-smi";

            var result = await CommandRunner.RunProcessAsync(smi,
                "--query-gpu=temperature.gpu --format=csv,noheader,nounits");

            if (result.ExitCode != 0)
                return null;

            var text = result.Output.Trim().Split('\n').FirstOrDefault()?.Trim();
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ManagementObjectSearcher Query(ManagementScope scope, string wql) =>
        new(scope, new ObjectQuery(wql));

    private static double SumPerf(string className, string property, string filterProperty, string filterValue)
    {
        try
        {
            using var query = Query(CimScope,
                $"SELECT {property} FROM {className} WHERE {filterProperty}='{filterValue}'");

            double sum = 0;
            using var results = query.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                    sum += ToDouble(item[property]);
            }

            return sum;
        }
        catch
        {
            return 0;
        }
    }

    private static double SumWhere(string className, string property, string filterProperty, string contains)
    {
        try
        {
            using var query = Query(CimScope, $"SELECT {property},{filterProperty} FROM {className}");

            double sum = 0;
            using var results = query.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    var name = item[filterProperty]?.ToString() ?? string.Empty;
                    if (name.Contains(contains, StringComparison.OrdinalIgnoreCase))
                        sum += ToDouble(item[property]);
                }
            }

            return sum;
        }
        catch
        {
            // класс счётчиков может отсутствовать на старых сборках Windows
            return 0;
        }
    }

    private static double ToDouble(object? value) =>
        double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);
}
