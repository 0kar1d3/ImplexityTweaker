using Microsoft.Win32;

namespace ImplexityTweaker.Services;

/// <summary>
/// Твики безопасности Windows: отключение Защитника Windows, SmartScreen,
/// брандмауэра, UAC и службы Центра безопасности. Требуются права администратора.
/// </summary>
public static class SecurityService
{
    private const string DefenderPolicy = @"SOFTWARE\Policies\Microsoft\Windows Defender";
    private const string SmartScreenPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
    private const string SystemPolicies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const string FirewallPolicy = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";

    // ===================== Защитник Windows =====================

    public static bool GetDefenderDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(DefenderPolicy);
            return (key?.GetValue("DisableAntiSpyware") as int? ?? 0) == 1
                || (key?.GetValue("DisableAntiVirus") as int? ?? 0) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Отключает (или включает) Защитник Windows через политики и службы.
    /// В современных сборках перед постоянным отключением нужно выключить
    /// Tamper Protection в самом Центре безопасности.
    /// </summary>
    public static async Task SetDefenderDisabledAsync(bool disable)
    {
        int v = disable ? 1 : 0;
        string svcMode = disable ? "disabled" : "auto";
        string script = $@"
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiSpyware /t REG_DWORD /d {v} /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiVirus /t REG_DWORD /d {v} /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableRealtimeMonitoring /t REG_DWORD /d {v} /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableBehaviorMonitoring /t REG_DWORD /d {v} /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableOnAccessProtection /t REG_DWORD /d {v} /f
sc.exe config WinDefend start= {svcMode}
sc.exe config WdNisSvc start= {svcMode}
{(disable ? "sc.exe stop WinDefend | Out-Null" : "")}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }

    // ===================== SmartScreen =====================

    public static bool GetSmartScreenDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SmartScreenPolicy);
            return (key?.GetValue("EnableSmartScreen") as int? ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetSmartScreenDisabledAsync(bool disable)
    {
        string explorerMode = disable ? "Off" : "RequireAdmin";
        string script = $@"
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows\System"" /v EnableSmartScreen /t REG_DWORD /d {(disable ? 0 : 1)} /f
reg add ""HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer"" /v SmartScreenEnabled /t REG_SZ /d ""{explorerMode}"" /f
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }

    // ===================== Брандмауэр Windows =====================

    public static bool GetFirewallDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(FirewallPolicy + @"\StandardProfile");
            return (key?.GetValue("EnableFirewall") as int? ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetFirewallDisabledAsync(bool disable)
    {
        var r = await CommandRunner.RunPowerShellAsync(
            $"netsh advfirewall set allprofiles state {(disable ? "off" : "on")}");
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }

    // ===================== UAC =====================

    public static bool GetUacDisabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SystemPolicies);
            return (key?.GetValue("EnableLUA") as int? ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    public static async Task SetUacDisabledAsync(bool disable)
    {
        var r = await CommandRunner.RunPowerShellAsync(
            $"reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v EnableLUA /t REG_DWORD /d {(disable ? 0 : 1)} /f");
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }

    // ===================== Безопасность Windows (удаление) =====================

    /// <summary>
    /// Возвращает true, если приложение «Безопасность Windows» (SecHealthUI) удалено.
    /// </summary>
    public static async Task<bool> GetWindowsSecurityRemovedAsync()
    {
        try
        {
            var r = await CommandRunner.RunPowerShellAsync(
                "(Get-AppxPackage -AllUsers Microsoft.SecHealthUI -ErrorAction SilentlyContinue).Name");
            return r.ExitCode == 0 && string.IsNullOrWhiteSpace(r.Output);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Удаляет приложение «Безопасность Windows» целиком (SecHealthUI) и отключает
    /// связанные службы. При включении обратно возвращает службы и пытается
    /// переустановить приложение из системных файлов.
    /// </summary>
    public static async Task SetWindowsSecurityRemovedAsync(bool remove)
    {
        string script = remove
            ? @"
# Удаляем приложение Безопасность Windows (SecHealthUI)
Get-AppxPackage -AllUsers Microsoft.SecHealthUI -ErrorAction SilentlyContinue | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
# Отключаем связанные службы
sc.exe config SecurityCenter start= disabled 2>$null | Out-Null
sc.exe stop SecurityCenter 2>$null | Out-Null
sc.exe config SecurityHealthService start= disabled 2>$null | Out-Null
sc.exe stop SecurityHealthService 2>$null | Out-Null
"
            : @"
# Возвращаем службы
sc.exe config SecurityCenter start= auto 2>$null | Out-Null
sc.exe start SecurityCenter 2>$null | Out-Null
sc.exe config SecurityHealthService start= auto 2>$null | Out-Null
sc.exe start SecurityHealthService 2>$null | Out-Null
# Пытаемся переустановить приложение Безопасность Windows из системных файлов
$dir = Get-ChildItem 'C:\Program Files\WindowsApps' -Directory -Filter 'Microsoft.SecHealthUI_*' -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'AppxManifest.xml') } | Select-Object -First 1
if ($dir) {
    Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $dir.FullName 'AppxManifest.xml') -ErrorAction SilentlyContinue
}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }

    // ===================== Windows Defender (полное отключение) =====================

    /// <summary>
    /// Возвращает true, если Windows Defender полностью отключён:
    /// и движок выключен (политики/службы), и приложение «Безопасность Windows» удалено.
    /// </summary>
    public static async Task<bool> GetDefenderFullyDisabledAsync()
    {
        bool engineOff = GetDefenderDisabled();
        bool appRemoved = await GetWindowsSecurityRemovedAsync();
        return engineOff && appRemoved;
    }

    /// <summary>
    /// Полностью отключает Windows Defender одним действием:
    /// политики и службы антивируса + удаление приложения «Безопасность Windows»
    /// и служб статуса. При включении обратно — всё восстанавливается (best-effort).
    /// </summary>
    public static async Task SetDefenderFullyDisabledAsync(bool disable)
    {
        string script = disable
            ? @"
# --- Защитник Windows: политики и службы
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiSpyware /t REG_DWORD /d 1 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiVirus /t REG_DWORD /d 1 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableRealtimeMonitoring /t REG_DWORD /d 1 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableBehaviorMonitoring /t REG_DWORD /d 1 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableOnAccessProtection /t REG_DWORD /d 1 /f
sc.exe config WinDefend start= disabled
sc.exe config WdNisSvc start= disabled
sc.exe stop WinDefend | Out-Null
# --- Удаляем приложение Безопасность Windows (SecHealthUI)
Get-AppxPackage -AllUsers Microsoft.SecHealthUI -ErrorAction SilentlyContinue | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue
# --- Отключаем службы статуса безопасности
sc.exe config SecurityCenter start= disabled 2>$null | Out-Null
sc.exe stop SecurityCenter 2>$null | Out-Null
sc.exe config SecurityHealthService start= disabled 2>$null | Out-Null
sc.exe stop SecurityHealthService 2>$null | Out-Null
"
            : @"
# --- Возвращаем политики Защитника Windows
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiSpyware /t REG_DWORD /d 0 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender"" /v DisableAntiVirus /t REG_DWORD /d 0 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableRealtimeMonitoring /t REG_DWORD /d 0 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableBehaviorMonitoring /t REG_DWORD /d 0 /f
reg add ""HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection"" /v DisableOnAccessProtection /t REG_DWORD /d 0 /f
sc.exe config WinDefend start= auto
sc.exe config WdNisSvc start= auto
sc.exe start WinDefend | Out-Null
# --- Возвращаем службы статуса безопасности
sc.exe config SecurityCenter start= auto 2>$null | Out-Null
sc.exe start SecurityCenter 2>$null | Out-Null
sc.exe config SecurityHealthService start= auto 2>$null | Out-Null
sc.exe start SecurityHealthService 2>$null | Out-Null
# --- Пытаемся переустановить приложение Безопасность Windows
$dir = Get-ChildItem 'C:\Program Files\WindowsApps' -Directory -Filter 'Microsoft.SecHealthUI_*' -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'AppxManifest.xml') } | Select-Object -First 1
if ($dir) {
    Add-AppxPackage -DisableDevelopmentMode -Register (Join-Path $dir.FullName 'AppxManifest.xml') -ErrorAction SilentlyContinue
}
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error + r.Output);
    }
}

