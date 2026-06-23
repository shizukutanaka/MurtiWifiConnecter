using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
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
/// マルチアダプター対応: <see cref="CheckAsync"/> にアダプター ID を渡すと、その
/// アダプターのローカル IP へソケットをバインドし、プローブが当該アダプター経由で
/// 送出されるようにする。バインドできない場合は既定ルートへフォールバックするため、
/// 従来挙動からの後退は無い (= 取得精度が上がるか、最悪でも従来と同等)。
///
/// プライバシー: 個人特定情報送信せず。User-Agent最小。
/// </summary>
public sealed class HttpConnectivityChecker : IConnectivityChecker
{
    private const string ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt";
    private const string Expected = "Microsoft Connect Test";

    // プローブ毎にバインドすべきローカル IP を ConnectCallback へ受け渡すためのキー。
    private static readonly HttpRequestOptionsKey<IPAddress?> LocalBindKey =
        new("mwc.local-bind");

    private static readonly HttpClient _http = CreateClient();
    private readonly ILogger<HttpConnectivityChecker> _log;

    public HttpConnectivityChecker(ILogger<HttpConnectivityChecker> log) => _log = log;

    public async Task<ConnectivityStatus> CheckAsync(
        Guid? adapterId = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            using var req = new HttpRequestMessage(HttpMethod.Get, ProbeUrl);
            // 指定アダプターのローカル IP を解決できた場合のみソケットをそのアドレスに
            // バインドする。解決不能/未指定なら null → ConnectCallback はバインドせず
            // 既定ルートで送出するため、既存挙動からの後退は無い。
            req.Options.Set(LocalBindKey,
                adapterId is { } id ? ResolveLocalIp(id) : null);

            using var resp = await _http.SendAsync(req, cts.Token);
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

    /// <summary>
    /// アダプター Guid → そのインターフェースの IPv4 ユニキャストアドレス。
    /// 見つからない/未割当(リンク未確立)の場合は null を返し、呼び出し側は
    /// バインドせず既定ルートへフォールバックする。
    /// Windows の <see cref="NetworkInterface.Id"/> は "{GUID}" 形式の文字列。
    /// </summary>
    private static IPAddress? ResolveLocalIp(Guid adapterId)
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (!Guid.TryParse(ni.Id.Trim('{', '}'), out var g) || g != adapterId) continue;

            return ni.GetIPProperties().UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork
                                     && !IPAddress.IsLoopback(a));
        }
        return null;
    }

    private static HttpClient CreateClient()
    {
        // SocketsHttpHandler + PooledConnectionLifetime で DNS の鮮度を保つ。
        // 長時間稼働セッションでプール接続が固定 IP に張り付くと、ネットワーク
        // 変更後に msftconnecttest の旧 IP へ繋ぎ続けてプローブが失敗し、
        // 実際は疎通があるのに「インターネット無し」と誤判定しうる。
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect        = false,
            UseCookies               = false,
            UseProxy                 = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback          = BindToRequestedLocalIpAsync,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        c.DefaultRequestHeaders.Add("User-Agent", "MWC/1.0");
        c.DefaultRequestHeaders.CacheControl =
            new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        return c;
    }

    // 要求に紐づくローカル IP があればソケットを当該アドレスにバインドしてから接続する。
    // これによりプローブが「今接続したアダプター」経由で送出され、既定ルート側
    // (別アダプター) の疎通を当該アダプターの結果として誤報告することを防ぐ。
    // 注: プール再利用された接続は本コールバックを経由しないため、直前に別アダプターで
    // 張った接続が稀に再利用されうるが、その場合でも「バインド無し=従来挙動」と同等で
    // あり後退は無い。接続直後の検証時はプールが空/別ホストで新規接続になる場合が多い。
    private static async ValueTask<Stream> BindToRequestedLocalIpAsync(
        SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            if (ctx.InitialRequestMessage.Options.TryGetValue(LocalBindKey, out var local)
                && local is not null)
                socket.Bind(new IPEndPoint(local, 0));

            await socket.ConnectAsync(ctx.DnsEndPoint, ct).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
