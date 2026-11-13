using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Твики мыши, клавиатуры и снижения инпут-лага:
/// 1) 1:1 Raw Input (отключение акселерации указателя + линейные кривые MarkC Fix).
/// 2) Размер буфера очереди ввода мыши и клавиатуры (MouseDataQueueSize / KeyboardDataQueueSize).
/// 3) Точный таймер 0.5 мс (NtSetTimerResolution + GlobalTimerResolutionRequests).
/// 4) Запрет энергосбережения USB/HID-мыши (Selective Suspend + Device Parameters).
/// 5) Приоритет csrss.exe, Dynamic Tick и Win32PrioritySeparation.
/// </summary>
public static class MouseTweaksService
{
    private const string MouseRegPath = @"Control Panel\Mouse";
    private const string MouClassRegPath = @"SYSTEM\CurrentControlSet\Services\mouclass\Parameters";
    private const string KernelRegPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string PriorityControlRegPath = @"SYSTEM\CurrentControlSet\Control\PriorityControl";

    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryTimerResolution(out uint minimumResolution, out uint maximumResolution, out uint currentResolution);

    [DllImport("ntdll.dll")]
    private static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    private static uint _activeDesiredTimerRes;
    private static bool _timerRequested;

    // Линейные кривые MarkC 1:1 (100% DPI)
    private static readonly byte[] LinearXCurve =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xC0, 0xCC, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x80, 0x99, 0x19, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x40, 0x66, 0x26, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x33, 0x33, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static readonly byte[] LinearYCurve =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x38, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x70, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xA8, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0xE0, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    // Стандартные кривые Windows 10/11
    private static readonly byte[] DefaultXCurve =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x15, 0x6E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x40, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x29, 0xDC, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x28, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    private static readonly byte[] DefaultYCurve =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFD, 0x11, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x24, 0x04, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0xFC, 0x12, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0xC0, 0xBB, 0x01, 0x00, 0x00, 0x00, 0x00
    ];

    public static bool GetRawInputEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MouseRegPath, false);
            var speed = key?.GetValue("MouseSpeed")?.ToString() ?? "1";
            var t1 = key?.GetValue("MouseThreshold1")?.ToString() ?? "6";
            var t2 = key?.GetValue("MouseThreshold2")?.ToString() ?? "10";
            return speed == "0" && t1 == "0" && t2 == "0";
        }
        catch
        {
            return false;
        }
    }

    public static Task SetRawInputAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(MouseRegPath, true)
                ?? throw new InvalidOperationException("Не удалось открыть ветку реестра HKCU\\Control Panel\\Mouse.");

            if (enable)
            {
                key.SetValue("MouseSpeed", "0", RegistryValueKind.String);
                key.SetValue("MouseThreshold1", "0", RegistryValueKind.String);
                key.SetValue("MouseThreshold2", "0", RegistryValueKind.String);
                key.SetValue("SmoothMouseXCurve", LinearXCurve, RegistryValueKind.Binary);
                key.SetValue("SmoothMouseYCurve", LinearYCurve, RegistryValueKind.Binary);

                int[] mouseParams = [0, 0, 0];
                SystemParametersInfo(SPI_SETMOUSE, 0, mouseParams, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
            else
            {
                key.SetValue("MouseSpeed", "1", RegistryValueKind.String);
                key.SetValue("MouseThreshold1", "6", RegistryValueKind.String);
                key.SetValue("MouseThreshold2", "10", RegistryValueKind.String);
                key.SetValue("SmoothMouseXCurve", DefaultXCurve, RegistryValueKind.Binary);
                key.SetValue("SmoothMouseYCurve", DefaultYCurve, RegistryValueKind.Binary);

                int[] mouseParams = [6, 10, 1];
                SystemParametersInfo(SPI_SETMOUSE, 0, mouseParams, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            }
        });
    }

    public static int GetInputQueueSize()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MouClassRegPath, false);
            if (key?.GetValue("MouseDataQueueSize") is int val && val > 0)
                return val;
        }
        catch
        {
            // ignore
        }

        return 100;
    }

    private static RegistryKey OpenOrCreateLmKey(string subKey)
    {
        return Registry.LocalMachine.OpenSubKey(subKey, writable: true)
            ?? Registry.LocalMachine.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Не удалось открыть ветку реестра HKLM\\{subKey}.");
    }

    public static Task SetInputQueueSizeAsync(int queueSize)
    {
        int mouseQueue = Math.Clamp(queueSize, 16, 100);
        return Task.Run(() =>
        {
            using var mou = OpenOrCreateLmKey(MouClassRegPath);
            mou.SetValue("MouseDataQueueSize", mouseQueue, RegistryValueKind.DWord);
        });
    }

    // ===================== Системный таймер 0.5 мс =====================

    public static bool GetHighPrecisionTimerEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KernelRegPath, false);
            if (key?.GetValue("GlobalTimerResolutionRequests") is int v && v == 1)
                return true;
        }
        catch { }

        return _timerRequested;
    }

    public static double GetCurrentTimerResolutionMs()
    {
        try
        {
            if (NtQueryTimerResolution(out _, out _, out uint current) == 0 && current > 0)
                return Math.Round(current / 10000.0, 2);
        }
        catch { }
        return 15.63;
    }

    public static void InitializeTimerResolutionAtStartup(bool? savedSetting)
    {
        bool active = savedSetting ?? GetHighPrecisionTimerEnabled();
        if (active)
            ApplyInProcessTimerResolution(true);
    }

    public static void ReleaseTimerResolution()
    {
        if (_timerRequested)
            ApplyInProcessTimerResolution(false);
    }

    private static void ApplyInProcessTimerResolution(bool enable)
    {
        try
        {
            if (enable)
            {
                if (NtQueryTimerResolution(out _, out uint maxRes, out _) == 0)
                {
                    uint target = Math.Clamp(maxRes, 5000u, 10000u);
                    if (NtSetTimerResolution(target, true, out _) == 0)
                    {
                        _activeDesiredTimerRes = target;
                        _timerRequested = true;
                    }
                }
            }
            else if (_timerRequested)
            {
                NtSetTimerResolution(_activeDesiredTimerRes > 0 ? _activeDesiredTimerRes : 5000u, false, out _);
                _timerRequested = false;
            }
        }
        catch { }
    }

    public static Task SetHighPrecisionTimerAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = OpenOrCreateLmKey(KernelRegPath);
            if (enable)
                key.SetValue("GlobalTimerResolutionRequests", 1, RegistryValueKind.DWord);
            else
                key.DeleteValue("GlobalTimerResolutionRequests", throwOnMissingValue: false);

            ApplyInProcessTimerResolution(enable);
        });
    }

    // ===================== Отключение Dynamic Tick (bcdedit) =====================

    public static async Task<bool> GetDynamicTickDisabledAsync()
    {
        try
        {
            var r = await CommandRunner.RunProcessAsync("bcdedit", "/enum {current}");
            if (r.ExitCode == 0 && !string.IsNullOrWhiteSpace(r.Output))
            {
                foreach (var line in r.Output.Split('\n'))
                {
                    if (line.Contains("disabledynamictick", StringComparison.OrdinalIgnoreCase) &&
                        (line.Contains("Yes", StringComparison.OrdinalIgnoreCase) || line.Contains("Да", StringComparison.OrdinalIgnoreCase)))
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    public static async Task SetDynamicTickDisabledAsync(bool disable)
    {
        var r = disable
            ? await CommandRunner.RunProcessAsync("bcdedit", "/set disabledynamictick yes")
            : await CommandRunner.RunProcessAsync("bcdedit", "/deletevalue disabledynamictick");

        if (disable)
        {
            // Гарантируем, что принудительный HPET не включён (он резко повышает задержку)
            await CommandRunner.RunProcessAsync("bcdedit", "/deletevalue useplatformclock");
        }

        if (r.ExitCode != 0 && disable)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);
    }

    // ===================== Приоритет активного окна (Win32PrioritySeparation) =====================

    public static int GetWin32PrioritySeparation()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PriorityControlRegPath, false);
            if (key?.GetValue("Win32PrioritySeparation") is int val)
                return val;
        }
        catch { }
        return 2;
    }

    public static Task SetWin32PrioritySeparationAsync(int value)
    {
        value = Math.Clamp(value, 0, 0x3F);
        return Task.Run(() =>
        {
            using var key = OpenOrCreateLmKey(PriorityControlRegPath);
            key.SetValue("Win32PrioritySeparation", value, RegistryValueKind.DWord);
        });
    }
}
