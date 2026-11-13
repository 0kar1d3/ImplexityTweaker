using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ImplexityTweaker.Services;

/// <summary>
/// Генерация и парсинг отчёта о состоянии аккумулятора и питания (powercfg /batteryreport).
/// </summary>
public static class BatteryReportService
{
    public sealed record BatteryReportInfo(
        bool HasBattery,
        string Name,
        string Manufacturer,
        string Chemistry,
        long DesignCapacityMwh,
        long FullChargeCapacityMwh,
        double WearPercent,
        double HealthPercent,
        int? CycleCount,
        int? ChargePercent,
        string StatusText,
        string HtmlReportPath);

    public static string ReportsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Implexity", "reports");

    public static async Task<BatteryReportInfo> GenerateAndParseAsync()
    {
        Directory.CreateDirectory(ReportsFolder);
        var xmlPath = Path.Combine(ReportsFolder, "battery-report.xml");
        var htmlPath = Path.Combine(ReportsFolder, "battery-report.html");

        try
        {
            if (File.Exists(xmlPath)) File.Delete(xmlPath);
            if (File.Exists(htmlPath)) File.Delete(htmlPath);
        }
        catch { }

        // Генерируем XML для точного парсинга и HTML для открытия в браузере
        await CommandRunner.RunProcessAsync("powercfg.exe", $"/batteryreport /output \"{xmlPath}\" /xml");
        await CommandRunner.RunProcessAsync("powercfg.exe", $"/batteryreport /output \"{htmlPath}\"");

        return await Task.Run(() => ParseReport(xmlPath, htmlPath));
    }

    private static BatteryReportInfo ParseReport(string xmlPath, string htmlPath)
    {
        int? currentCharge = null;
        string statusText = LocalizationService.T("От сети");

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT EstimatedChargeRemaining, BatteryStatus, Name FROM Win32_Battery");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (int.TryParse(obj["EstimatedChargeRemaining"]?.ToString(), out var chg))
                    currentCharge = chg;

                if (ushort.TryParse(obj["BatteryStatus"]?.ToString(), out var st))
                {
                    statusText = st switch
                    {
                        1 => LocalizationService.T("Разряжается (от батареи)"),
                        2 => LocalizationService.T("Подключено к сети"),
                        3 => LocalizationService.T("Полностью заряжена"),
                        6 or 7 or 8 or 9 => LocalizationService.T("Заряжается"),
                        _ => LocalizationService.T("Активна")
                    };
                }
                break;
            }
        }
        catch { }

        try
        {
            if (File.Exists(xmlPath))
            {
                var doc = XDocument.Load(xmlPath);
                var batNode = doc.Descendants().FirstOrDefault(e => e.Name.LocalName.Equals("Battery", StringComparison.OrdinalIgnoreCase));
                if (batNode != null)
                {
                    string GetVal(string localName) =>
                        batNode.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ?? "";

                    var id = GetVal("Id");
                    var manuf = GetVal("Manufacturer");
                    var chem = GetVal("Chemistry");
                    long.TryParse(GetVal("DesignCapacity"), out var design);
                    long.TryParse(GetVal("FullChargeCapacity"), out var full);
                    int? cycles = int.TryParse(GetVal("CycleCount"), out var c) && c > 0 ? c : null;

                    if (design > 0 || full > 0)
                    {
                        double health = design > 0 && full > 0
                            ? Math.Clamp(Math.Round(full * 100.0 / design, 1), 0, 100)
                            : 100;
                        double wear = Math.Round(100.0 - health, 1);

                        return new BatteryReportInfo(
                            HasBattery: true,
                            Name: string.IsNullOrWhiteSpace(id) ? "System Battery" : id,
                            Manufacturer: string.IsNullOrWhiteSpace(manuf) ? "—" : manuf,
                            Chemistry: string.IsNullOrWhiteSpace(chem) ? "Li-Ion" : chem,
                            DesignCapacityMwh: design,
                            FullChargeCapacityMwh: full,
                            WearPercent: wear,
                            HealthPercent: health,
                            CycleCount: cycles,
                            ChargePercent: currentCharge,
                            StatusText: statusText,
                            HtmlReportPath: htmlPath);
                    }
                }
            }
        }
        catch { }

        return new BatteryReportInfo(
            HasBattery: false,
            Name: LocalizationService.T("Аккумулятор не обнаружен (стационарный ПК)"),
            Manufacturer: "—",
            Chemistry: "—",
            DesignCapacityMwh: 0,
            FullChargeCapacityMwh: 0,
            WearPercent: 0,
            HealthPercent: 0,
            CycleCount: null,
            ChargePercent: null,
            StatusText: LocalizationService.T("Питание от сети AC"),
            HtmlReportPath: htmlPath);
    }

    public static void OpenHtmlReport(string htmlPath)
    {
        if (File.Exists(htmlPath))
        {
            Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        }
    }
}
