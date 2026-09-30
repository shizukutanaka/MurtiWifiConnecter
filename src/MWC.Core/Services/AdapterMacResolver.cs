using System;
using System.Collections.Generic;

namespace MWC.Core.Services;

/// <summary>
/// WLAN アダプター GUID と OS のネットワークインターフェース一覧を突き合わせ、
/// そのアダプターの MAC を "AA:BB:CC:DD:EE:FF" 形式で返す純関数。
/// Windows では <c>NetworkInterface.Id</c> が "{GUID}" 形式で WLAN インターフェース GUID と
/// 一致する見込みだが**実機未検証**。一致しなければ null(呼び出し側は従来どおり
/// <c>--mac</c> 指定を求める。退行しない)。
/// </summary>
public static class AdapterMacResolver
{
    public static string? Resolve(Guid adapterId, IEnumerable<(string Id, byte[] Address)> nics)
    {
        foreach (var (id, addr) in nics)
        {
            if (addr is null || addr.Length != 6) continue;
            if (!Guid.TryParse(id, out var g) || g != adapterId) continue;
            return string.Join(":", Array.ConvertAll(addr, b => b.ToString("X2")));
        }
        return null;
    }
}
