using System.Net.Http;
using System.Text.Json;

namespace ImplexityTweaker.Services;

/// <summary>Проверка обновлений твикера на GitHub (релизы).</summary>
public static class UpdateCheckerService
{
    private const string ReleasesApi = "https://api.github.com/repos/0kar1d3/ImplexityTweaker/releases/latest";

    public enum UpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        Failed
    }

    public sealed record UpdateResult(UpdateStatus Status, string LatestVersion, string Url);

    public static async Task<UpdateResult> CheckAsync(string currentVersion)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ImplexityTweaker");

            var json = await http.GetStringAsync(ReleasesApi);
            using var doc = JsonDocument.Parse(json);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? string.Empty
                : string.Empty;
            var url = doc.RootElement.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString() ?? string.Empty
                : string.Empty;

            var latest = tag.TrimStart('v', 'V').Trim();
            if (latest.Length == 0)
                return new UpdateResult(UpdateStatus.Failed, string.Empty, string.Empty);

            return IsNewer(latest, currentVersion)
                ? new UpdateResult(UpdateStatus.UpdateAvailable, latest, url)
                : new UpdateResult(UpdateStatus.UpToDate, latest, url);
        }
        catch
        {
            return new UpdateResult(UpdateStatus.Failed, string.Empty, string.Empty);
        }
    }

    private static bool IsNewer(string latest, string current)
    {
        static Version? Parse(string s)
        {
            var clean = s.Split('-', '+')[0];
            return Version.TryParse(clean, out var v) ? v : null;
        }

        var a = Parse(latest);
        var b = Parse(current);
        if (a == null || b == null)
            return false;

        return a > b;
    }
}
