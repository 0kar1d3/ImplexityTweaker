using System.Management;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Linq;
using System;
using Microsoft.Win32;
using ImplexityTweaker.Services;

namespace ImplexityTweaker.Pages;

public partial class PcInfoPage : Page
{
    public PcInfoPage()
    {
        InitializeComponent();
        _ = RefreshAsync();
        InitializeMonitoring();
        Loaded += (_, _) => _monitorTimer.Start();
        Unloaded += (_, _) => _monitorTimer.Stop();
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
                return (LocalizationService.T("ОС: n/a"), "CPU: n/a", "GPU: n/a", LocalizationService.T("Память: n/a"), LocalizationService.T("Мат. плата: n/a"), "BIOS/UEFI: n/a", LocalizationService.T("Диски: n/a"), LocalizationService.T("Сеть: n/a"));
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
                return LocalizationService.TF(
                    "ОС: {0}\nВерсия: {1}\nСборка: {2}\nАрхитектура: {3}\nИзготовитель: {4}\nЗарегистрированный: {5}",
                    caption, versionLabel, build, arch, manuf, regUser);
            }
        }
        catch { /* ignore */ }

        return LocalizationService.T("ОС: n/a");
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

                sb.AppendLine(LocalizationService.T("CPU:"));
                sb.AppendLine(LocalizationService.TF("  Название: {0}", name));
                sb.AppendLine(LocalizationService.TF("  Производитель: {0}", manuf));
                sb.AppendLine(LocalizationService.TF("  Ядра / Потоки: {0} / {1}", cores, logical));
                sb.AppendLine(LocalizationService.TF("  Максимальная частота: {0} MHz", mhz));
                if (!string.IsNullOrWhiteSpace(l2) || !string.IsNullOrWhiteSpace(l3))
                    sb.AppendLine(LocalizationService.TF("  Кэш L2/L3: {0} / {1}", l2, l3));
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : LocalizationService.T("CPU: n/a");
        }
        catch
        {
            return LocalizationService.T("CPU: n/a");
        }
    }

    private static string GpuInfo()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Name, AdapterRAM, DriverVersion, VideoProcessor, PNPDeviceID FROM Win32_VideoController");
            var first = true;
            var sb = new StringBuilder();
            foreach (ManagementObject o in q.Get())
            {
                if (!first) sb.AppendLine();
                first = false;

                var name = o["Name"]?.ToString() ?? "";
                var driver = o["DriverVersion"]?.ToString() ?? "";
                var vproc = o["VideoProcessor"]?.ToString() ?? "";
                var pnpId = o["PNPDeviceID"]?.ToString() ?? "";

                sb.AppendLine(LocalizationService.T("GPU:"));
                sb.AppendLine(LocalizationService.TF("  Модель: {0}", name));
                sb.AppendLine(LocalizationService.TF("  Видеопроцессор: {0}", vproc));
                sb.AppendLine(LocalizationService.TF("  Драйвер: {0}", driver));

                // Видеопамять: AdapterRAM (UInt32) в WMI ограничен 4 ГБ, поэтому для карт
                // с большим объёмом берём 64-битный HardwareInformation.qwMemorySize из реестра драйвера.
                var vramBytes = GetVideoRam(pnpId, o["AdapterRAM"]?.ToString());
                if (vramBytes > 0)
                    sb.AppendLine(LocalizationService.TF("  Память: {0}", FormatBytes(vramBytes)));
                else
                    sb.AppendLine(LocalizationService.T("  Память: n/a"));
            }

            return sb.Length > 0 ? sb.ToString().TrimEnd() : LocalizationService.T("GPU: n/a");
        }
        catch
        {
            return LocalizationService.T("GPU: n/a");
        }
    }

    // Возвращает объём видеопамяти в байтах. Приоритет — реестр драйвера GPU
    // (HardwareInformation.qwMemorySize, 64 бита); fallback — AdapterRAM из WMI (до 4 ГБ).
    private static ulong GetVideoRam(string pnpDeviceId, string? adapterRamRaw)
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (baseKey != null)
            {
                foreach (var subName in baseKey.GetSubKeyNames())
                {
                    using var sub = baseKey.OpenSubKey(subName);
                    var matchId = sub?.GetValue("MatchingDeviceId")?.ToString() ?? "";
                    // MatchingDeviceId — аппаратный ID без инстанса; PNPDeviceID содержит его как префикс.
                    if (string.IsNullOrEmpty(matchId) ||
                        !pnpDeviceId.StartsWith(matchId, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var qw = sub?.GetValue("HardwareInformation.qwMemorySize");
                    // Значение приходит как REG_QWORD (ulong/long) или REG_BINARY (byte[], 8 байт).
                    if (qw is ulong qwSize && qwSize > 0)
                        return qwSize;
                    if (qw is long signedSize && signedSize > 0)
                        return (ulong)signedSize;
                    if (qw is byte[] bytes && bytes.Length >= 8)
                    {
                        var val = BitConverter.ToUInt64(bytes, 0);
                        if (val > 0) return val;
                    }
                    break;
                }
            }
        }
        catch { /* ignore */ }

        // Fallback: AdapterRAM из WMI (некорректен для карт с видеопамятью > 4 ГБ).
        if (ulong.TryParse(adapterRamRaw, out var ram) && ram > 0)
            return ram;

        return 0;
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
                    sb.AppendLine(LocalizationService.T("Память:"));
                    sb.AppendLine(LocalizationService.TF("  Всего: {0}", FormatBytes(totalBytes)));
                    sb.AppendLine(LocalizationService.TF("  Свободно: {0}", FormatBytes(freeBytes)));
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
                        sb.AppendLine(LocalizationService.T("Память:"));
                        sb.AppendLine(LocalizationService.TF("  Всего: {0}", FormatBytes(bytes)));
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
        return result.Length > 0 ? result : LocalizationService.T("Память: n/a");
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

            sb.AppendLine(LocalizationService.TF("Планка {0}:", idx));
            if (!string.IsNullOrWhiteSpace(locator))
                sb.AppendLine(LocalizationService.TF("  Слот: {0}", locator));

            if (ulong.TryParse(capacityStr, out var capBytes))
                sb.AppendLine(LocalizationService.TF("  Объём: {0}", FormatBytes(capBytes)));
            else if (!string.IsNullOrWhiteSpace(capacityStr))
                sb.AppendLine(LocalizationService.TF("  Объём: {0}", capacityStr));
            else
                sb.AppendLine(LocalizationService.T("  Объём: n/a"));

            // ConfiguredClockSpeed (фактическая) предпочтительнее; fallback — Speed.
            var freq = !string.IsNullOrWhiteSpace(cfgSpeed) ? cfgSpeed : speedStr;
            if (!string.IsNullOrWhiteSpace(freq))
                sb.AppendLine(LocalizationService.TF("  Частота: {0} МГц", freq));

            if (!string.IsNullOrWhiteSpace(vendor) && !vendor.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(LocalizationService.TF("  Производитель: {0}", vendor));
            if (!string.IsNullOrWhiteSpace(part) && !part.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                sb.AppendLine(LocalizationService.TF("  Парт-номер: {0}", part));

            sb.AppendLine();
            idx++;
        }

        return idx > 1 ? sb.ToString().TrimEnd() : LocalizationService.T("  Планки: n/a");
    }
    catch { /* ignore */ }
    return LocalizationService.T("  Планки: n/a");
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

                return LocalizationService.TF(
                    "Мат. плата:\n  Производитель: {0}\n  Модель: {1}\n  Тип системы: {2}\n  TPM: {3}",
                    manuf, model, type, TpmInfo());
            }
        }
        catch { /* ignore */ }
        return LocalizationService.T("Мат. плата: n/a");
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
                    ? LocalizationService.T("есть (версия неизвестна)")
                    : (isOn
                        ? LocalizationService.TF("{0} (включён)", ver)
                        : LocalizationService.TF("{0} (есть)", ver));
            }
        }
        catch { /* ignore — переходим к реестру */ }

        // 2) Registry fallback: наличие драйвера TPM2/TPM (работает, даже если WMI недоступен).
        try
        {
            using var tpm2 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM2");
            using var tpm1 = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM");
            if (tpm2 != null) return LocalizationService.TF("{0} (есть)", "2.0");
            if (tpm1 != null) return LocalizationService.TF("{0} (есть)", "1.2");
        }
        catch { /* ignore */ }

        return LocalizationService.T("не обнаружен");
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
                return LocalizationService.TF(
                    "BIOS/UEFI:\n  Производитель: {0}\n  Версия: {1}\n  Дата релиза: {2}",
                    manuf, ver, rel);
            }
        }
        catch { /* ignore */ }

        return LocalizationService.T("BIOS/UEFI: n/a");
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

                sb.AppendLine(LocalizationService.T("Диск:"));
                sb.AppendLine(LocalizationService.TF("  Модель: {0}", model));
                sb.AppendLine(LocalizationService.TF("  Тип: {0}", mediaType));
                if (ulong.TryParse(sizeStr, out var bytes))
                    sb.AppendLine(LocalizationService.TF("  Объём: {0}", FormatBytes(bytes)));
                else
                    sb.AppendLine(LocalizationService.T("  Объём: n/a"));
                sb.AppendLine();
            }

            if (!any) return LocalizationService.T("Диски: n/a");
            return sb.ToString().TrimEnd();
        }
        catch { /* ignore */ }
        return LocalizationService.T("Диски: n/a");
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
                sb.AppendLine(LocalizationService.T("Сеть:"));
                sb.AppendLine(LocalizationService.TF("  Адаптер: {0}", desc));
                sb.AppendLine(LocalizationService.TF("  MAC: {0}", mac));
                sb.AppendLine(LocalizationService.TF("  DHCP: {0}", LocalizationService.T(dhcp ? "включен" : "выключен")));

                var ipArr = o["IPAddress"] as Array;
                if (ipArr != null)
                {
                    var ips = ipArr.Cast<object>().Select(x => x?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                    if (ips.Length > 0)
                        sb.AppendLine(LocalizationService.TF("  IP: {0}", string.Join(", ", ips)));
                }

                sb.AppendLine();
            }

            if (!any) return LocalizationService.T("Сеть: n/a");
            return sb.ToString().TrimEnd();
        }
        catch { /* ignore */ }
        return LocalizationService.T("Сеть: n/a");
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
                    sb.AppendLine(LocalizationService.TF("Диск {0}: размер {1}, свободно {2}", dev, FormatBytes(sizeBytes), FormatBytes(freeBytes)));
                }
                else
                {
                    sb.AppendLine(LocalizationService.TF("Диск {0}: размер {1}, свободно {2}", dev, sizeObj?.ToString() ?? "", freeObj?.ToString() ?? ""));
                }
            }
            return sb.ToString();
        }
        catch
        {
            return LocalizationService.T("Диски: n/a");
        }
    }

    // ===================== Мониторинг в реальном времени =====================

    private const int MonitorHistoryLength = 60;
    private const double MonitorSparkHeight = 44;

    private readonly DispatcherTimer _monitorTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private MonitorRow _monitorCpu = null!;
    private MonitorRow _monitorRam = null!;
    private MonitorRow _monitorGpu = null!;
    private MonitorRow _monitorDisk = null!;
    private MonitorRow _monitorNet = null!;
    private int _monitorTempTick;
    private double _monitorNetMax = 10;
    private bool _monitorBusy;

    private sealed class MonitorRow
    {
        public TextBlock Value = null!;
        public Polyline Line = null!;
        public Polygon Area = null!;
        public Grid Host = null!;
        public readonly Queue<double> History = new();
    }

    private void InitializeMonitoring()
    {
        _monitorCpu = AddMonitorRow(LocalizationService.T("Процессор"));
        _monitorRam = AddMonitorRow(LocalizationService.T("Память"));
        _monitorGpu = AddMonitorRow(LocalizationService.T("Видеокарта"));
        _monitorDisk = AddMonitorRow(LocalizationService.T("Диск"));
        _monitorNet = AddMonitorRow(LocalizationService.T("Сеть"));

        _monitorTimer.Tick += async (_, _) => await MonitorTickAsync();
    }

    private MonitorRow AddMonitorRow(string title)
    {
        var accent = (Brush)Application.Current.Resources["App.Accent"];
        var iconAccent = (Brush)Application.Current.Resources["App.IconAccent"];
        var border = (Brush)Application.Current.Resources["App.Border"];

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });

        var label = new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("ItemTitleText"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        // Заливка под линией графика делает его заметным на светлых и тёмных темах.
        var area = new Polygon { Fill = accent, Opacity = 0.16 };

        var line = new Polyline
        {
            Stroke = iconAccent,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };

        var spark = new Grid
        {
            Height = MonitorSparkHeight,
            ClipToBounds = true,
            Margin = new Thickness(12, 0, 12, 0)
        };
        spark.Children.Add(area);
        spark.Children.Add(line);

        var sparkBox = new Border
        {
            Child = spark,
            BorderBrush = border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(sparkBox, 1);
        grid.Children.Add(sparkBox);

        var value = new TextBlock
        {
            Style = (Style)FindResource("ItemTitleText"),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Text = "—"
        };
        Grid.SetColumn(value, 2);
        grid.Children.Add(value);

        var tile = new Border
        {
            Background = (Brush)Application.Current.Resources["App.SurfaceAlt"],
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid
        };

        MonitorRowsHost.Children.Add(tile);
        return new MonitorRow { Value = value, Line = line, Area = area, Host = spark };
    }

    private async Task MonitorTickAsync()
    {
        // WMI бывает медленным: не допускаем наложения тиков друг на друга.
        if (_monitorBusy)
            return;

        _monitorBusy = true;
        try
        {
            var snapshot = await MonitoringService.GetSnapshotAsync();
            if (snapshot != null)
            {
                PushMonitorValue(_monitorCpu, snapshot.CpuPercent, $"{snapshot.CpuPercent:0} %", 100);
                PushMonitorValue(_monitorRam, snapshot.RamUsedGb,
                    LocalizationService.TF("{0:0.0} / {1:0.0} ГБ", snapshot.RamUsedGb, snapshot.RamTotalGb),
                    Math.Max(1, snapshot.RamTotalGb));
                PushMonitorValue(_monitorGpu, snapshot.GpuPercent, $"{snapshot.GpuPercent:0} %", 100);
                PushMonitorValue(_monitorDisk, snapshot.DiskPercent, $"{snapshot.DiskPercent:0} %", 100);
                PushMonitorValue(_monitorNet, snapshot.NetMbps,
                    LocalizationService.TF("{0:0.0} Мбит/с", snapshot.NetMbps),
                    Math.Max(10, _monitorNetMax));
            }

            // Температуры спрашиваем реже — запросы к датчикам дорогие.
            _monitorTempTick++;
            if (_monitorTempTick % 5 == 1)
            {
                var cpu = await MonitoringService.GetCpuTemperatureAsync();
                MonitorCpuTempText.Text = cpu.HasValue
                    ? $"{cpu.Value:0.0} °C"
                    : LocalizationService.T("нет данных");

                var gpu = await MonitoringService.GetGpuTemperatureAsync();
                MonitorGpuTempText.Text = gpu.HasValue
                    ? $"{gpu.Value:0} °C"
                    : LocalizationService.T("нет данных");
            }
        }
        catch
        {
            // не мешаем интерфейсу при сбое WMI
        }
        finally
        {
            _monitorBusy = false;
        }
    }

    private void PushMonitorValue(MonitorRow row, double value, string text, double max)
    {
        if (ReferenceEquals(row, _monitorNet) && value > _monitorNetMax)
            _monitorNetMax = value;

        row.Value.Text = text;

        row.History.Enqueue(value);
        while (row.History.Count > MonitorHistoryLength)
            row.History.Dequeue();

        var width = row.Host.ActualWidth;
        if (width <= 1)
            width = 300;

        var count = row.History.Count;
        if (count < 2)
        {
            row.Line.Points = new PointCollection();
            row.Area.Points = new PointCollection();
            return;
        }

        var step = width / (count - 1);
        var points = new PointCollection();
        var areaPoints = new PointCollection();
        var index = 0;

        foreach (var item in row.History)
        {
            var normalized = max <= 0 ? 0 : Math.Clamp(item / max, 0, 1);
            var y = MonitorSparkHeight - 2 - normalized * (MonitorSparkHeight - 6);
            var point = new Point(index * step, y);
            points.Add(point);
            areaPoints.Add(point);
            index++;
        }

        // Замыкаем контур заливки по нижнему краю поля графика.
        areaPoints.Add(new Point((count - 1) * step, MonitorSparkHeight));
        areaPoints.Add(new Point(0, MonitorSparkHeight));

        row.Line.Points = points;
        row.Area.Points = areaPoints;
    }
}
