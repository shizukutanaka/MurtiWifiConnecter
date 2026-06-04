using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// スキャン結果エクスポート。WifiInfoView/NetSpot/inSSIDer の標準機能に相当。
///
/// 出力形式:
///  - CSV: Excel/スプレッドシート向け
///  - JSON: スクリプト/API 連携向け
///  - TXT: 人間向けテキストレポート
/// </summary>
public static class ExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ───── CSV ─────
    public static void ToCsv(IEnumerable<WifiNetwork> networks, string path)
    {
        ArgumentNullException.ThrowIfNull(networks);
        using var w = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        // ヘッダ
        w.WriteLine("SSID,BSSID(1st),Signal(%),RSSI(dBm),Band,Channel,ChannelWidth(MHz)," +
                    "PHY,Auth,Cipher,MaxSpeed(Mbps),Vendor,Connected,HasProfile,ScannedAt");

        var at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        foreach (var n in networks)
        {
            var bssid = n.BssEntries.Count > 0 ? n.BssEntries[0].Bssid : "";
            w.WriteLine(string.Join(",",
                CsvEscape(n.Ssid),
                CsvEscape(bssid),
                n.SignalQuality,
                n.Rssi?.ToString() ?? "",
                n.Band.ToString(),
                n.Channel,
                n.ChannelWidth > 0 ? n.ChannelWidth.ToString() : "",
                n.Phy.ToShortLabel(),
                n.Auth,
                n.Cipher,
                n.MaxLinkSpeedMbps?.ToString() ?? "",
                CsvEscape(n.VendorName ?? ""),
                n.IsConnected,
                n.HasProfile,
                at));
        }
    }

    // ───── JSON ─────
    public static void ToJson(IEnumerable<WifiNetwork> networks, string path)
    {
        ArgumentNullException.ThrowIfNull(networks);
        var payload = new ExportPayload
        {
            ScannedAt = DateTimeOffset.UtcNow,
            Networks = new List<WifiNetwork>(networks)
        };
        File.WriteAllText(path,
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8);
    }

    // ───── TXT ─────
    public static void ToText(IEnumerable<WifiNetwork> networks, string path)
    {
        ArgumentNullException.ThrowIfNull(networks);
        var sb = new StringBuilder();
        sb.AppendLine("MWC Scan Report");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine(new string('─', 72));
        sb.AppendLine();

        int i = 1;
        foreach (var n in networks)
        {
            sb.AppendLine($"[{i++:D3}] {n.Ssid}{(n.IsConnected ? "  ← Connected" : "")}");
            sb.AppendLine($"     Signal   : {n.SignalQuality}%  {BuildBar(n.SignalQuality)}" +
                          (n.Rssi.HasValue ? $"  ({n.Rssi} dBm)" : ""));
            sb.AppendLine($"     Auth     : {n.Auth}  /  {n.Cipher}");
            sb.AppendLine($"     Band     : {BandLabel(n.Band)}  Ch {n.Channel}" +
                          (n.ChannelWidth > 0 ? $"  ({n.ChannelWidth} MHz wide)" : ""));
            sb.AppendLine($"     PHY      : {n.Phy.ToGenerationLabel()}");
            if (n.MaxLinkSpeedMbps.HasValue)
                sb.AppendLine($"     Max Speed: {n.MaxLinkSpeedMbps} Mbps");
            if (n.BssEntries.Count > 0)
            {
                sb.AppendLine($"     BSSIDs   :");
                foreach (var b in n.BssEntries)
                    sb.AppendLine($"               {b.Bssid}  {b.Rssi} dBm  Ch{b.Channel}" +
                                  (string.IsNullOrEmpty(n.VendorName) ? "" : $"  ({n.VendorName})"));
            }
            if (n.HasProfile)
                sb.AppendLine($"     Profile  : {n.ProfileName ?? "(saved)"}");
            sb.AppendLine();
        }
        sb.AppendLine(new string('─', 72));
        sb.AppendLine($"Total: {i - 1} networks");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    // ───── helpers ─────
    private static string CsvEscape(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    private static string BuildBar(int quality)
    {
        int filled = (int)Math.Round(quality / 10.0);
        return "[" + new string('█', filled) + new string('░', 10 - filled) + "]";
    }

    private static string BandLabel(WifiBand b) => b switch
    {
        WifiBand.Band2_4GHz => "2.4 GHz",
        WifiBand.Band5GHz   => "5 GHz",
        WifiBand.Band6GHz   => "6 GHz (Wi-Fi 6E/7)",
        _ => "Unknown"
    };

    private sealed class ExportPayload
    {
        public DateTimeOffset ScannedAt { get; init; }
        public List<WifiNetwork> Networks { get; init; } = new();
    }
}
