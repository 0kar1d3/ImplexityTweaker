using System.Globalization;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>Игровые настройки Windows: HAGS, Game Mode, парковка ядер процессора.</summary>
public static class GamingService
{
    private const string GraphicsDriversReg = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string GameBarReg = @"Software\Microsoft\GameBar";

    // ===================== HAGS (аппаратное планирование GPU) =====================

    /// <summary>null — значение не задано (нужен перезапуск/не поддерживается).</summary>
    public static bool? GetHagsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversReg);
            var v = key?.GetValue("HwSchMode") as int?;
            return v.HasValue ? v.Value == 2 : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>HwSchMode: 2 — включено, 1 — выключено. Требуется перезагрузка.</summary>
    public static Task SetHagsAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(GraphicsDriversReg, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(GraphicsDriversReg, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ветку HKLM\\{GraphicsDriversReg}.");
            key.SetValue("HwSchMode", enable ? 2 : 1, RegistryValueKind.DWord);
        });
    }

    // ===================== Игровой режим (Game Mode) =====================

    public static bool GetGameModeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameBarReg);
            return (key?.GetValue("AutoGameModeEnabled") as int? ?? 1) != 0;
        }
        catch
        {
            return true;
        }
    }

    public static Task SetGameModeAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameBarReg, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(GameBarReg, writable: true)
                ?? throw new InvalidOperationException($"Не удалось открыть ветку HKCU\\{GameBarReg}.");
            int val = enable ? 1 : 0;
            key.SetValue("AutoGameModeEnabled", val, RegistryValueKind.DWord);
            key.SetValue("AllowAutoGameMode", val, RegistryValueKind.DWord);
        });
    }

    // ===================== Парковка ядер =====================

    private const string ProcessorSubGroup = "54533251-82be-4824-96c1-47b60b740d00";
    private const string CoreParkingMin = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const int ParkingRestoreValue = 10;

    /// <summary>true — парковка ядер отключена (CPMINCORES = 100).</summary>
    public static async Task<bool> GetCoreParkingDisabledAsync()
    {
        var script = $@"
$q = powercfg /q SCHEME_CURRENT {ProcessorSubGroup} {CoreParkingMin}
foreach ($line in $q) {{
    if ($line -match 'Current AC Power Setting Index:\s*0x([0-9a-fA-F]+)') {{ Write-Output $matches[1]; break }}
}}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        var hex = r.Output.Trim();
        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        return value >= 100;
    }

    /// <summary>Отключает (true) или возвращает (false) парковку ядер на текущей схеме.</summary>
    public static async Task SetCoreParkingAsync(bool disableParking)
    {
        var value = disableParking ? 100 : ParkingRestoreValue;
        var script = $@"
$ErrorActionPreference = 'Stop'
powercfg /setacvalueindex SCHEME_CURRENT {ProcessorSubGroup} {CoreParkingMin} {value} | Out-Null
powercfg /setdcvalueindex SCHEME_CURRENT {ProcessorSubGroup} {CoreParkingMin} {value} | Out-Null
powercfg /setactive SCHEME_CURRENT | Out-Null
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }
}
