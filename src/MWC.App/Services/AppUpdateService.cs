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

    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    });
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

            // GitHub API は未認証時 60 req/h でレート制限され、その際 tag_name を持たない
            // エラー JSON ({"message":"API rate limit exceeded",...}) を返す。GetProperty だと
            // KeyNotFoundException を投げ、誤解を招くスタックトレース付きで "Update check failed"
            // ログになる。TryGetProperty で「リリースではない」応答を静かに Failed 扱いにする。
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tag_name", out var tagEl))
                return UpdateCheckResult.Failed;

            var tag        = tagEl.GetString() ?? "";
            var url        = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            var body       = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            var prerelease = root.TryGetProperty("prerelease", out var preEl)
                             && preEl.ValueKind == JsonValueKind.True;

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
