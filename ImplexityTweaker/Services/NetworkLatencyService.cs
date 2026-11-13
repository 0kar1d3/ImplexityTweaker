using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Твики снижения задержки сети (Latency / TCP) и приоритет GPU для игр.
/// Все изменения вносятся в реестр Windows, Get-методы читают состояние ключей
/// для отображения в ToggleSwitch.
/// </summary>
public static class NetworkLatencyService
{
    private const string MultimediaReg = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesReg = MultimediaReg + @"\Tasks\Games";
    private const string TcpReg = @"System\CurrentControlSet\Services\Tcpip\Parameters";

    // ===================== SystemResponsiveness = 0 =====================

    /// <summary>Проверяет, установлена ли SystemResponsiveness = 0.</summary>
    public static bool GetSystemResponsivenessEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaReg);
            return (key?.GetValue("SystemResponsiveness") as int? ?? 20) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Устанавливает SystemResponsiveness = 0 (или 20 при отключении).</summary>
    public static Task SetSystemResponsivenessAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(MultimediaReg, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(MultimediaReg, writable: true)
                ?? throw new InvalidOperationException($"Failed to open HKLM\\{MultimediaReg}");
            key.SetValue("SystemResponsiveness", enable ? 0 : 20, RegistryValueKind.DWord);
        });
    }

    // ===================== TcpAckFrequency = 1 =========================

    /// <summary>Проверяет, что на всех интерфейсах установлено TcpAckFrequency = 1.</summary>
    public static bool GetTcpAckFrequencyEnabled()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpReg + @"\Interfaces");
            if (baseKey == null)
                return false;

            var names = baseKey.GetSubKeyNames();
            if (names.Length == 0)
                return false;

            foreach (var name in names)
            {
                using var iface = baseKey.OpenSubKey(name);
                if ((iface?.GetValue("TcpAckFrequency") as int? ?? 0) != 1)
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Устанавливает TcpAckFrequency = 1 (или 2) на всех сетевых интерфейсах.</summary>
    public static async Task SetTcpAckFrequencyAsync(bool enable)
    {
        try
        {
            string script = $@"
$interfaces = Get-ChildItem -Path 'HKLM:\{TcpReg}\Interfaces' -ErrorAction SilentlyContinue
foreach ($iface in $interfaces) {{
    Set-ItemProperty -Path $iface.PSPath -Name 'TcpAckFrequency' -Value {(enable ? 1 : 2)} -Type DWord -Force
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set TcpAckFrequency", ex);
        }
    }

    /// <summary>Проверяет, что на всех интерфейсах установлено TCPNoDelay = 1.</summary>
    public static bool GetTcpNoDelayEnabled()
    {
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpReg + @"\Interfaces");
            if (baseKey == null)
                return false;

            var names = baseKey.GetSubKeyNames();
            if (names.Length == 0)
                return false;

            foreach (var name in names)
            {
                using var iface = baseKey.OpenSubKey(name);
                if ((iface?.GetValue("TcpNoDelay") as int? ?? 0) != 1)
                    return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Устанавливает TCPNoDelay = 1 (или 0) на всех сетевых интерфейсах —
    /// это и есть отключение алгоритма Нагла.
    /// </summary>
    public static async Task SetTcpNoDelayAsync(bool enable)
    {
        try
        {
            string script = $@"
$interfaces = Get-ChildItem -Path 'HKLM:\{TcpReg}\Interfaces' -ErrorAction SilentlyContinue
foreach ($iface in $interfaces) {{
    Set-ItemProperty -Path $iface.PSPath -Name 'TcpNoDelay' -Value {(enable ? 1 : 0)} -Type DWord -Force
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set TCPNoDelay", ex);
        }
    }

    // ===================== GPU Priority (Games) ========================

    /// <summary>Проверяет, что GPU Priority в Tasks\Games = 8.</summary>
    public static bool GetGpuPriorityEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GamesReg);
            return (key?.GetValue("GPU Priority") as int? ?? 0) == 8;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Устанавливает приоритет GPU и CPU для игровых задач:
    /// Multimedia\SystemProfile\Tasks\Games → GPU Priority = 8, Priority = 6
    /// (при отключении — 0 и 2).
    /// </summary>
    public static Task SetGpuPriorityAsync(bool enable)
    {
        return Task.Run(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(GamesReg, writable: true)
                ?? Registry.LocalMachine.CreateSubKey(GamesReg, writable: true)
                ?? throw new InvalidOperationException($"Failed to open HKLM\\{GamesReg}");
            key.SetValue("GPU Priority", enable ? 8 : 0, RegistryValueKind.DWord);
            key.SetValue("Priority", enable ? 6 : 2, RegistryValueKind.DWord);
        });
    }
}

