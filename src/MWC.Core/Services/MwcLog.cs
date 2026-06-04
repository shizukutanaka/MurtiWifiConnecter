using System;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;

namespace MWC.Core.Services;

/// <summary>
/// 高性能構造化ログ定義 (LoggerMessage source generation)。
///
/// .NET の <c>[LoggerMessage]</c> 属性はコンパイル時にログメソッドを生成する。
/// 従来の <c>logger.LogInformation($"...")</c> は:
///   - 文字列補間で常にアロケーションが発生
///   - ログレベルが無効でも文字列を構築してしまう
///   - 構造化ログのフィールドが抽出されない
///
/// LoggerMessage source generation は:
///   - ゼロアロケーション (ログレベル無効時は何もしない)
///   - 構造化フィールドを自動抽出 (検索・集計可能)
///   - コンパイル時に型チェック
///
/// PII (I5) を含めないため、SSID/パスフレーズはログに記録しない。
/// 識別には SSID のハッシュ (短縮) のみを使う。
/// </summary>
public static partial class MwcLog
{
    // ── 接続フロー ──────────────────────────────────────────────

    [LoggerMessage(
        EventId = 1001, Level = LogLevel.Information,
        Message = "接続試行開始 adapter={AdapterId} ssidHash={SsidHash} auth={Auth}")]
    public static partial void ConnectAttempt(
        this ILogger logger, Guid adapterId, string ssidHash, AuthMethod auth);

    [LoggerMessage(
        EventId = 1002, Level = LogLevel.Information,
        Message = "接続成功 adapter={AdapterId} ssidHash={SsidHash} elapsedMs={ElapsedMs}")]
    public static partial void ConnectSucceeded(
        this ILogger logger, Guid adapterId, string ssidHash, long elapsedMs);

    [LoggerMessage(
        EventId = 1003, Level = LogLevel.Warning,
        Message = "接続失敗 adapter={AdapterId} ssidHash={SsidHash} reason={Failure} attempt={Attempt}")]
    public static partial void ConnectFailed(
        this ILogger logger, Guid adapterId, string ssidHash,
        ConnectionFailure failure, int attempt);

    [LoggerMessage(
        EventId = 1004, Level = LogLevel.Information,
        Message = "切断 adapter={AdapterId}")]
    public static partial void Disconnected(this ILogger logger, Guid adapterId);

    [LoggerMessage(
        EventId = 1005, Level = LogLevel.Debug,
        Message = "リトライ待機 attempt={Attempt} delayMs={DelayMs}")]
    public static partial void RetryBackoff(this ILogger logger, int attempt, long delayMs);

    // ── セキュリティ ────────────────────────────────────────────

    [LoggerMessage(
        EventId = 2001, Level = LogLevel.Warning,
        Message = "セキュリティ勧告 code={Code} ssidHash={SsidHash}")]
    public static partial void SecurityAdvisory(
        this ILogger logger, string code, string ssidHash);

    [LoggerMessage(
        EventId = 2002, Level = LogLevel.Warning,
        Message = "Evil Twin の疑い risk={Risk} ssidHash={SsidHash}")]
    public static partial void EvilTwinSuspected(
        this ILogger logger, EvilTwinRisk risk, string ssidHash);

    // ── プラグイン ──────────────────────────────────────────────

    [LoggerMessage(
        EventId = 3001, Level = LogLevel.Error,
        Message = "プラグインエラー plugin={PluginName} hook={Hook}")]
    public static partial void PluginError(
        this ILogger logger, string pluginName, string hook, Exception ex);

    // ── ヘルパー ────────────────────────────────────────────────

    /// <summary>
    /// SSID を PII にならない短いハッシュ文字列に変換する (ログ用)。
    /// 同一 SSID は同一ハッシュになるため追跡可能だが、元の SSID は復元できない。
    /// </summary>
    public static string HashSsid(string ssid)
    {
        if (string.IsNullOrEmpty(ssid)) return "(empty)";
        // FNV-1a 32bit (ゼロ依存、暗号用途ではないがログ識別には十分)
        const uint offset = 2166136261;
        const uint prime  = 16777619;
        uint hash = offset;
        foreach (var c in ssid)
        {
            hash ^= c;
            hash *= prime;
        }
        return hash.ToString("x8");
    }
}
