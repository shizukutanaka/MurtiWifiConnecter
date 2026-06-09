using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;

namespace MWC.Platform.Windows;

/// <summary>
/// Windows標準のNCSI(Network Connectivity Status Indicator)相当の検証。
/// 短いHTTPで疎通+キャプティブポータル判定。
///
/// 仕様:
///   http://www.msftconnecttest.com/connecttest.txt → "Microsoft Connect Test"
///   違う応答 = キャプティブポータル疑い
///
/// プライバシー: 個人特定情報送信せず。User-Agent最小。
/// </summary>
public sealed class HttpConnectivityChecker : IConnectivityChecker
{
    private const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string Expected = "Microsoft Connect Test";

    private static readonly HttpClient _http = CreateClient();
    private readonly ILogger<HttpConnectivityChecker> _log;

    public HttpConnectivityChecker(ILogger<HttpConnectivityChecker> log) => _log = log;

    public async Task<ConnectivityStatus> CheckAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var resp = await _http.GetAsync(ProbeUrl, cts.Token);
            sw.Stop();

            if (!resp.IsSuccessStatusCode)
                return new ConnectivityStatus(false, true, (int)sw.ElapsedMilliseconds);

            string body = await resp.Content.ReadAsStringAsync(cts.Token);
            bool ok = body.Trim() == Expected;
            return new ConnectivityStatus(ok, !ok, (int)sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Connectivity probe failed");
            return new ConnectivityStatus(false, false, null);
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        c.DefaultRequestHeaders.Add("User-Agent", "MWC/1.0");
        c.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return c;
    }
}
