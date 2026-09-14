using System.IO;
using Microsoft.Win32;

namespace ImplexityTweaker.Services;

public static class OptimizationService
{
    private const string PersonalizeReg = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AdvancedReg = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string GameDvrReg = @"Software\Microsoft\Windows\CurrentVersion\GameDVR";
    private const string TcpReg = @"System\CurrentControlSet\Services\Tcpip\Parameters";

    // Power scheme GUIDs (правильные значения):
    // e9a42b02 — скрытая схема Ultimate Performance, 8c5e7fda — High Performance.
    internal const string PowerSchemeUltimatePerf = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    internal const string PowerSchemeHighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

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
            // Windows хранит системную прозрачность в EnableTransparency (1 — включена, 0 — выключена).
            return (key?.GetValue("EnableTransparency") as int? ?? 1) == 0;
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
    -Name 'EnableTransparency' -Value {(disable ? 0 : 1)} -Force
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
        await ClearGameCacheAsync(null);
    }

    /// <param name="progress">Опциональный прогресс в процентах 0..100 (по папкам кэша).</param>
    public static async Task ClearGameCacheAsync(IProgress<int>? progress)
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

            int total = cachePaths.Length;
            for (int i = 0; i < total; i++)
            {
                progress?.Report((i + 1) * 100 / Math.Max(1, total));

                var cachePath = cachePaths[i];
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
        await ClearTempFilesAsync(null);
    }

    /// <param name="progress">Опциональный прогресс в процентах 0..100 (по командам очистки).</param>
    public static async Task ClearTempFilesAsync(IProgress<int>? progress)
    {
        try
        {
            string[] commands =
            {
                @"Remove-Item -Path 'C:\Windows\Temp\*' -Recurse -Force -ErrorAction SilentlyContinue",
                @"Remove-Item -Path ""$env:TEMP\*"" -Recurse -Force -ErrorAction SilentlyContinue",
                @"Remove-Item -Path 'C:\Windows\Prefetch\*' -Recurse -Force -ErrorAction SilentlyContinue"
            };

            int total = commands.Length;
            for (int i = 0; i < total; i++)
            {
                progress?.Report((i + 1) * 100 / Math.Max(1, total));

                var result = await CommandRunner.RunPowerShellAsync(commands[i]);
                if (result.ExitCode != 0)
                    throw new Exception(result.Error);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to clear temp files", ex);
        }
    }
    public static async Task ClearDnsCacheAsync()
    {
        try
        {
            var result = await CommandRunner.RunProcessAsync("ipconfig", "/flushdns");
            if (result.ExitCode != 0)
                throw new Exception(result.Error);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to clear DNS cache", ex);
        }
    }

    public static async Task ClearThumbnailCacheAsync()
    {
        try
        {
            string explorerCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Windows\Explorer");

            if (Directory.Exists(explorerCache))
            {
                foreach (var file in Directory.EnumerateFiles(explorerCache, "thumbcache_*.db"))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Файл заблокирован — пропускаем.
                    }
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to clear thumbnail cache", ex);
        }
    }

    // ===================== Ультрапроизводительность (Ultimate Performance) =====================

    // Скрытая схема "Ultimate Performance" (по умолчанию не видна в списке схем).
    private const string UltimatePerfGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    // "Высокая производительность" — возвращаемся на неё при выключении твика.
    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    /// <summary>
    /// Проверяет, что активная схема электропитания — Ultra/Ultimate Performance.
    /// </summary>
    public static async Task<bool> GetUltimatePerformanceEnabledAsync()
    {
        try
        {
            var r = await CommandRunner.RunProcessAsync("powercfg", "/getactivescheme");
            if (r.ExitCode != 0)
                return false;

            var output = (r.Output + " " + r.Error).ToLowerInvariant();
            return output.Contains(UltimatePerfGuid)
                || output.Contains("ultimate")
                || output.Contains("ultra");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Включает (активирует схему Ultimate Performance) или выключает
    /// (возвращает схему «Высокая производительность»).
    /// </summary>
    public static async Task SetUltimatePerformanceAsync(bool enable)
    {
        if (enable)
        {
            string script = @"
# 1. Если активная схема уже Ultra/Ultimate Performance — выходим.
$active = powercfg /getactivescheme
if ($active -match 'e9a42b02-d5df-448d-aa00-03f14749eb61' -or $active -match 'Ultra|Ultimate') { exit 0 }
# 2. Пробуем активировать скрытую схему Ultimate Performance напрямую.
powercfg /setactive e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null
if ($LASTEXITCODE -eq 0) { exit 0 }
# 3. Ищем уже созданную схему с именем Ultra/Ultimate Performance.
$line = powercfg /list | Select-String -Pattern 'Ultra|Ultimate' | Select-Object -Last 1
if ($line) {
    $g = [regex]::Match($line.ToString(), '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}').Value
    if ($g) { powercfg /setactive $g; if ($LASTEXITCODE -eq 0) { exit 0 } }
}
# 4. Раскрываем скрытую схему дубликатом и активируем её.
$out = powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 2>$null
$g2 = [regex]::Match([string]$out, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}').Value
if ($g2) {
    powercfg /setactive $g2
    if ($LASTEXITCODE -eq 0) { exit 0 }
}
throw 'Не удалось найти или создать схему Ultimate Performance'
";
            var r = await CommandRunner.RunPowerShellAsync(script);
            if (r.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
        }
        else
        {
            var r = await CommandRunner.RunProcessAsync("powercfg", $"/setactive {HighPerfGuid}");
            if (r.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
        }
    }
}


