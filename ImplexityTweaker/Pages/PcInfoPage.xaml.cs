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
        _ = RefreshAsync();
    }

    private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e) => _ = RefreshAsync();

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        // Показываем индикатор загрузки, чтобы пользователь понимал, что идёт сбор данных.
        try { LoadingIndicator.Visibility = System.Windows.Visibility.Visible; } catch { }

        // WMI-запросы выполняются в фоновом потоке, чтобы не блокировать UI.
        var (os, cpu, gpu, mem, sys, bios, disk, net) = await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                return (
                    OsInfo(),
                    CpuInfo(),
                    GpuInfo(),
                    MemoryInfo(),
                    SystemInfo(),
                    BiosInfo(),
                    DisksInfo(),
                    NetworkInfo()
                );
            }
            catch
            {
                return ("ОС: n/a", "CPU: n/a", "GPU: n/a", "Память: n/a", "Мат. плата: n/a", "BIOS/UEFI: n/a", "Диски: n/a", "Сеть: n/a");
            }
        });

        // Обновляем UI в потоке диспетчера.
        try
        {
            SummaryMachine.Text = Environment.MachineName;
            SummaryUser.Text = Environment.UserName;

            OsCard.Text = os;
            CpuCard.Text = cpu;
            GpuCard.Text = gpu;
            MemoryCard.Text = mem;
            SystemCard.Text = sys;
            BiosCard.Text = bios;
            DiskCard.Text = disk;
            NetworkCard.Text = net;
        }
        catch
        {
            // keep whatever was shown before
        }
        finally
        {
            try { LoadingIndicator.Visibility = System.Windows.Visibility.Collapsed; } catch { }
        }
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

                // Версия вида "22H2", "23H2", "24H2" и т.п. берётся из реестра (DisplayVersion), т.к. в WMI её нет.
                string displayVersion = "";
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "";
                }
                catch { /* ignore */ }

                var versionLabel = !string.IsNullOrWhiteSpace(displayVersion) ? displayVersion : version;
                return $"ОС: {caption}\nВерсия: {versionLabel}\nСборка: {build}\nАрхитектура: {arch}\nИзготовитель: {manuf}\nЗарегистрированный: {regUser}";
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
        var sb = new StringBuilder();
        var hasTotal = false;
        try
        {
            using var q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (ManagementObject o in q.Get())
            {
                // TotalPhysicalMemory — в байтах; FreePhysicalMemory — в килобайтах.
                if (ulong.TryParse(o["TotalPhysicalMemory"]?.ToString(), out var totalBytes))
                {
                    hasTotal = true;
                    var freeBytes = 0UL;
                    if (ulong.TryParse(o["FreePhysicalMemory"]?.ToString(), out var freeKb))
                        freeBytes = freeKb * 1024;
                    sb.AppendLine("Память:");
                    sb.AppendLine($"  Всего: {FormatBytes(totalBytes)}");
                    sb.AppendLine($"  Свободно: {FormatBytes(freeBytes)}");
                }
                break;
            }
        }
        catch { /* ignore */ }

        if (!hasTotal)
        {
            // fallback: Win32_ComputerSystem.TotalPhysicalMemory — тоже в байтах.
            try
            {
                using var q = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject o in q.Get())
                {
                    if (ulong.TryParse(o["TotalPhysicalMemory"]?.ToString(), out var bytes))
                    {
                        hasTotal = true;
                        sb.AppendLine("Память:");
                        sb.AppendLine($"  Всего: {FormatBytes(bytes)}");
                    }
                    break;
                }
            }
            catch { /* ignore */ }
        }

        // Сведения о каждой плашке ОЗУ — всегда добавляем (best-effort),
        // независимо от того, получилось ли вычислить суммарный объём.
        sb.AppendLine(RamSticksInfo());

        var result = sb.ToString().TrimEnd();
        return result.Length > 0 ? result : "Память: n/a";
    }
    private static string RamSticksInfo()
{
    try
    {
        // Надёжные поля: слот, объём, частота, производитель, парт-номер.
        using var q = new ManagementObjectSearcher("SELECT DeviceLocator, Capacity, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
        var sb = new StringBuilder();
        var idx = 1;
        foreach (ManagementObject o in q.Get())
        {
            var locator = o["DeviceLocator"]?.ToString() ?? "";
            var capacityStr = o["Capacity"]?.ToString() ?? "";
            var cfgSpeed = o["ConfiguredClockSpeed"]?.ToString() ?? "";
            var speedStr = o["Speed"]?.ToString() ?? "";
            var vendor = o["Manufacturer"]?.ToString() ?? "";
            var part = o["PartNumber"]?.ToString() ?? "";

            sb.AppendLine($"Планка {idx}:");
            if (!string.IsNullOrWhiteSpace(locator))
                sb.AppendLine($"  Слот: {locator}");

            if (ulong.TryParse(capacityStr, out var capBytes))
                sb.AppendLine($"  Объём: {FormatBytes(capBytes)}");
            else if (!string.IsNullOrWhiteSpace(capacityStr))
                sb.AppendLine($"  Объём: {capacityStr}");
            else
                sb.AppendLine("  Объём: n/a");

            // ConfiguredClockSpeed (фактическая) предпочтительнее; fallback — Speed.
            var freq = !string.IsNullOrWhiteSpace(cfgSpeed) ? cfgSpeed : speedStr;
            if (!string.IsNullOrWhiteSpace(freq))
                sb.AppendLine($"  Частота: {freq} МГц");

            if (!string.IsNullOrWhiteSpace(vendor) && !vendor.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"  Производитель: {vendor}");
            if (!string.IsNullOrWhiteSpace(part) && !part.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine($"  Парт-номер: {part}");

            sb.AppendLine();
            idx++;
        }

        return idx > 1 ? sb.ToString().TrimEnd() : "  Планки: n/a";
    }
    catch { /* ignore */ }
    return "  Планки: n/a";
}

    private static string SystemInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem");
            foreach (ManagementObject o in q.Get())
            {
                var manuf = o["Manufacturer"]?.ToString() ?? "";
                var model = o["Model"]?.ToString() ?? "";
                var type = o["SystemType"]?.ToString() ?? "";

                return $"Мат. плата:\n  Производитель: {manuf}\n  Модель: {model}\n  Тип системы: {type}\n  TPM: {TpmInfo()}";
            }
        }
        catch { /* ignore */ }
        return "Мат. плата: n/a";
    }

    private static string TpmInfo()
    {
        // 1) WMI Win32_Tpm — SpecVersion + факт включения.
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftTpm");
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT SpecVersion, IsEnabled_InitialValue FROM Win32_Tpm"));
            foreach (ManagementObject o in searcher.Get())
            {
                var spec = o["SpecVersion"]?.ToString() ?? "";
                var enabled = o["IsEnabled_InitialValue"]?.ToString() ?? "";
                var ver = spec.Contains("2.0") ? "2.0"
                          : spec.Contains("1.2") ? "1.2"
                          : (string.IsNullOrWhiteSpace(spec) ? null : spec);
                var isOn = string.Equals(enabled, "True", StringComparison.OrdinalIgnoreCase) || enabled == "1";
                return ver is null
                    ? "есть (версия неизвестна)"
                    : (isOn ? $"{ver} (включён)" : $"{ver} (есть)");
            }
        }
        catch { /* ignore — переходим к реестру */ }

        // 2) Registry fallback: наличие драйвера TPM2/TPM (работает, даже если WMI недоступен).
        try
        {
            using var tpm2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM2");
            using var tpm1 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM");
            if (tpm2 != null) return "2.0 (есть)";
            if (tpm1 != null) return "1.2 (есть)";
        }
        catch { /* ignore */ }

        return "не обнаружен";
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
                var release = o["ReleaseDate"]?.ToString() ?? "";

                var rel = release;
                return $"BIOS/UEFI:\n  Производитель: {manuf}\n  Версия: {ver}\n  Дата релиза: {rel}";
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
                var mediaType = o["MediaType"]?.ToString() ?? "";
                var sizeStr = o["Size"]?.ToString() ?? "";

                sb.AppendLine("Диск:");
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
