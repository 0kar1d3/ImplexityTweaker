using System.IO;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

public static class OptimizationService
{
    private const string PersonalizeReg = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AdvancedReg = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string GameSettingsReg = @"Software\Microsoft\GameBarPresenceWriter\GameBarSettings";
    private const string TcpReg = @"System\CurrentControlSet\Services\Tcpip\Parameters";
    private const string NetworkInterfacesReg = @"System\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";

    // ГРАФИКА КУБОВ И ПЕНИСА

    public static bool GetFullScreenOptimizationsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedReg);
            return (key?.GetValue("DisableFullscreenOptimizations") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetFullScreenOptimizationsAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{AdvancedReg}' `
    -Name 'DisableFullscreenOptimizations' -Value {(disable ? 1 : 0)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить FSO", ex);
        }
    }

    public static bool GetMpoDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedReg);
            return (key?.GetValue("DisableMpoOptimization") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetMpoDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{AdvancedReg}' `
    -Name 'DisableMpoOptimization' -Value {(disable ? 1 : 0)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить MPO", ex);
        }
    }

    public static bool GetGameDvrDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameSettingsReg);
            return (key?.GetValue("AllowGameDVR") as int? ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetGameDvrDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{GameSettingsReg}' `
    -Name 'AllowGameDVR' -Value {(disable ? 0 : 1)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить Game DVR", ex);
        }
    }

    public static async Task SetProcessPriorityAsync(string processName, bool high)
    {
        try
        {
            string script = $@"
$processes = Get-Process -Name '{Path.GetFileNameWithoutExtension(processName)}' -ErrorAction SilentlyContinue
if ($null -ne $processes) {{
    foreach ($process in $processes) {{
        [System.Diagnostics.Process]::GetProcessById($process.Id).PriorityClass = '{(high ? "High" : "Normal")}'

    }}
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0 && !result.Error.Contains("Get-Process"))
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить приоритет процесса", ex);
        }
    }

    // ИНТЕРНЕТ

    public static bool GetNagleDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TcpReg);
            return (key?.GetValue("TcpNoDelay") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetNagleDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKLM:\{TcpReg}' `
    -Name 'TcpNoDelay' -Value {(disable ? 1 : 0)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить Nagle", ex);
        }
    }

    public static bool GetAutoTuningDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(TcpReg);
            var value = key?.GetValue("AutoTuningLevelLocal") as string ?? "normal";
            return value == "disabled";
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetAutoTuningDisabledAsync(bool disable)
    {
        try
        {
            string value = disable ? "disabled" : "normal";
            string script = $@"
Set-ItemProperty -Path 'HKLM:\{TcpReg}' `
    -Name 'AutoTuningLevelLocal' -Value '{value}' -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить Auto-Tuning", ex);
        }
    }

    public static async Task SetNetworkQoSAsync(bool enable)
    {
        try
        {
            string script = $@"
if ({(enable ? "1" : "0")} -eq 1) {{
    netsh int tcp set supplemental enable_rsc=disabled enable_dctcp=enabled enable_ecn=enabled
}} else {{
    netsh int tcp set supplemental enable_rsc=enabled enable_dctcp=disabled enable_ecn=disabled
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить QoS", ex);
        }
    }

    // ОПТИМИЗАЦИЯ СИСИТЕМЫ

    public static bool GetVisualEffectsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeReg);
            return (key?.GetValue("DisableTransparency") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetVisualEffectsDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{PersonalizeReg}' `
    -Name 'DisableTransparency' -Value {(disable ? 1 : 0)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить визуальные эффекты", ex);
        }
    }

    public static bool GetWindowAnimationsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedReg);
            return (key?.GetValue("TaskbarAnimations") as int? ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetWindowAnimationsDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{AdvancedReg}' `
    -Name 'TaskbarAnimations' -Value {(disable ? 0 : 1)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить анимации", ex);
        }
    }

    public static bool GetBackgroundAppsDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeReg);
            return (key?.GetValue("DisableBackgroundApps") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetBackgroundAppsDisabledAsync(bool disable)
    {
        try
        {
            string script = $@"
Set-ItemProperty -Path 'HKCU:\{PersonalizeReg}' `
    -Name 'DisableBackgroundApps' -Value {(disable ? 1 : 0)} -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось установить фоновые приложения", ex);
        }
    }

    // ОЧИСТКА БЛЯДСКАЯ

    public static async Task ClearGameCacheAsync()
    {
        try
        {
            string[] cachePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"SteamCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"D3DSCache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                    @"Steam\steamapps\shadercache"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    @"Steam\appcache")
            };

            foreach (var cachePath in cachePaths)
            {
                if (Directory.Exists(cachePath))
                {
                    try
                    {
                        Directory.Delete(cachePath, true);
                    }
                    catch
                    {

                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось очистить кэш", ex);
        }
    }

    public static async Task ClearTempFilesAsync()
    {
        try
        {
            string script = $@"
Remove-Item -Path 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path $env:TEMP'\*' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'C:\Windows\Prefetch\*' -Recurse -Force -ErrorAction SilentlyContinue
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Не удалось очистить временные файлы", ex);
        }
    }
}
