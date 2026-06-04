using MWC.Core.Models;
using MWC.Core.Services;

namespace MWC.App.ViewModels;

/// <summary>
/// NetworkItemViewModel に Apple "Clarity" プロパティを追加する
/// 部分クラス拡張。
///
/// 元ファイルに partial を追加すればそのまま統合可能。
/// 独立ファイルとして管理することで MainViewModel の肥大化を防ぐ。
/// </summary>
public static class NetworkItemViewModelAppleExtensions
{
    // ───── SecurityBadge ─────

    /// <summary>セキュリティバッジ(例: "最高セキュリティ", "暗号化なし")</summary>
    public static SecurityBadge GetBadge(this NetworkItemViewModel vm)
        => SecurityBadgeService.GetBadge(vm.Auth);

    /// <summary>信号の人間語ラベル(例: "優良", "弱い")</summary>
    public static string GetSignalLabel(this NetworkItemViewModel vm)
        => SecurityBadgeService.GetSignalLabel(vm.Signal);

    /// <summary>セキュリティレベルに対応した色コード</summary>
    public static string GetBadgeColor(this NetworkItemViewModel vm)
        => SecurityBadgeService.GetBadge(vm.Auth).Level switch
        {
            SecurityLevel.Excellent => "#22C55E",  // 緑
            SecurityLevel.Good      => "#3B82F6",  // 青
            SecurityLevel.Fair      => "#F59E0B",  // 黄
            SecurityLevel.Weak      => "#F97316",  // オレンジ
            SecurityLevel.Danger    => "#EF4444",  // 赤
            _ => "#9CA3AF"
        };

    /// <summary>信号バーの色(信号強度で変化)</summary>
    public static string GetSignalColor(this NetworkItemViewModel vm)
        => vm.Signal switch
        {
            >= 80 => "#22C55E",
            >= 60 => "#A3E635",
            >= 35 => "#F59E0B",
            _     => "#EF4444"
        };
}
