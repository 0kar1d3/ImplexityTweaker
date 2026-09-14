using System.IO;

namespace ImplexityTweaker.Services;

/// <summary>Блокировка телеметрии Microsoft через файл hosts (обратимо).</summary>
public static class HostsService
{
    private const string BeginMarker = "# === Implexity Tweaker: блокировка телеметрии (начало) ===";
    private const string EndMarker = "# === Implexity Tweaker: блокировка телеметрии (конец) ===";

    /// <summary>Домены телеметрии и диагностики Windows.</summary>
    private static readonly string[] Domains =
    [
        "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com",
        "telecommand.telemetry.microsoft.com",
        "telemetry.microsoft.com",
        "oca.telemetry.microsoft.com",
        "sqm.telemetry.microsoft.com",
        "watson.telemetry.microsoft.com",
        "redir.metaservices.microsoft.com",
        "choice.microsoft.com",
        "df.telemetry.microsoft.com",
        "reports.wes.df.telemetry.microsoft.com",
        "wes.df.telemetry.microsoft.com",
        "services.wes.df.telemetry.microsoft.com",
        "sqm.df.telemetry.microsoft.com",
        "survey.watson.microsoft.com",
        "statsfe2.ws.microsoft.com",
        "corpext.msitadfs.glbdns2.microsoft.com",
        "compatexchange.cloudapp.net",
        "cs1.wpc.v0cdn.net",
        "a-0001.a-msedge.net",
        "settings-win.data.microsoft.com",
        "fd.api.iris.microsoft.com",
        "geo.prod.do.dsp.mp.microsoft.com",
        "displaycatalog.mp.microsoft.com",
        "watson.microsoft.com",
        "telemetry.api.microsoft.com",
        "browser.events.data.microsoft.com",
        "self.events.data.microsoft.com",
    ];

    private static string HostsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    public static bool IsBlocked()
    {
        try
        {
            return File.Exists(HostsPath) &&
                   File.ReadAllText(HostsPath).Contains(BeginMarker, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Добавляет блокировку в hosts (идемпотентно).</summary>
    public static void Enable()
    {
        var text = File.Exists(HostsPath) ? File.ReadAllText(HostsPath) : string.Empty;
        text = RemoveBlock(text);

        var block = new System.Text.StringBuilder();
        block.AppendLine(BeginMarker);
        foreach (var domain in Domains)
            block.AppendLine($"0.0.0.0 {domain}");

        block.AppendLine(EndMarker);

        if (!text.EndsWith('\n') && text.Length > 0)
            text += Environment.NewLine;

        File.WriteAllText(HostsPath, text + block.ToString());
        FlushDns();
    }

    /// <summary>Убирает добавленную блокировку, остальное в hosts не трогает.</summary>
    public static void Disable()
    {
        if (!File.Exists(HostsPath))
            return;

        var text = RemoveBlock(File.ReadAllText(HostsPath));
        File.WriteAllText(HostsPath, text);
        FlushDns();
    }

    private static string RemoveBlock(string text)
    {
        var start = text.IndexOf(BeginMarker, StringComparison.Ordinal);
        if (start < 0)
            return text;

        var end = text.IndexOf(EndMarker, StringComparison.Ordinal);
        if (end < 0)
            return text[..start];

        end += EndMarker.Length;
        while (end < text.Length && (text[end] == '\r' || text[end] == '\n'))
            end++;

        return text.Remove(start, end - start);
    }

    private static void FlushDns()
    {
        // Сбрасываем DNS-кэш, чтобы правка hosts применилась сразу.
        _ = CommandRunner.RunProcessAsync("ipconfig", "/flushdns");
    }
}
