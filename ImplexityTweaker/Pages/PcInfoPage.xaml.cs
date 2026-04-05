using System.Management;
using System.Text;
using System.Windows.Controls;
using System.Linq;
using System;

namespace ImplexityTweaker.Pages;

public partial class PcInfoPage : Page
{
    public PcInfoPage()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Компьютер: {Environment.MachineName}");
        sb.AppendLine($"Пользователь: {Environment.UserName}");
        sb.AppendLine();

        sb.AppendLine(OsInfo());
        sb.AppendLine();

        sb.AppendLine(CpuInfo());
        sb.AppendLine();

        sb.AppendLine(GpuInfo());
        sb.AppendLine();

        sb.AppendLine(MemoryInfo());
        sb.AppendLine();

        sb.AppendLine(SystemInfo());
        sb.AppendLine();

        sb.AppendLine(BiosInfo());
        sb.AppendLine();

        sb.AppendLine(DisksInfo());
        sb.AppendLine();

        sb.AppendLine(DrivesInfo());
        sb.AppendLine();

        sb.AppendLine(NetworkInfo());
        sb.AppendLine();

        sb.AppendLine("Детали приложения:");
        sb.AppendLine($".NET runtime: {Environment.Version}");

        Info.Text = sb.ToString();
    }

    private static string OsInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture, Manufacturer, RegisteredUser FROM Win32_OperatingSystem");
            foreach (ManagementObject o in q.Get())
            {
                var caption = o["Caption"]?.ToString() ?? "";
                var version = o["Version"]?.ToString() ?? "";
                var build = o["BuildNumber"]?.ToString() ?? "";
                var arch = o["OSArchitecture"]?.ToString() ?? "";
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var regUser = o["RegisteredUser"]?.ToString() ?? "";
                return $"ОС: {caption}\nВерсия: {version}\nСборка: {build}\nАрхитектура: {arch}\nИзготовитель: {manuf}\nЗарегистрированный: {regUser}";
            }
        }
        catch { /* ignore */ }

        return "ОС: n/a";
    }

    private static string CpuInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
            var first = true;
            var sb = new StringBuilder();
            foreach (ManagementObject o in q.Get())
            {
                if (!first) sb.AppendLine();
                first = false;

                var name = o["Name"]?.ToString() ?? "";
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var cores = o["NumberOfCores"]?.ToString() ?? "";
                var logical = o["NumberOfLogicalProcessors"]?.ToString() ?? "";
                var mhz = o["MaxClockSpeed"]?.ToString() ?? "";
                var l2 = o["L2CacheSize"]?.ToString() ?? "";
                var l3 = o["L3CacheSize"]?.ToString() ?? "";

                sb.AppendLine("CPU:");
                sb.AppendLine($"  Название: {name}");
                sb.AppendLine($"  Производитель: {manuf}");
                sb.AppendLine($"  Ядра / Потоки: {cores} / {logical}");
                sb.AppendLine($"  Максимальная частота: {mhz} MHz");
                if (!string.IsNullOrWhiteSpace(l2) || !string.IsNullOrWhiteSpace(l3))
                    sb.AppendLine($"  Кэш L2/L3: {l2} / {l3}");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "CPU: n/a";
        }
        catch
        {
            return "CPU: n/a";
        }
    }

    private static string GpuInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor FROM Win32_VideoController WHERE AdapterRAM IS NOT NULL");
            var first = true;
            var sb = new StringBuilder();
            foreach (ManagementObject o in q.Get())
            {
                if (!first) sb.AppendLine();
                first = false;

                var name = o["Name"]?.ToString() ?? "";
                var ramBytes = o["AdapterRAM"]?.ToString() ?? "";
                var driver = o["DriverVersion"]?.ToString() ?? "";
                var vproc = o["VideoProcessor"]?.ToString() ?? "";

                sb.AppendLine("GPU:");
                sb.AppendLine($"  Модель: {name}");
                sb.AppendLine($"  Видеопроцессор: {vproc}");
                sb.AppendLine($"  Драйвер: {driver}");
                if (ulong.TryParse(ramBytes, out var ram))
                    sb.AppendLine($"  Память: {FormatBytes(ram)}");
                else
                    sb.AppendLine("  Память: n/a");
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : "GPU: n/a";
        }
        catch
        {
            return "GPU: n/a";
        }
    }

    private static string MemoryInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject o in q.Get())
            {
                var totalKb = o["TotalPhysicalMemory"] as ulong?;
                var freeKb = o["FreePhysicalMemory"] as ulong?;
                if (totalKb is not null)
                {
                    // TotalPhysicalMemory / FreePhysicalMemory are in kilobytes on many systems; keep safe formatting.
                    // If values look small, still show raw.
                    var totalBytes = (double)totalKb.Value * 1024d;
                    return $"Память:\n  Всего: {FormatBytes((ulong)totalBytes)}\n  Свободно: {(freeKb is null ? 0UL : FormatBytes((ulong)((double)freeKb.Value * 1024d)))}";
                }
                break;
            }
        }
        catch { /* ignore */ }

        // fallback: Win32_ComputerSystem
        try
        {
            using var q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject o in q.Get())
            {
                if (ulong.TryParse(o["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    return $"Память:\n  Всего: {FormatBytes(bytes)}";
            }
        }
        catch { /* ignore */ }

        return "Память: n/a";
    }

    private static string SystemInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemType, SystemSKUNumber, TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (ManagementObject o in q.Get())
            {
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var model = o["Model"]?.ToString() ?? "";
                var type = o["SystemType"]?.ToString() ?? "";
                var sku = o["SystemSKUNumber"]?.ToString() ?? "";

                return $"Системная информация:\n  Производитель: {manuf}\n  Модель: {model}\n  Тип системы: {type}\n  SKU: {sku}";
            }
        }
        catch { /* ignore */ }
        return "Системная информация: n/a";
    }

    private static string BiosInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, SerialNumber, ReleaseDate FROM Win32_BIOS");
            foreach (ManagementObject o in q.Get())
            {
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var ver = o["SMBIOSBIOSVersion"]?.ToString() ?? "";
                var serial = o["SerialNumber"]?.ToString() ?? "";
                var release = o["ReleaseDate"]?.ToString() ?? "";

                var rel = release;
                return $"BIOS/UEFI:\n  Производитель: {manuf}\n  Версия: {ver}\n  Серийный: {serial}\n  Дата релиза: {rel}";
            }
        }
        catch { /* ignore */ }

        return "BIOS/UEFI: n/a";
    }

    private static string DisksInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Model, Manufacturer, MediaType, Size FROM Win32_DiskDrive");
            var sb = new StringBuilder();
            var any = false;
            foreach (ManagementObject o in q.Get())
            {
                any = true;
                var model = o["Model"]?.ToString() ?? "";
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var mediaType = o["MediaType"]?.ToString() ?? "";
                var sizeStr = o["Size"]?.ToString() ?? "";

                sb.AppendLine("Диск:");
                sb.AppendLine($"  Производитель: {manuf}");
                sb.AppendLine($"  Модель: {model}");
                sb.AppendLine($"  Тип: {mediaType}");
                if (ulong.TryParse(sizeStr, out var bytes))
                    sb.AppendLine($"  Объём: {FormatBytes(bytes)}");
                else
                    sb.AppendLine("  Объём: n/a");
                sb.AppendLine();
            }

            if (!any) return "Диски: n/a";
            return sb.ToString().TrimEnd();
        }
        catch { /* ignore */ }
        return "Диски: n/a";
    }

    private static string DrivesInfo()
    {
        // logical drives with free space
        return WmiDisk();
    }

    private static string NetworkInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Description, MACAddress, IPEnabled, DHCPEnabled, IPAddress, IPSubnet FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled = TRUE");
            var sb = new StringBuilder();
            var any = false;
            foreach (ManagementObject o in q.Get())
            {
                any = true;
                var desc = o["Description"]?.ToString() ?? "";
                var mac = o["MACAddress"]?.ToString() ?? "";
                var dhcp = (bool?)(o["DHCPEnabled"] as bool?) ?? false;
                sb.AppendLine("Сеть:");
                sb.AppendLine($"  Адаптер: {desc}");
                sb.AppendLine($"  MAC: {mac}");
                sb.AppendLine($"  DHCP: {(dhcp ? "включен" : "выключен")}");

                var ipArr = o["IPAddress"] as Array;
                if (ipArr != null)
                {
                    var ips = ipArr.Cast<object>().Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    if (ips.Length > 0)
                        sb.AppendLine($"  IP: {string.Join(", ", ips)}");
                }

                sb.AppendLine();
            }

            if (!any) return "Сеть: n/a";
            return sb.ToString().TrimEnd();
        }
        catch { /* ignore */ }
        return "Сеть: n/a";
    }

    private static string FormatBytes(ulong bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var idx = 0;
        while (value >= 1024d && idx < units.Length - 1)
        {
            value /= 1024d;
            idx++;
        }
        return $"{value:0.##} {units[idx]}";
    }

    private static string Wmi(string cls, string prop)
    {
        try
        {
            using var q = new ManagementObjectSearcher($"SELECT {prop} FROM {cls}");
            foreach (ManagementObject o in q.Get())
                return o[prop]?.ToString() ?? "";
        }
        catch { /* ignore */ }
        return $"{cls}: n/a";
    }

    private static string WmiDisk()
    {
        try
        {
            var sb = new StringBuilder();
            using var q = new ManagementObjectSearcher("SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
            foreach (ManagementObject o in q.Get())
            {
                var dev = o["DeviceID"]?.ToString() ?? "";
                var sizeObj = o["Size"];
                var freeObj = o["FreeSpace"];
                if (ulong.TryParse(sizeObj?.ToString(), out var sizeBytes) && ulong.TryParse(freeObj?.ToString(), out var freeBytes))
                {
                    sb.AppendLine($"Диск {dev}: размер {FormatBytes(sizeBytes)}, свободно {FormatBytes(freeBytes)}");
                }
                else
                {
                    sb.AppendLine($"Диск {dev}: размер {sizeObj}, свободно {freeObj}");
                }
            }
            return sb.ToString();
        }
        catch
        {
            return "Диски: n/a";
        }
    }
}
