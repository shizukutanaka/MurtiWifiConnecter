using System;

namespace MWC.Core.Services;

/// <summary>
/// 指数バックオフ + ジッターによるリトライ遅延計算。
///
/// 固定バックオフ (例: 500ms → 1000ms) は、複数クライアントが同時に
/// 再接続を試みると同じタイミングで衝突する (thundering herd)。
/// ジッターを加えることで再試行を時間的に分散させ、衝突を避ける。
///
/// AWS の "Exponential Backoff And Jitter" 記事の Full Jitter 方式を採用:
///   delay = random(0, min(cap, base * 2^attempt))
///
/// ゼロ外部依存。Polly の代替として軽量実装。
/// </summary>
public sealed class RetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;
    private readonly int      _maxAttempts;
    private readonly Random   _rng;

    public RetryPolicy(
        TimeSpan? baseDelay   = null,
        TimeSpan? maxDelay    = null,
        int       maxAttempts = 3,
        Random?   rng         = null)
    {
        _baseDelay   = baseDelay ?? TimeSpan.FromMilliseconds(500);
        _maxDelay    = maxDelay  ?? TimeSpan.FromSeconds(8);
        _maxAttempts = maxAttempts;
        _rng         = rng ?? Random.Shared;
    }

    /// <summary>最大リトライ回数。</summary>
    public int MaxAttempts => _maxAttempts;

    /// <summary>
    /// 指定試行回数 (0始まり) に対する遅延を Full Jitter 方式で計算する。
    /// </summary>
    public TimeSpan ComputeDelay(int attempt)
    {
        if (attempt < 0) attempt = 0;

        // base * 2^attempt (オーバーフロー防止のため double で計算)
        double exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        double capped      = Math.Min(exponential, _maxDelay.TotalMilliseconds);

        // Full Jitter: [0, capped] の一様乱数
        double jittered = _rng.NextDouble() * capped;

        return TimeSpan.FromMilliseconds(jittered);
    }

    /// <summary>
    /// 上限を考慮した遅延 (ジッターなし、決定論的)。テスト/表示用。
    /// </summary>
    public TimeSpan ComputeDeterministicDelay(int attempt)
    {
        if (attempt < 0) attempt = 0;
        double exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(exponential, _maxDelay.TotalMilliseconds));
    }

    /// <summary>
    /// この失敗種別がリトライ可能かどうかを判定する。
    /// 決定的失敗 (同じ入力なら必ず同じ結果になるもの: 認証失敗・権限不足・
    /// 不正プロファイル・ユーザーによるキャンセル) は再試行しても無意味なため false。
    /// 一時的失敗 (Timeout/NotInRange/OsError) と分類不能 (Unknown) は true —
    /// Unknown を true に倒すのは「原因不明の失敗は電波状況等の一時要因である
    /// 可能性が高く、リトライのコスト (数秒の待機) が誤分類の害より小さい」ため。
    /// </summary>
    public static bool IsRetriable(MWC.Core.Models.ConnectionFailure failure) => failure switch
    {
        MWC.Core.Models.ConnectionFailure.BadCredentials       => false,
        MWC.Core.Models.ConnectionFailure.InsufficientPrivilege => false,
        MWC.Core.Models.ConnectionFailure.AdapterDisabled      => false,
        MWC.Core.Models.ConnectionFailure.AdapterNotFound      => false,
        MWC.Core.Models.ConnectionFailure.InvalidProfile       => false,
        MWC.Core.Models.ConnectionFailure.ProfileRejected      => false,
        MWC.Core.Models.ConnectionFailure.Cancelled            => false,
        MWC.Core.Models.ConnectionFailure.Timeout              => true,
        MWC.Core.Models.ConnectionFailure.NotInRange           => true,
        _                                                       => true
    };
}
