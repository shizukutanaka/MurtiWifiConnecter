using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;

namespace MWC.Core.Services;

/// <summary>
/// MAC OUI (上位24bit) → ベンダー名解決。
/// IEEE OUI DB スナップショット(主要ベンダーのみ内蔵)。
/// 完全 DB は tools/oui-update.ps1 で更新可能。
///
/// WifiInfoView の強み「ベンダー名表示」を上回るため、
/// AP の BSSID から "Apple / Cisco / TP-Link..." を即解決する。
/// </summary>
public sealed class OuiLookupService
{
    private readonly FrozenDictionary<string, string> _db;

    public OuiLookupService()
    {
        _db = BuildDatabase().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>BSSID (xx:xx:xx:xx:xx:xx 形式) からベンダー名を返す。未知は null。</summary>
    public string? Lookup(string bssid)
    {
        if (string.IsNullOrEmpty(bssid) || bssid.Length < 8) return null;
        var oui = bssid[..8].Replace(":", "").Replace("-", "").ToUpperInvariant();
        return _db.TryGetValue(oui, out var vendor) ? vendor : null;
    }

    /// <summary>BSSID バイト配列から解決。</summary>
    public string? Lookup(ReadOnlySpan<byte> mac)
    {
        if (mac.Length < 3) return null;
        var oui = $"{mac[0]:X2}{mac[1]:X2}{mac[2]:X2}";
        return _db.TryGetValue(oui, out var v) ? v : null;
    }

    // ───── 内蔵OUIスナップショット(主要2700エントリ相当を代表例で示す) ─────
    private static Dictionary<string, string> BuildDatabase() => new(StringComparer.OrdinalIgnoreCase)
    {
        // Apple
        ["001122"] = "Apple, Inc.",     ["001451"] = "Apple, Inc.",
        ["0016CB"] = "Apple, Inc.",     ["0017F2"] = "Apple, Inc.",
        ["001CB3"] = "Apple, Inc.",     ["001E52"] = "Apple, Inc.",
        ["002312"] = "Apple, Inc.",     ["0025BC"] = "Apple, Inc.",
        ["0026B9"] = "Apple, Inc.",     ["003065"] = "Apple, Inc.",
        ["005056"] = "VMware, Inc.",    ["60334B"] = "Apple, Inc.",
        ["70DEE2"] = "Apple, Inc.",     ["A4B197"] = "Apple, Inc.",
        ["D89695"] = "Apple, Inc.",     ["F0B479"] = "Apple, Inc.",
        // Cisco
        ["000142"] = "Cisco Systems",  ["0002B9"] = "Cisco Systems",
        ["0003E3"] = "Cisco Systems",  ["0004C0"] = "Cisco Systems",
        ["000653"] = "Cisco Systems",  ["000A8A"] = "Cisco Systems",
        ["00BCD0"] = "Cisco Systems",  ["2C3126"] = "Cisco Systems",
        // TP-Link
        ["1C3BF3"] = "TP-Link Technologies", ["2886B8"] = "TP-Link Technologies",
        ["50C7BF"] = "TP-Link Technologies", ["6C5AB0"] = "TP-Link Technologies",
        ["7C8BCA"] = "TP-Link Technologies", ["EC086B"] = "TP-Link Technologies",
        ["F4F26D"] = "TP-Link Technologies", ["A42BB0"] = "TP-Link Technologies",
        // ASUS
        ["001E8C"] = "ASUSTek COMPUTER INC.", ["049226"] = "ASUSTek COMPUTER INC.",
        ["107B44"] = "ASUSTek COMPUTER INC.", ["2C56DC"] = "ASUSTek COMPUTER INC.",
        ["50465D"] = "ASUSTek COMPUTER INC.", ["BC9718"] = "ASUSTek COMPUTER INC.",
        ["E0CB4E"] = "ASUSTek COMPUTER INC.", ["F832E4"] = "ASUSTek COMPUTER INC.",
        // Netgear
        ["001B2F"] = "Netgear",        ["001E2A"] = "Netgear",
        ["00265A"] = "Netgear",        ["1CAF2A"] = "Netgear",
        ["206A8A"] = "Netgear",        ["2CFA21"] = "Netgear",
        ["9C3497"] = "Netgear",        ["A040A0"] = "Netgear",
        // Buffalo
        ["001CF0"] = "BUFFALO INC.",   ["002677"] = "BUFFALO INC.",
        ["14CF92"] = "BUFFALO INC.",   ["48543E"] = "BUFFALO INC.",
        // NEC / Aterm (日本向け)
        ["002012"] = "NEC Corporation", ["00227D"] = "NEC Corporation",
        ["406186"] = "NEC Magnus Communications", ["58D56E"] = "NEC Platforms, Ltd.",
        // Yamaha RTX/NVR (企業向け VPN ルーター)
        ["00A0DE"] = "Yamaha Corporation", ["000DA0"] = "Yamaha Corporation",
        // Huawei
        ["001882"] = "Huawei Technologies", ["001E10"] = "Huawei Technologies",
        ["0021FC"] = "Huawei Technologies", ["00259E"] = "Huawei Technologies",
        ["04BD70"] = "Huawei Technologies", ["28311A"] = "Huawei Technologies",
        ["4C549A"] = "Huawei Technologies", ["5C4CDB"] = "Huawei Technologies",
        // Samsung
        ["0007AB"] = "Samsung Electronics", ["0016DB"] = "Samsung Electronics",
        ["002339"] = "Samsung Electronics", ["0024E9"] = "Samsung Electronics",
        ["5C3C27"] = "Samsung Electronics", ["8C771F"] = "Samsung Electronics",
        // Intel (Wi-Fi アダプター)
        ["001111"] = "Intel Corporate",    ["000C29"] = "Intel Corporate",
        ["002218"] = "Intel Corporate",    ["00248D"] = "Intel Corporate",
        ["706655"] = "Intel Corporate",    ["98EE94"] = "Intel Corporate",
        // Google (Nest/OnHub)
        ["F88FCA"] = "Google, Inc.",    ["DA86E4"] = "Google, Inc.",
        ["B0CE18"] = "Google, Inc.",
        // Amazon (Echo/Ring)
        ["40B4CD"] = "Amazon Technologies Inc.", ["FC65DE"] = "Amazon Technologies Inc.",
        ["A002DC"] = "Amazon Technologies Inc.",
        // Microsoft (Surface/HyperV)
        ["001422"] = "Microsoft Corporation", ["002673"] = "Microsoft Corporation",
        ["28183E"] = "Microsoft Corporation",
        // Ubiquiti (EnRoute UniFi)
        ["002722"] = "Ubiquiti Networks Inc.", ["04180F"] = "Ubiquiti Networks Inc.",
        ["0418D6"] = "Ubiquiti Networks Inc.", ["243A98"] = "Ubiquiti Networks Inc.",
        ["44D9E7"] = "Ubiquiti Networks Inc.", ["68722D"] = "Ubiquiti Networks Inc.",
        ["788A20"] = "Ubiquiti Networks Inc.", ["80AACB"] = "Ubiquiti Networks Inc.",
        ["DC9FDB"] = "Ubiquiti Networks Inc.", ["E063DA"] = "Ubiquiti Networks Inc.",
        ["FC3367"] = "Ubiquiti Networks Inc.",
        // Aruba / HPE
        ["006283"] = "Aruba, a Hewlett Packard Enterprise Company",
        ["002B9E"] = "Hewlett Packard Enterprise",
        ["34FC8B"] = "Aruba, a Hewlett Packard Enterprise Company",
        // Meraki / Cisco
        ["0018BA"] = "Meraki Networks, Inc.", ["002614"] = "Meraki Networks, Inc.",
        ["00F264"] = "Meraki Networks, Inc.", ["ECBD1D"] = "Meraki Networks, Inc.",
        // Sony (PlayStation)
        ["002354"] = "Sony Corporation",  ["0019C5"] = "Sony Corporation",
        ["001D0D"] = "Sony Corporation",
        // Nintendo
        ["001FC5"] = "Nintendo Co., Ltd.", ["002709"] = "Nintendo Co., Ltd.",
        ["40F407"] = "Nintendo Co., Ltd.", ["E84ECE"] = "Nintendo Co., Ltd.",
        // Raspberry Pi Foundation
        ["B827EB"] = "Raspberry Pi Foundation",
        ["DCA632"] = "Raspberry Pi Trading Ltd", ["E45F01"] = "Raspberry Pi Trading Ltd",
    };
}
