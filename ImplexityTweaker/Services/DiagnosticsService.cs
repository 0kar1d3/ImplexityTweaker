namespace ImplexityTweaker.Services;

/// <summary>Диагностика и ремонт системы: проверки, драйверы, точки восстановления.</summary>
public static class DiagnosticsService
{
    public sealed record ProblemDevice(string Name, string DeviceClass, int ErrorCode);

    public sealed record DisplayDriver(string Model, string Version, string Date);

    public sealed record RestorePointInfo(int SequenceNumber, string Description, string Created);

    // ===================== Проверки (для окна прогресса) =====================

    public static async Task CheckImageAsync()
    {
        await CommandRunner.RunProcessAsync("dism.exe", "/Online /Cleanup-Image /CheckHealth");
    }

    public static async Task CheckDiskAsync()
    {
        // Только проверка, без исправления: /scan быстро сканирует системный том.
        await CommandRunner.RunProcessAsync("chkdsk.exe", "C: /scan");
    }

    public static async Task SfcAsync()
    {
        await CommandRunner.RunProcessAsync("sfc.exe", "/scannow");
    }

    public static async Task CheckBootAsync()
    {
        await CommandRunner.RunProcessAsync("bcdedit.exe", "/enum");
    }

    public static async Task ScanSystemFilesAsync()
    {
        // Целостность компонентов хранилища (WinSxS), без удаления.
        await CommandRunner.RunProcessAsync("dism.exe", "/Online /Cleanup-Image /ScanHealth");
    }

    // ===================== Драйверы =====================

    public static async Task<IReadOnlyList<ProblemDevice>> GetProblemDevicesAsync()
    {
        const string script = @"
Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue |
  Where-Object { $_.ConfigManagerErrorCode -ne 0 } |
  ForEach-Object { Write-Output ($_.Name + ';' + $_.PNPClass + ';' + $_.ConfigManagerErrorCode) }
";
        var r = await CommandRunner.RunPowerShellAsync(script);

        var list = new List<ProblemDevice>();
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.Trim();
            var parts = line.Split(';');
            if (parts.Length < 3)
                continue;

            if (!int.TryParse(parts[2].Trim(), out var code))
                continue;

            list.Add(new ProblemDevice(parts[0].Trim(), parts[1].Trim(), code));
        }
        return list;
    }

    public static async Task<IReadOnlyList<DisplayDriver>> GetDisplayDriversAsync()
    {
        const string script = @"
Get-CimInstance Win32_VideoController -ErrorAction SilentlyContinue |
  ForEach-Object { Write-Output ($_.Name + ';' + $_.DriverVersion + ';' + $_.DriverDate) }
";
        var r = await CommandRunner.RunPowerShellAsync(script);

        var list = new List<DisplayDriver>();
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.Trim();
            var parts = line.Split(';');
            if (parts.Length < 3 || parts[0].Length == 0)
                continue;

            var date = parts[2].Trim();
            list.Add(new DisplayDriver(parts[0].Trim(), parts[1].Trim(), date));
        }
        return list;
    }

    // ===================== Точки восстановления =====================

    public static async Task<IReadOnlyList<RestorePointInfo>> GetRestorePointsAsync()
    {
        const string script = @"
Get-ComputerRestorePoint -ErrorAction SilentlyContinue |
  ForEach-Object { Write-Output ($_.SequenceNumber.ToString() + ';' + $_.Description + ';' + $_.ConvertToDateTime($_.CreationTime).ToString('yyyy-MM-dd HH:mm')) }
";
        var r = await CommandRunner.RunPowerShellAsync(script);

        var list = new List<RestorePointInfo>();
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.Trim();
            var parts = line.Split(';');
            if (parts.Length < 3)
                continue;

            if (!int.TryParse(parts[0].Trim(), out var seq))
                continue;

            list.Add(new RestorePointInfo(seq, parts[1].Trim(), parts[2].Trim()));
        }

        // Самые новые точки — сверху.
        list.Sort((a, b) => b.SequenceNumber.CompareTo(a.SequenceNumber));
        return list;
    }

    /// <summary>
    /// Создаёт точку восстановления и проверяет, что она действительно появилась.
    /// Частота создания обнуляется: по умолчанию Windows делает точки не чаще
    /// одного раза в сутки и в остальных случаях молча игнорирует запрос.
    /// </summary>
    public static async Task<bool> CreateRestorePointAsync(string description)
    {
        var safeDescription = description.Replace("'", "''");
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
Enable-ComputerRestore -Drive 'C:\' | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name 'SystemRestorePointCreationFrequency' -Value 0 -PropertyType DWord -Force | Out-Null

$before = @(Get-ComputerRestorePoint).Count
Checkpoint-Computer -Description '{safeDescription}' -RestorePointType 'MODIFY_SETTINGS' | Out-Null
$after = @(Get-ComputerRestorePoint).Count

if ($after -gt $before) {{ Write-Output 'created' }} else {{ Write-Output 'failed' }}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        return r.Output.Contains("created", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Откат к точке восстановления (требуется перезагрузка).</summary>
    public static async Task RestoreToPointAsync(int sequenceNumber)
    {
        var script = $@"
$ErrorActionPreference = 'Stop'
Restore-Computer -RestorePoint {sequenceNumber}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }

    public static async Task EnableSystemProtectionAsync()
    {
        const string script = @"
$ErrorActionPreference = 'SilentlyContinue'
Enable-ComputerRestore -Drive 'C:\'
";
        await CommandRunner.RunPowerShellAsync(script);
    }
}
