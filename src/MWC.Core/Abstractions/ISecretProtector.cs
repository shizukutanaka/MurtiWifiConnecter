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
    /// <summary>NCSI相当の検証。MS標準のmsftconnecttest.com利用</summary>
    Task<ConnectivityStatus> CheckAsync(CancellationToken ct = default);
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
