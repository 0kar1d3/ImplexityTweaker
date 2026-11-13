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
    public static Task SetMemoryIntegrityAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(HvciKey, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(HvciKey, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ветку HKLM\\{HvciKey}.");
            key.SetValue("Enabled", enable ? 1 : 0, RegistryValueKind.DWord);
        });
    }
}
