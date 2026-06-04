using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MWC.App.Services;

/// <summary>
/// GitHub Releases API で最新バージョンを確認する。
/// Apple "Software Update" に相当。プライバシー: バージョン番号のみ送信。
/// </summary>
public sealed class AppUpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/shizukutanaka/MurtiWifiConnecter/releases/latest";

    private static readonly HttpClient _http = new();
    private readonly ILogger<AppUpdateService> _log;

    static AppUpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", $"MWC/{App.Version}");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    public AppUpdateService(ILogger<AppUpdateService> log) => _log = log;

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl, ct);
            using var doc  = JsonDocument.Parse(json);
            var root       = doc.RootElement;
            var tag        = root.GetProperty("tag_name").GetString() ?? "";
            var url        = root.GetProperty("html_url").GetString()  ?? "";
            var body       = root.GetProperty("body").GetString()       ?? "";
            var prerelease = root.GetProperty("prerelease").GetBoolean();

            var latest  = Version.TryParse(tag.TrimStart('v'), out var v) ? v : null;
            var current = Version.TryParse(App.Version, out var cv) ? cv : null;

            bool hasUpdate = latest is not null && current is not null
                             && latest > current && !prerelease;

            return new UpdateCheckResult(
                HasUpdate:     hasUpdate,
                LatestVersion: tag,
                ReleaseUrl:    url,
                ReleaseNotes:  body.Length > 500 ? body[..500] + "…" : body,
                CheckedAt:     DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Update check failed");
            return UpdateCheckResult.Failed;
        }
    }
}

public sealed record UpdateCheckResult(
    bool           HasUpdate,
    string         LatestVersion,
    string         ReleaseUrl,
    string         ReleaseNotes,
    DateTimeOffset CheckedAt)
{
    public static UpdateCheckResult Failed =>
        new(false, "", "", "", DateTimeOffset.UtcNow);
}
