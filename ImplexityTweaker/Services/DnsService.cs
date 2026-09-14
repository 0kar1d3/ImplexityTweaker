namespace ImplexityTweaker.Services;

/// <summary>
/// Быстрая смена DNS-серверов на активном сетевом адаптере.
/// Все операции требуют прав администратора.
/// </summary>
public static class DnsService
{
    public sealed record DnsProvider(string Id, string Title, string Primary, string Secondary)
    {
        public string ServersText => $"{Primary}, {Secondary}";
    }

    /// <summary>Готовые наборы DNS-серверов.</summary>
    public static IReadOnlyList<DnsProvider> Providers { get; } =
    [
        new("cloudflare", "Cloudflare", "1.1.1.1", "1.0.0.1"),
        new("google", "Google", "8.8.8.8", "8.8.4.4"),
        new("quad9", "Quad9", "9.9.9.9", "149.112.112.112"),
        new("adguard", "AdGuard DNS", "94.140.14.14", "94.140.15.15"),
        new("opendns", "OpenDNS", "208.67.222.222", "208.67.220.220"),
        new("xbox", "Xbox DNS", "111.88.96.50", "111.88.96.51"),
    ];

    /// <summary>Устанавливает DNS выбранного провайдера. Возвращает имя адаптера.</summary>
    public static async Task<string> ApplyAsync(DnsProvider provider)
    {
        // Берём адаптер с шлюзом по умолчанию — это и есть активное подключение к интернету.
        var script = $@"
$ErrorActionPreference = 'Stop'
$cfg = Get-NetIPConfiguration | Where-Object {{ $_.IPv4DefaultGateway -ne $null -and $_.NetAdapter.Status -eq 'Up' }} | Select-Object -First 1
if ($null -eq $cfg) {{ throw 'Не найден активный сетевой адаптер' }}
Set-DnsClientServerAddress -InterfaceIndex $cfg.InterfaceIndex -ServerAddresses ('{provider.Primary}','{provider.Secondary}')
Write-Output $cfg.InterfaceAlias
";

        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);

        return r.Output.Trim();
    }

    /// <summary>Возвращает DNS к автоматическому получению (DHCP). Возвращает имя адаптера.</summary>
    public static async Task<string> ResetAsync()
    {
        const string script = @"
$ErrorActionPreference = 'Stop'
$cfg = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1
if ($null -eq $cfg) { throw 'Не найден активный сетевой адаптер' }
Set-DnsClientServerAddress -InterfaceIndex $cfg.InterfaceIndex -ResetServerAddresses
Write-Output $cfg.InterfaceAlias
";

        var r = await CommandRunner.RunPowerShellAsync(script);
        if (r.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(r.Error) ? r.Output : r.Error);

        return r.Output.Trim();
    }

    /// <summary>Текущие IPv4 DNS-адреса активного адаптера (пустая строка — автоматически/DHCP).</summary>
    public static async Task<string> GetCurrentAsync()
    {
        const string script = @"
$cfg = Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1
if ($null -eq $cfg) { return }
$dns = Get-DnsClientServerAddress -InterfaceIndex $cfg.InterfaceIndex -AddressFamily IPv4 | Select-Object -ExpandProperty ServerAddresses
if ($dns) { Write-Output ($dns -join ', ') }
";

        var r = await CommandRunner.RunPowerShellAsync(script);
        return r.ExitCode == 0 ? r.Output.Trim() : string.Empty;
    }

    /// <summary>Определяет провайдера по текущим DNS-адресам (null — не совпал ни с одним).</summary>
    public static DnsProvider? MatchProvider(string currentDns)
    {
        if (string.IsNullOrWhiteSpace(currentDns))
            return null;

        foreach (var p in Providers)
        {
            if (currentDns.Contains(p.Primary, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }
}
