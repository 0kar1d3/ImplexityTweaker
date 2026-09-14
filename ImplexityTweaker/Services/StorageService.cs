using System.Globalization;

namespace ImplexityTweaker.Services;

/// <summary>
/// Состояние накопителей: тип, оценка здоровья (в процентах), температура,
/// износ, наработка и ошибки. Данные берутся из Get-PhysicalDisk и
/// Get-StorageReliabilityCounter, при возможности — из WMI S.M.A.R.T.
/// </summary>
public static class StorageService
{
    /// <summary>Качество накопителя по оценке здоровья.</summary>
    public enum DiskGrade
    {
        Unknown,
        Excellent,
        Normal,
        Poor,
        Terrible
    }

    /// <summary>Одна запись о накопителе. null — диск не отдал значение.</summary>
    public sealed record DiskHealth(
        string Name,
        string Media,
        string StatusWord,
        double SizeGb,
        int? HealthPercent,
        DiskGrade Grade,
        int? WearPercent,
        int? Temperature,
        int? TemperatureMax,
        long? PowerOnHours,
        long? ReadErrors,
        long? WriteErrors,
        bool? PredictFailure);

    public static async Task<IReadOnlyList<DiskHealth>> GetHealthAsync()
    {
        const string script = @"
$fail = @{}
try {
    Get-WmiObject -Namespace root\wmi -Class MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue |
        ForEach-Object { if ($_.InstanceName) { $fail[$_.InstanceName] = [bool]$_.PredictFailure } }
} catch {}
Get-PhysicalDisk -ErrorAction SilentlyContinue | ForEach-Object {
    $d = $_
    $c = $null
    try { $c = $d | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue } catch {}
    $predict = ''
    if ($fail.Count -gt 0 -and $d.DeviceId) {
        foreach ($k in $fail.Keys) {
            if ($k -like ('*' + $d.DeviceId + '*')) { $predict = $fail[$k]; break }
        }
    }
    $temp = ''
    $tmax = ''
    $wear = ''
    $hours = ''
    $errR = ''
    $errW = ''
    if ($c) {
        if ($null -ne $c.Temperature) { $temp = $c.Temperature }
        if ($null -ne $c.TemperatureMax) { $tmax = $c.TemperatureMax }
        if ($null -ne $c.Wear) { $wear = $c.Wear }
        if ($null -ne $c.PowerOnHours) { $hours = $c.PowerOnHours }
        if ($null -ne $c.ReadErrorsTotal) { $errR = $c.ReadErrorsTotal }
        if ($null -ne $c.WriteErrorsTotal) { $errW = $c.WriteErrorsTotal }
    }
    $size = [math]::Round($d.Size / 1GB, 1)
    Write-Output ($d.FriendlyName + ';' + $d.MediaType + ';' + $d.HealthStatus + ';' + $size + ';' + $temp + ';' + $tmax + ';' + $wear + ';' + $hours + ';' + $errR + ';' + $errW + ';' + $predict)
}
";
        var r = await CommandRunner.RunPowerShellAsync(script);

        var list = new List<DiskHealth>();
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.Trim();
            var parts = line.Split(';');
            if (parts.Length < 11 || parts[0].Length == 0)
                continue;

            var status = parts[2].Trim();
            var wear = ParseInt(parts[6]);
            var readErrors = ParseLong(parts[8]);
            var writeErrors = ParseLong(parts[9]);
            var predict = ParseBool(parts[10]);

            var health = EvaluateHealth(status, wear, predict, readErrors, writeErrors);

            list.Add(new DiskHealth(
                parts[0].Trim(),
                NormalizeMedia(parts[1]),
                StatusWord(status),
                ParseDouble(parts[3]),
                health,
                GradeOf(health),
                wear,
                PlausibleTemp(ParseInt(parts[4])),
                PlausibleTemp(ParseInt(parts[5])),
                ParseLong(parts[7]),
                readErrors,
                writeErrors,
                predict));
        }

        return list;
    }

    /// <summary>
    /// Оценка здоровья 0–100 %: главный показатель — износ, дополнительно
    /// учитываются состояние диска и SMART-прогноз. null — данных нет.
    /// </summary>
    private static int? EvaluateHealth(string status, int? wear, bool? predict, long? readErrors, long? writeErrors)
    {
        var hasData = !string.IsNullOrWhiteSpace(status)
            || wear.HasValue || predict.HasValue || readErrors.HasValue || writeErrors.HasValue;
        if (!hasData)
            return null;

        var health = wear.HasValue
            ? 100 - Math.Clamp(wear.Value, 0, 100)
            : status.ToLowerInvariant() switch
            {
                "warning" => 65,
                "unhealthy" => 25,
                _ => 100
            };

        if (status.Equals("Unhealthy", StringComparison.OrdinalIgnoreCase))
            health = Math.Min(health, 25);
        else if (status.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            health = Math.Min(health, 65);

        if (predict == true)
            health = Math.Min(health, 20);

        var errors = (readErrors ?? 0) + (writeErrors ?? 0);
        if (errors > 0)
            health -= (int)Math.Min(20, errors * 2);

        return Math.Clamp(health, 0, 100);
    }

    /// <summary>Оценка «отлично / нормально / плохо / ужасно» по проценту здоровья.</summary>
    public static DiskGrade GradeOf(int? health) => health switch
    {
        null => DiskGrade.Unknown,
        >= 90 => DiskGrade.Excellent,
        >= 70 => DiskGrade.Normal,
        >= 40 => DiskGrade.Poor,
        _ => DiskGrade.Terrible
    };

    /// <summary>Состояние диска в виде ключа локализации (переводится при показе).</summary>
    private static string StatusWord(string status) => status.ToLowerInvariant() switch
    {
        "healthy" => "Здоров",
        "warning" => "Предупреждение",
        "unhealthy" => "Неисправен",
        _ => "Неизвестно"
    };

    private static string NormalizeMedia(string media) =>
        string.IsNullOrWhiteSpace(media) || media.Equals("Unspecified", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : media.Trim();

    private static int? ParseInt(string value) => int.TryParse(value?.Trim(), out var parsed) ? parsed : null;

    /// <summary>
    /// Температура накопителя правдоподобна (1–100 °C). Часть контроллеров
    /// отдаёт 0 или мусор — такие значения лучше не показывать вовсе.
    /// </summary>
    private static int? PlausibleTemp(int? value) => value is >= 1 and <= 100 ? value : null;

    private static long? ParseLong(string value) => long.TryParse(value?.Trim(), out var parsed) ? parsed : null;

    private static double ParseDouble(string value) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static bool? ParseBool(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" => true,
        "false" => false,
        _ => null
    };
}
