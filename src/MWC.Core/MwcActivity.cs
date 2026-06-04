using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MWC.Core;

/// <summary>
/// MWC OpenTelemetry 計測定義。
///
/// 使い方 (ConnectionExecutor 等で):
/// <code>
/// using var activity = MwcActivity.Source.StartActivity("wifi.connect");
/// activity?.SetTag("ssid", ssid);
/// activity?.SetTag("auth", auth.ToString());
/// </code>
///
/// Prometheus / OTLP エクスポーターは App/Cli 層で設定。
/// Core は ActivitySource と Meter の定義のみ持つ。
/// </summary>
public static class MwcActivity
{
    public const string ServiceName    = "MWC";
    public const string ServiceVersion = "2.4.1";

    /// <summary>分散トレーシング用 ActivitySource</summary>
    public static readonly ActivitySource Source =
        new(ServiceName, ServiceVersion);

    // ── メトリクス ────────────────────────────────────────────────────

    private static readonly Meter _meter = new(ServiceName, ServiceVersion);

    /// <summary>接続試行カウンター</summary>
    public static readonly Counter<long> ConnectAttempts =
        _meter.CreateCounter<long>(
            "mwc.wifi.connect.attempts",
            description: "Total Wi-Fi connection attempts");

    /// <summary>接続成功カウンター</summary>
    public static readonly Counter<long> ConnectSuccesses =
        _meter.CreateCounter<long>(
            "mwc.wifi.connect.successes",
            description: "Total successful Wi-Fi connections");

    /// <summary>接続失敗カウンター</summary>
    public static readonly Counter<long> ConnectFailures =
        _meter.CreateCounter<long>(
            "mwc.wifi.connect.failures",
            description: "Total Wi-Fi connection failures");

    /// <summary>スキャン実行カウンター</summary>
    public static readonly Counter<long> ScanCount =
        _meter.CreateCounter<long>(
            "mwc.wifi.scan.count",
            description: "Total Wi-Fi scan operations");

    /// <summary>スキャンで発見されたネットワーク数のヒストグラム</summary>
    public static readonly Histogram<int> ScanNetworkCount =
        _meter.CreateHistogram<int>(
            "mwc.wifi.scan.network_count",
            description: "Number of networks found per scan");

    /// <summary>接続所要時間(ms)のヒストグラム</summary>
    public static readonly Histogram<double> ConnectDurationMs =
        _meter.CreateHistogram<double>(
            "mwc.wifi.connect.duration_ms",
            unit: "ms",
            description: "Wi-Fi connection duration in milliseconds");

    // ── ヘルパーメソッド ──────────────────────────────────────────────

    /// <summary>接続アクティビティを開始し、タグを付与する</summary>
    public static Activity? StartConnectActivity(string ssid, string auth)
    {
        var activity = Source.StartActivity("wifi.connect");
        activity?.SetTag("wifi.ssid",    ssid);
        activity?.SetTag("wifi.auth",    auth);
        activity?.SetTag("service.name", ServiceName);
        return activity;
    }

    /// <summary>スキャンアクティビティを開始する</summary>
    public static Activity? StartScanActivity(string adapterId)
    {
        var activity = Source.StartActivity("wifi.scan");
        activity?.SetTag("wifi.adapter_id", adapterId);
        return activity;
    }
}
