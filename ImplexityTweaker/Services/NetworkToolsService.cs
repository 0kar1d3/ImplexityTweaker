using System.Globalization;

namespace ImplexityTweaker.Services;

/// <summary>Сетевые утилиты: сброс стека, IPv6, тест пинга, пароли Wi-Fi.</summary>
public static class NetworkToolsService
{
    public sealed record PingResult(double Avg, double Min, double Max, double Jitter, int Loss);

    public sealed record WifiProfile(string Name, string Password);

    /// <summary>Полный сброс сетевого стека. Требуется перезагрузка.</summary>
    public static async Task ResetStackAsync()
    {
        const string script = @"
$ErrorActionPreference = 'SilentlyContinue'
netsh winsock reset | Out-Null
netsh int ip reset | Out-Null
netsh int ipv6 reset | Out-Null
netsh int tcp reset | Out-Null
ipconfig /flushdns | Out-Null
ipconfig /registerdns | Out-Null
arp -d * | Out-Null
nbtstat -R | Out-Null
exit 0
";
        await CommandRunner.RunPowerShellAsync(script);
    }

    /// <summary>Включает/отключает IPv6 на всех сетевых адаптерах.</summary>
    public static async Task SetIpv6EnabledAsync(bool enabled)
    {
        var cmd = enabled ? "Enable-NetAdapterBinding" : "Disable-NetAdapterBinding";
        var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
Get-NetAdapter | ForEach-Object {{ {cmd} -Name $_.Name -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue }}
exit 0
";
        await CommandRunner.RunPowerShellAsync(script);
    }

    public static async Task<bool> GetIpv6EnabledAsync()
    {
        const string script = @"
$on = Get-NetAdapterBinding -ComponentID ms_tcpip6 -ErrorAction SilentlyContinue | Where-Object { $_.Enabled } | Select-Object -First 1
if ($on) { 'on' } else { 'off' }
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        return r.Output.Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Пинг: среднее/мин/макс/джиттер/потери. null — узел недоступен.</summary>
    public static async Task<PingResult?> PingAsync(string host, int count = 10)
    {
        var script = $@"
$r = Test-Connection -ComputerName '{host}' -Count {count} -ErrorAction SilentlyContinue
$times = @($r | ForEach-Object {{ [double]$_.ResponseTime }} | Where-Object {{ $_ -ge 0 }})
if ($times.Count -eq 0) {{ Write-Output 'FAIL'; exit 0 }}
$avg = [math]::Round(($times | Measure-Object -Average).Average, 1)
$min = [math]::Round(($times | Measure-Object -Minimum).Minimum, 1)
$max = [math]::Round(($times | Measure-Object -Maximum).Maximum, 1)
$j = 0.0
if ($times.Count -gt 1) {{
  for ($i = 1; $i -lt $times.Count; $i++) {{ $j += [math]::Abs($times[$i] - $times[$i-1]) }}
  $j = [math]::Round($j / ($times.Count - 1), 1)
}}
$loss = {count} - $times.Count
Write-Output ('' + $avg + ';' + $min + ';' + $max + ';' + $j + ';' + $loss)
";
        var r = await CommandRunner.RunPowerShellAsync(script);
        var text = r.Output.Trim();
        if (text.Length == 0 || text.StartsWith("FAIL", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = text.Split(';');
        if (parts.Length < 5)
            return null;

        return new PingResult(Parse(parts[0]), Parse(parts[1]), Parse(parts[2]), Parse(parts[3]),
            int.TryParse(parts[4].Trim(), out var loss) ? loss : 0);
    }

    private static double Parse(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Сохранённые Wi-Fi сети и их пароли (netsh wlan, key=clear).</summary>
    public static async Task<IReadOnlyList<WifiProfile>> GetWifiProfilesAsync()
    {
        const string script = @"
$names = @()
foreach ($line in (netsh wlan show profiles)) {
    if ($line -match '^\s+.*:\s*(.+?)\s*$') { $names += $matches[1].Trim() }
}
foreach ($n in ($names | Select-Object -Unique)) {
    $pass = ''
    foreach ($line in (netsh wlan show profile name=""$n"" key=clear)) {
        $low = $line.ToLower()
        if ($low -match 'key content|содержимое ключа|вміст ключа|schlüsselinhalt|obsah') {
            $idx = $line.IndexOf(':')
            if ($idx -ge 0) { $pass = $line.Substring($idx + 1).Trim() }
        }
    }
    Write-Output ($n + ';' + $pass)
}
";
        var r = await CommandRunner.RunPowerShellAsync(script);

        var list = new List<WifiProfile>();
        foreach (var raw in r.Output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var idx = line.IndexOf(';');
            if (idx <= 0)
                continue;

            list.Add(new WifiProfile(line[..idx].Trim(), line[(idx + 1)..].Trim()));
        }
        return list;
    }
}
