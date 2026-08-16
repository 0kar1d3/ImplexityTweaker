using System.IO;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

public static class OptimizationService
{
    private const string PersonalizeReg = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AdvancedReg = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string GameDvrReg = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string TcpReg = @"System\CurrentControlSet\Services\Tcpip\Parameters";

    // Power scheme GUIDs
    internal const string PowerSchemeUltimatePerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    internal const string PowerSchemeHighPerf = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private const string ProcessPriorityRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

    // ГРАФИКА И GPU

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
            throw new InvalidOperationException("Failed to set FSO", ex);
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
            throw new InvalidOperationException("Failed to set MPO", ex);
        }
    }

    // GameDVR (Xbox Game Bar / capture)

    public static bool GetGameDvrDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(GameDvrReg);
            var val = key?.GetValue("AppCaptureEnabled") as int? ?? 1;
            return val == 0;
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
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion' -Name 'GameDVR' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR' `
    -Name 'AppCaptureEnabled' -Value {(disable ? 0 : 1)} -Type DWord -Force
try {{
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows' -Name 'GameDVR' -Force | Out-Null
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR' `
        -Name 'AllowGameDVR' -Value {(disable ? 0 : 1)} -Type DWord -Force
}} catch {{ }}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set Game DVR", ex);
        }
    }

    // Process Priority (CS2)

    public static async Task SetProcessPriorityAsync(string processName, bool high)
    {
        try
        {
            string exeName = Path.GetFileNameWithoutExtension(processName);
            string script = $@"
try {{
    New-Item -Path 'HKLM:\{ProcessPriorityRegPath}\{exeName}' -Force | Out-Null
    Set-ItemProperty -Path 'HKLM:\{ProcessPriorityRegPath}\{exeName}' `
        -Name 'Priority' -Value {(high ? 3 : 2)} -Type DWord -Force
}} catch {{ }}
$processes = Get-Process -Name '{exeName}' -ErrorAction SilentlyContinue
if ($null -ne $processes) {{
    foreach ($process in $processes) {{
        try {{
            $p = [System.Diagnostics.Process]::GetProcessById($process.Id)
            $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::{(high ? "High" : "Normal")}
        }} catch {{ }}
    }}
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0 && !string.IsNullOrWhiteSpace(result.Error) && !result.Error.Contains("Get-Process"))
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
                        throw new InvalidOperationException("Failed to set process priority", ex);
        }
    }

    // NETWORK

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
            // Nagle управляется значением TcpNoDelay в каждом сетевом интерфейсе,
            // а не в верхнем ключе Tcpip\Parameters. Применяем к каждому интерфейсу.
            string script = $@"
$interfaces = Get-ChildItem -Path 'HKLM:\{TcpReg}\Interfaces' -ErrorAction SilentlyContinue
foreach ($iface in $interfaces) {{
    Set-ItemProperty -Path $iface.PSPath -Name 'TcpNoDelay' -Value {(disable ? 1 : 0)} -Type DWord -Force
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set Nagle", ex);
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
netsh int tcp set global autotuninglevel={value}
Set-ItemProperty -Path 'HKLM:\{TcpReg}' `
    -Name 'AutoTuningLevelLocal' -Value '{value}' -Type String -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set Auto-Tuning", ex);
        }
    }

    public static async Task SetNetworkQoSAsync(bool enable)
    {
        try
        {
            // Реальная приоритезация сетевого трафика для игр:
            // 1) TcpAckFrequency=1 / TcpDelAckTicks=0 на каждом интерфейсе — мгновенная
            //    отправка ACK, снижает задержку (латентность) в онлайн-играх.
            // 2) DSCP-метка (Expedited Forwarding, 46) через QoS-политику Windows.
            string script = $@"
$interfaces = Get-ChildItem -Path 'HKLM:\{TcpReg}\Interfaces' -ErrorAction SilentlyContinue
foreach ($iface in $interfaces) {{
    Set-ItemProperty -Path $iface.PSPath -Name 'TcpAckFrequency' -Value {(enable ? 1 : 2)} -Type DWord -Force
    Set-ItemProperty -Path $iface.PSPath -Name 'TcpDelAckTicks' -Value {(enable ? 0 : 2)} -Type DWord -Force
}}
if ({(enable ? 1 : 0)} -eq 1) {{
    New-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Force | Out-Null
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'Version' -Value 1 -Type DWord -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'AppName' -Value '*' -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'Protocol' -Value '*' -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'DSCP' -Value 46 -Type DWord -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'LocalPort' -Value 0 -Type DWord -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'RemotePort' -Value 0 -Type DWord -Force
    Set-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Name 'ThrottleRate' -Value -1 -Type DWord -Force
}} else {{
    Remove-Item -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\QoS\ImplexityTweaker' -Recurse -Force -ErrorAction SilentlyContinue
}}
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set QoS", ex);
        }
    }

    // Power Scheme (Ultra Performance)

        public static bool GetUltraPerformanceEnabled()
    {
        try
        {
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains(PowerSchemeUltimatePerf, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetUltraPerformanceAsync(bool enable)
    {
        try
        {
            string schemeGuid = enable ? PowerSchemeUltimatePerf : PowerSchemeHighPerf;
            var result = await CommandRunner.RunProcessAsync("powercfg", $"/setactive {schemeGuid}");
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
                {
            throw new InvalidOperationException("Failed to set power scheme", ex);
        }
    }

    // System Performance

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
            throw new InvalidOperationException("Failed to set visual effects", ex);
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
            throw new InvalidOperationException("Failed to set window animations", ex);
        }
    }

    public static bool GetBackgroundAppsDisabled()
    {
        try
        {
            // Фоновая активность приложений хранится в BackgroundAccessApplications,
            // а не в Themes\Personalize.
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications");
            return (key?.GetValue("GlobalUserDisabled") as int? ?? 0) == 1;
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
New-Item -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion' -Name 'BackgroundAccessApplications' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications' `
    -Name 'GlobalUserDisabled' -Value {(disable ? 1 : 0)} -Type DWord -Force
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to set background apps", ex);
        }
    }

    // Cleanup

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
            throw new InvalidOperationException("Failed to clear cache", ex);
        }
    }

    public static async Task ClearTempFilesAsync()
    {
        try
        {
            string script = $@"
Remove-Item -Path 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path ""$env:TEMP\*"" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'C:\Windows\Prefetch\*' -Recurse -Force -ErrorAction SilentlyContinue
";
            var result = await CommandRunner.RunPowerShellAsync(script);
            if (result.ExitCode != 0)
                throw new Exception(result.Error);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to clear temp files", ex);
        }
    }
}