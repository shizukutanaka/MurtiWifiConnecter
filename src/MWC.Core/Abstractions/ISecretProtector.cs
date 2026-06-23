using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Models;

namespace MWC.Core.Abstractions;

/// <summary>
/// パスワード等の永続化保護(Win=DPAPI、テスト=メモリ)
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

/// <summary>
/// 実接続後のインターネット疎通確認
/// </summary>
public interface IConnectivityChecker
{
    /// <summary>
    /// NCSI相当の検証。MS標準のmsftconnecttest.com利用。
    ///
    /// <paramref name="adapterId"/> を指定すると、そのアダプターのローカル IP に
    /// プローブ用ソケットをバインドし、検証トラフィックを<b>当該アダプター経由</b>で
    /// 送出する。複数の無線アダプターを独立管理する本ツールでは、既定ルート
    /// (別アダプター) の疎通を誤って「今接続したアダプターの結果」として報告しない
    /// ために必須。null の場合は既定ルートで送出する (バインド無し)。
    /// </summary>
    Task<ConnectivityStatus> CheckAsync(Guid? adapterId = null, CancellationToken ct = default);
}

public readonly record struct ConnectivityStatus(
    bool HasInternet,
    bool CaptivePortalDetected,
    int? LatencyMs);

public interface IProfileStore
{
    Task SaveAsync(WifiProfileSpec spec, CancellationToken ct = default);
    Task<WifiProfileSpec?> LoadAsync(string ssid, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(string ssid, CancellationToken ct = default);
}
