using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Состояние «Целостности памяти» (Memory Integrity / HVCI) и управление этим твиком.
/// </summary>
public static class SecurityPostureService
{
    private const string HvciKey = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";

    /// <summary>Включена ли «Целостность памяти» (Memory Integrity / HVCI).</summary>
    public static bool GetMemoryIntegrityEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(HvciKey);
            return (key?.GetValue("Enabled") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Включает/выключает «Целостность памяти». Требуется перезагрузка.</summary>
    public static async Task SetMemoryIntegrityAsync(bool enable)
    {
        var script = $@"
$ErrorActionPreference = 'Stop'
New-Item -Path 'HKLM:\{HvciKey}' -Force | Out-Null
Set-ItemProperty -Path 'HKLM:\{HvciKey}' -Name 'Enabled' -Value {(enable ? 1 : 0)} -Type DWord -Force
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }
}
