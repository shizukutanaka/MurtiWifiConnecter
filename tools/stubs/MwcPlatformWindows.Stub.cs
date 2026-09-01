// ─────────────────────────────────────────────────────────────────────────────
//  MWC.Platform.Windows の **型検査専用スタブ**。製品には一切含めない。
//
//  なぜ在るか:
//    Platform.Windows は Windows API と ManagedNativeWifi (NuGet) に依存するため
//    Linux のサンドボックスではコンパイルできない。しかし `MWC.Cli/Program.cs` は
//    DI 登録で 3 つの型名を参照するだけで、それが解決できないと **CLI 全体の
//    ハンドラ本体が束縛されず**、Core の API 誤用が一切検出できなくなる
//    (実験で確認済み: 存在しないメンバ名を入れてもエラーが出なかった)。
//
//    このスタブは「名前を解決させる」ためだけに在る。中身は一切実行しない。
//
//  ★ 信用してよい範囲は SystemCommandLine.Stub.cs と同じ:
//    ハンドラ本体の中身(Core API・BCL・null 許容)は本物に対して検査される。
//    Platform.Windows の**実装**は何も検査していない — スタブは空だからである。
// ─────────────────────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MWC.Core.Abstractions;
using MWC.Core.Models;

namespace MWC.Platform.Windows;

public sealed class WindowsWifiService : IWifiService
{
    public Task<IReadOnlyList<WifiAdapter>> GetAdaptersAsync(CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<IReadOnlyList<WifiNetwork>> ScanAsync(Guid adapterId, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<bool> RegisterProfileAsync(Guid adapterId, string profileXml, bool overwrite,
                                           CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<ConnectionResult> ConnectAsync(Guid adapterId, string profileName, string ssid,
                                               TimeSpan timeout, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<bool> DisconnectAsync(Guid adapterId, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<bool> DeleteProfileAsync(Guid adapterId, string profileName, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public Task<IReadOnlyList<string>> ListProfilesAsync(Guid adapterId, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");

    public IAsyncEnumerable<WifiEvent> SubscribeEventsAsync(CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");
}

public sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(byte[] plaintext) => throw new NotSupportedException("type-check stub");
    public byte[] Unprotect(byte[] ciphertext) => throw new NotSupportedException("type-check stub");
}

public sealed class HttpConnectivityChecker : IConnectivityChecker
{
    public Task<ConnectivityStatus> CheckAsync(Guid? adapterId = null, CancellationToken ct = default)
        => throw new NotSupportedException("type-check stub");
}
