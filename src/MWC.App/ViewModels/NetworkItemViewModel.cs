using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;
using MWC.Core.Models;
using MWC.Core.Profile;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

public sealed partial class NetworkItemViewModel : ObservableObject
{
    [ObservableProperty] private string     _ssid        = "";
    [ObservableProperty] private int        _signal;
    [ObservableProperty] private AuthMethod _auth;
    [ObservableProperty] private bool       _isConnected;
    [ObservableProperty] private bool       _hasProfile;
    [ObservableProperty] private string     _phyLabel    = "";
    [ObservableProperty] private string     _vendorLabel = "";

    public WifiNetwork Source { get; private set; } = null!;

    public NetworkItemViewModel(WifiNetwork n) { Source = n; Update(n); }

    public void Update(WifiNetwork n)
    {
        Source      = n;
        Ssid        = n.Ssid;
        Signal      = n.SignalQuality;
        Auth        = n.Auth;
        IsConnected = n.IsConnected;
        HasProfile  = n.HasProfile;
        PhyLabel    = MWC.App.Resources.L.PhyShortLabel(n.Phy);
        VendorLabel = n.VendorName ?? "";
        OnPropertyChanged(nameof(BandLabel));
        OnPropertyChanged(nameof(IsDfs));
    }

    // 段階判定は SignalIconService (Core, WCAG 1.4.1 の非色覚依存表現のために設計) に一元化。
    // 以前はここに独自閾値 (75/50/25/>0) の重複実装があり、Core の正式基準 (80/60/40/20) と
    // 食い違っていた (2026-07 品質パスで統一。docs/FEATURE-AUDIT.md §1a 参照)。
    public int    Bars                 => SignalIconService.Describe(Signal).Bars;
    public string SignalAutomationLabel =>
        $"{MWC.App.Resources.L.MainSignalStrength(Signal)} · {SecurityBadgeLabel}";

    // ── Pinned state ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isPinned;

    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinMenuHeader));

    public string PinMenuHeader => IsPinned
        ? MWC.App.Resources.L.ContextMenuUnpinNetwork
        : MWC.App.Resources.L.ContextMenuPinNetwork;

    // ── Signal trend indicator ────────────────────────────────────────
    [ObservableProperty] private string _signalTrendLabel = "";

    // ── Security level badge ─────────────────────────────────────────
    public SecurityLevel SecurityLevel  => SecurityBadgeService.GetBadge(Auth).Level;
    public string SecurityBadgeLabel    => MWC.App.Resources.L.SecurityLevelLabel(SecurityLevel);
    public string SecurityTechLabel     => SecurityBadgeService.GetBadge(Auth).TechLabel;

    // ── DFS channel indicator ────────────────────────────────────────
    public bool IsDfs => DfsChannelHelper.IsDfsChannel(Source);

    // ── Channel congestion indicator ─────────────────────────────────
    [ObservableProperty] private int  _congestionPercent;
    [ObservableProperty] private bool _isChannelOverloaded;

    public bool IsChannelCrowded => CongestionPercent >= 30;

    public string? CongestionTooltip => CongestionPercent < 30 ? null
        : IsChannelOverloaded
            ? MWC.App.Resources.L.CongestionOverloadedTooltip(CongestionPercent)
            : MWC.App.Resources.L.CongestionBusyTooltip(CongestionPercent);

    partial void OnCongestionPercentChanged(int value)
    {
        OnPropertyChanged(nameof(IsChannelCrowded));
        OnPropertyChanged(nameof(CongestionTooltip));
    }

    partial void OnSignalChanged(int value) => OnPropertyChanged(nameof(SignalAutomationLabel));
    partial void OnAuthChanged(AuthMethod value)
    {
        OnPropertyChanged(nameof(AuthLabel));
        OnPropertyChanged(nameof(SecurityLevel));
        OnPropertyChanged(nameof(SecurityBadgeLabel));
        OnPropertyChanged(nameof(SecurityTechLabel));
        OnPropertyChanged(nameof(SignalAutomationLabel));
    }

    public string AuthLabel => MWC.App.Resources.L.AuthCompact(Auth);
    public string BandLabel => MWC.App.Resources.L.BandCompact(Source.Band);
}
