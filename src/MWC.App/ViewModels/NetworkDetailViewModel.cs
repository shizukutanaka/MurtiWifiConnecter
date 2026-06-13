using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MWC.Core.Models;
using MWC.Core.Services;
using MWC.App.Resources;

namespace MWC.App.ViewModels;

/// <summary>
/// 選択ネットワーク詳細パネル ViewModel。
/// WifiInfoView の「詳細テーブル」を超える情報量:
///  SSID / BSSID全数 / RSSI / PHY世代 / チャンネル / チャンネル幅 /
///  周波数 / Max Speed / ベンダー / 認証方式 / 暗号 / 接続状態
/// </summary>
public sealed partial class NetworkDetailViewModel : ObservableObject
{
    // 解析サービスは全アダプター共有の static インスタンス。
    // 不変条件: Load() / RecordTrustedConnection() は WPF UI スレッドからのみ呼ばれる
    // (await 継続は SynchronizationContext で UI スレッドへ戻る)。これらが
    // EvilTwinDetector の Dictionary を保護なしで触るため、background スレッド
    // (Task.Run / ConfigureAwait(false)) から呼ぶ場合はロックが必要になる。
    private static readonly SecurityAdvisoryService _secAdvisor = new();
    private static readonly NetworkRecommendationEngine _recEngine = new();
    private static readonly RoamingAdvisoryService _roamAdvisor = new();
    private static readonly RssiDistanceEstimator _distEstimator = new();
    private static readonly EvilTwinDetector _evilTwin = new();
    private static readonly HandoverPredictor _handover = new();
    private static readonly InterferenceAnalyzer _interferenceAnalyzer = new();
    private static readonly MeshNetworkDetector _meshDetector = new();
    private static readonly PowerSaveAdvisorService _powerSaveAdvisor = new();
    private static readonly LinkRateEstimator _linkRate = new();
    private static readonly MloAnalyzerService _mloAnalyzer = new();

    [ObservableProperty] private string _ssid = "";

    public string SsidOrHint =>
        string.IsNullOrEmpty(Ssid) || Ssid == "-" ? L.MainSelectNetworkHint : Ssid;

    partial void OnSsidChanged(string value) => OnPropertyChanged(nameof(SsidOrHint));

    [ObservableProperty] private string _authLabel = "";
    [ObservableProperty] private string _cipherLabel = "";
    [ObservableProperty] private string _phyLabel = "";
    [ObservableProperty] private string _bandLabel = "";
    [ObservableProperty] private string _channelLabel = "";
    [ObservableProperty] private string _frequencyLabel = "";
    [ObservableProperty] private string _speedLabel = "";
    [ObservableProperty] private string _signalLabel = "";
    [ObservableProperty] private string _distanceLabel = "";
    [ObservableProperty] private string _roamingLabel = "";
    [ObservableProperty] private string _interferenceLabel = "";
    [ObservableProperty] private string _meshLabel = "";
    [ObservableProperty] private string _powerSaveLabel = "";
    [ObservableProperty] private string _linkEstimateLabel = "";
    [ObservableProperty] private string _mloLabel = "";
    [ObservableProperty] private string _predictedSignalLabel = "";

    // 大半のネットワークでは空になる行は、勧告パネル同様、値があるときだけ表示する
    // (情報過多を避ける — CLAUDE.md「性能 vs 可読性 → 可読性」)
    [ObservableProperty] private bool _hasMlo;
    [ObservableProperty] private bool _hasMesh;
    [ObservableProperty] private bool _hasPredictedSignal;
    [ObservableProperty] private string _statusLabel = "";
    [ObservableProperty] private string _vendorLabel = "";
    [ObservableProperty] private bool _hasProfile;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isDfs;
    [ObservableProperty] private double _recommendationScore;
    [ObservableProperty] private string _recommendationSummary = "";
    [ObservableProperty] private IReadOnlyList<SecurityAdvisoryItem> _securityAdvisories
        = Array.Empty<SecurityAdvisoryItem>();
    public bool HasSecurityAdvisories => SecurityAdvisories.Count > 0;
    [ObservableProperty] private IReadOnlyList<BssDetailRow> _bssRows = Array.Empty<BssDetailRow>();

    partial void OnSecurityAdvisoriesChanged(IReadOnlyList<SecurityAdvisoryItem> value)
        => OnPropertyChanged(nameof(HasSecurityAdvisories));

    public static void RecordTrustedConnection(WifiNetwork network)
    {
        // 接続した SSID を提供する全 BSS を信頼集合に記録する。
        // BssEntries[0] のみだと、同一 ESS の別 AP へ正規にローミングした際に
        // ベンダー/BSSID 不一致として誤検知してしまう。
        foreach (var bss in network.BssEntries)
            _evilTwin.RecordTrusted(network.Ssid, bss.Bssid, network.Auth);
    }

    public void Load(WifiNetwork? n,
                     IReadOnlyList<WifiNetwork>? allNetworks = null,
                     TimeSpan? connectedDuration = null,
                     IReadOnlyList<int>? rssiHistory = null)
    {
        if (n is null)
        {
            Ssid = "-";
            AuthLabel = CipherLabel = PhyLabel = BandLabel = VendorLabel = "";
            ChannelLabel = FrequencyLabel = SpeedLabel = SignalLabel = StatusLabel = "";
            DistanceLabel = RoamingLabel = InterferenceLabel = MeshLabel = PowerSaveLabel = "";
            LinkEstimateLabel = MloLabel = PredictedSignalLabel = "";
            HasMlo = HasMesh = HasPredictedSignal = false;
            IsDfs = false;
            RecommendationScore = 0;
            RecommendationSummary = "";
            SecurityAdvisories = Array.Empty<SecurityAdvisoryItem>();
            BssRows = Array.Empty<BssDetailRow>();
            return;
        }

        var visible = allNetworks ?? Array.Empty<WifiNetwork>();

        Ssid     = n.Ssid;
        IsConnected = n.IsConnected;
        HasProfile  = n.HasProfile;

        AuthLabel    = n.Auth.ToString();
        CipherLabel  = n.Cipher.ToString();
        PhyLabel     = n.Phy.ToGenerationLabel();
        BandLabel    = n.Band switch
        {
            WifiBand.Band2_4GHz => "2.4 GHz",
            WifiBand.Band5GHz   => "5 GHz",
            WifiBand.Band6GHz   => "6 GHz (Wi-Fi 6E/7)",
            _ => "Unknown"
        };
        IsDfs = DfsChannelHelper.IsDfsChannel(n);
        ChannelLabel = n.Channel > 0
            ? $"Ch {n.Channel}" + (n.ChannelWidth > 0 ? $"  ({n.ChannelWidth} MHz)" : "")
                       + (DfsChannelHelper.IsDfsChannel(n) ? "  ⚡ DFS" : "")
            : "-";
        FrequencyLabel = n.FrequencyMhz.HasValue
            ? $"{n.FrequencyMhz} MHz"
            : n.Channel > 0 ? ChannelToFreq(n.Channel) : "-";
        SpeedLabel   = n.MaxLinkSpeedMbps.HasValue
            ? $"{n.MaxLinkSpeedMbps} Mbps"
            : "-";
        SignalLabel  = $"{n.SignalQuality}%"
                       + (n.Rssi.HasValue ? $"  ({n.Rssi} dBm)" : "")
                       + "  " + BuildBar(n.SignalQuality);

        var predicted = rssiHistory is { Count: >= 3 }
            ? SignalQualityPredictor.PredictFromHistory(rssiHistory)
            : null;
        HasPredictedSignal = predicted.HasValue;
        PredictedSignalLabel = predicted.HasValue
            ? $"~{predicted:F0} dBm  ({rssiHistory!.Count} samples)"
            : "-";

        if (n.Rssi.HasValue)
        {
            var le = _linkRate.Estimate(n.Rssi.Value,
                channelWidthMhz: n.ChannelWidth > 0 ? n.ChannelWidth : 80);
            LinkEstimateLabel = $"MCS {le.MaxMcs}  {le.PhyRateMbps} Mbps PHY  (~{le.EffectiveMbps} Mbps effective)  SNR {le.SnrDb} dB";
        }
        else
        {
            LinkEstimateLabel = "-";
        }

        var mlo = _mloAnalyzer.Analyze(n);
        HasMlo = mlo.IsMlo;
        MloLabel = mlo.IsMlo
            ? $"{mlo.LinkCount} links  ({FormatBands(mlo.Bands)})  {mlo.AggregatedMbps:F0} Mbps aggregate  ({mlo.ReliabilityTier})"
            : "-";

        var dist = _distEstimator.Estimate(n);
        DistanceLabel = dist.Confidence != DistanceConfidence.Unknown ? dist.Label : "-";

        var roaming = _roamAdvisor.Analyze(n);
        RoamingLabel = roaming.Tier switch
        {
            RoamingTier.Seamless => $"Seamless ({string.Join("/", roaming.SupportedStandards)})",
            RoamingTier.Fast     => $"Fast (802.11r)  ~{roaming.EstimatedHandoverMs}ms",
            RoamingTier.Assisted => "Assisted (802.11k/v)",
            _                    => $"Standard  ~{roaming.EstimatedHandoverMs}ms"
        };

        var iReport = _interferenceAnalyzer.Analyze(n, visible);
        InterferenceLabel = iReport.Level == InterferenceLevel.Low
            ? $"Low  ({iReport.Score}/100)"
            : $"{iReport.Level}  ({iReport.Score}/100)  — {iReport.Factors.FirstOrDefault() ?? iReport.Recommendation}";

        var meshGroups = _meshDetector.Detect(visible);
        var myGroup = meshGroups.FirstOrDefault(g =>
            string.Equals(g.Ssid, n.Ssid, StringComparison.Ordinal));
        HasMesh = myGroup is not null;
        MeshLabel = myGroup is null ? "-"
            : $"{myGroup.NodeCount} nodes"
              + (myGroup.IsTriBand ? " · Tri-band" : myGroup.Has6GHz ? " · 6 GHz" : "")
              + (myGroup.HasFastTransition ? " · 802.11r" : "")
              + $"  ({myGroup.Confidence})";

        var ps = _powerSaveAdvisor.Analyze(n);
        PowerSaveLabel = ps.Tier switch
        {
            PowerSaveTier.Advanced => $"rTWT  (~{ps.EstimatedSavingPercent}% battery saving)",
            PowerSaveTier.Standard => $"TWT  (~{ps.EstimatedSavingPercent}% battery saving)",
            _                      => "Legacy (DTIM/PSM)"
        };

        VendorLabel  = n.VendorName ?? "";
        StatusLabel  = n.IsConnected ? MWC.App.Resources.L.Get("Detail_Connected")
                     : n.HasProfile  ? MWC.App.Resources.L.Get("Detail_HasProfile")
                     : MWC.App.Resources.L.Get("Detail_NotConnected");

        // セキュリティ勧告 + Evil Twin 検査 + スティッキークライアント検査
        var advisories = _secAdvisor.Analyze(n)
            .Select(a => new SecurityAdvisoryItem(a.Title, a.Severity))
            .ToList();

        var twin = _evilTwin.Analyze(n, visible);
        if (twin.Risk == EvilTwinRisk.HighRisk)
            advisories.Insert(0, new SecurityAdvisoryItem(
                "Evil Twin: High Risk — multiple spoofing indicators detected",
                AdvisorySeverity.Critical));
        else if (twin.Risk == EvilTwinRisk.Suspicious)
            advisories.Insert(0, new SecurityAdvisoryItem(
                "Evil Twin: Suspicious — possible rogue AP with same SSID",
                AdvisorySeverity.Warning));

        if (n.IsConnected && n.Rssi.HasValue && connectedDuration.HasValue &&
            _handover.IsStickyClient(n.Rssi.Value, connectedDuration.Value))
            advisories.Add(new SecurityAdvisoryItem(
                "Sticky Client: Signal too weak — consider moving closer or reconnecting",
                AdvisorySeverity.Warning));

        SecurityAdvisories = advisories;
        var score = _recEngine.Score(n);
        RecommendationScore   = Math.Round(score.Total, 0);
        RecommendationSummary = _recEngine.Explain(score).Summary;

        BssRows = n.BssEntries
            .Select(b => new BssDetailRow(
                b.Bssid,
                b.Rssi,
                b.Channel,
                b.FrequencyMhz > 0 ? $"{b.FrequencyMhz} MHz" : ChannelToFreq(b.Channel),
                b.Phy.ToShortLabel(),
                b.ChannelWidth > 0 ? $"{b.ChannelWidth} MHz" : "-"))
            .ToList();
    }

    private static string BuildBar(int q)
    {
        int f = q / 10;
        return "[" + new string('█', f) + new string('░', 10 - f) + "]";
    }

    private static string FormatBands(IReadOnlyList<WifiBand> bands)
        => string.Join("+", bands.Select(b => b switch
        {
            WifiBand.Band2_4GHz => "2.4",
            WifiBand.Band5GHz   => "5",
            WifiBand.Band6GHz   => "6",
            _                   => "?"
        })) + " GHz";

    private static string ChannelToFreq(int ch) => ch switch
    {
        >= 1 and <= 14 => $"{2412 + (ch - 1) * 5} MHz",
        36  => "5180 MHz", 40 => "5200 MHz", 44 => "5220 MHz", 48 => "5240 MHz",
        52  => "5260 MHz", 56 => "5280 MHz", 60 => "5300 MHz", 64 => "5320 MHz",
        100 => "5500 MHz", 104 => "5520 MHz", 108 => "5540 MHz", 112 => "5560 MHz",
        116 => "5580 MHz", 120 => "5600 MHz", 124 => "5620 MHz", 128 => "5640 MHz",
        132 => "5660 MHz", 136 => "5680 MHz", 140 => "5700 MHz", 144 => "5720 MHz",
        149 => "5745 MHz", 153 => "5765 MHz", 157 => "5785 MHz", 161 => "5805 MHz",
        165 => "5825 MHz",
        // 6 GHz (Wi-Fi 6E)
        1   => "5955 MHz", 5 => "5975 MHz", 9 => "5995 MHz", 13 => "6015 MHz",
        17  => "6035 MHz", 21 => "6055 MHz", 25 => "6075 MHz", 29 => "6095 MHz",
        _ => "-"
    };
}

public sealed record BssDetailRow(
    string Bssid,
    int Rssi,
    int Channel,
    string Frequency,
    string PhyLabel,
    string ChannelWidth
);

public sealed record SecurityAdvisoryItem(
    string Title,
    AdvisorySeverity Severity)
{
    public string SeverityColor => Severity switch
    {
        AdvisorySeverity.Critical => "#EF4444",
        AdvisorySeverity.Warning  => "#F59E0B",
        AdvisorySeverity.Info     => "#3B82F6",
        AdvisorySeverity.Good     => "#22C55E",
        _ => "#6B7280"
    };
}
