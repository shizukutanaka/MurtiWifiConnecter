using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace MWC.Core.Services;

/// <summary>
/// Group Policy / Intune (Microsoft Endpoint Manager) 設定プロバイダ。
///
/// 企業環境では IT管理者が MDM Policy (HKLM\SOFTWARE\Policies\MWC) または
/// Group Policy (HKLM\SOFTWARE\Microsoft\Group Policy\...) で
/// MWC の動作を制約・設定できる。
///
/// 設定優先順位:
///   1. HKLM\SOFTWARE\Policies\MWC   (GP / MDM 管理設定、最優先)
///   2. HKCU\SOFTWARE\MWC            (ユーザー設定)
///   3. アプリデフォルト
///
/// Intune カスタム OMA-URI:
///   ./Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/MWC/DisableManualConnect
/// </summary>
public sealed class GroupPolicyProvider
{
    private const string GpKeyPath   = @"SOFTWARE\Policies\MWC";
    private const string UserKeyPath  = @"SOFTWARE\MWC";

    // ── シングルトン ──────────────────────────────────────────────────
    private static readonly Lazy<GroupPolicyProvider> _lazy =
        new(() => new GroupPolicyProvider());
    public  static GroupPolicyProvider  Instance => _lazy.Value;

    // ── ポリシー値キャッシュ ──────────────────────────────────────────
    private readonly Dictionary<string, object?> _cache = new();

    // ── ポリシー定義 ─────────────────────────────────────────────────

    /// <summary>ユーザーが手動で Wi-Fi 接続先を変更することを禁止する</summary>
    public bool DisableManualConnect
        => GetDword(nameof(DisableManualConnect)) == 1;

    /// <summary>スキャン結果のエクスポートを無効化する</summary>
    public bool DisableExport
        => GetDword(nameof(DisableExport)) == 1;

    /// <summary>設定画面へのアクセスを制限する</summary>
    public bool DisableSettings
        => GetDword(nameof(DisableSettings)) == 1;

    /// <summary>QRコードの表示/スキャンを無効化する</summary>
    public bool DisableQrCode
        => GetDword(nameof(DisableQrCode)) == 1;

    /// <summary>接続を許可する SSID ホワイトリスト (カンマ区切り)</summary>
    public IReadOnlyList<string> AllowedSsids
        => ParseSsidList(GetString(nameof(AllowedSsids)));

    /// <summary>接続を禁止する SSID ブラックリスト (カンマ区切り)</summary>
    public IReadOnlyList<string> BlockedSsids
        => ParseSsidList(GetString(nameof(BlockedSsids)));

    /// <summary>認証方式の最低要件(0=なし, 1=WPA2以上, 2=WPA3以上)</summary>
    public int MinAuthLevel
        => GetDword(nameof(MinAuthLevel)) ?? 0;

    /// <summary>自動再接続を強制する</summary>
    public bool ForceAutoReconnect
        => GetDword(nameof(ForceAutoReconnect)) == 1;

    /// <summary>Intune 管理下にあるか(ポリシーが1件以上設定されているか)</summary>
    public bool IsManagedDevice => HasGpKey;

    /// <summary>全ポリシー設定をリスト形式で取得(管理コンソール向け)</summary>
    public IReadOnlyList<PolicyEntry> GetAllPolicies()
    {
        var result = new List<PolicyEntry>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(GpKeyPath, false);
            if (key is null) return result;

            foreach (var name in key.GetValueNames())
            {
                var val = key.GetValue(name);
                result.Add(new PolicyEntry(name, val?.ToString() ?? "", key.GetValueKind(name).ToString()));
            }
        }
        catch { }
        return result;
    }

    /// <summary>
    /// SSID がポリシーで許可されているかチェック。
    /// AllowedSsids が空なら全許可、BlockedSsids にあれば拒否。
    /// </summary>
    public bool IsSsidAllowed(string ssid)
    {
        if (BlockedSsids.Contains(ssid, StringComparer.OrdinalIgnoreCase))
            return false;
        if (AllowedSsids.Count > 0)
            return AllowedSsids.Contains(ssid, StringComparer.OrdinalIgnoreCase);
        return true;
    }

    // ── Private ─────────────────────────────────────────────────────

    private bool HasGpKey
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(GpKeyPath, false);
                return key is not null;
            }
            catch { return false; }
        }
    }

    private int? GetDword(string name)
    {
        if (_cache.TryGetValue(name, out var cached)) return cached as int?;
        var val = ReadValue(name);
        _cache[name] = val;
        return val as int?;
    }

    private string? GetString(string name)
    {
        if (_cache.TryGetValue("str_" + name, out var cached)) return cached as string;
        var val = ReadStringValue(name);
        _cache["str_" + name] = val;
        return val;
    }

    private static object? ReadValue(string name)
    {
        try
        {
            // GP キーが優先
            using var gpKey = Registry.LocalMachine.OpenSubKey(GpKeyPath, false);
            if (gpKey?.GetValue(name) is object gpVal) return (int)gpVal;
        }
        catch { }
        return null;
    }

    private static string? ReadStringValue(string name)
    {
        try
        {
            using var gpKey = Registry.LocalMachine.OpenSubKey(GpKeyPath, false);
            if (gpKey?.GetValue(name) is string gpVal) return gpVal;
        }
        catch { }
        return null;
    }

    private static IReadOnlyList<string> ParseSsidList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>キャッシュを無効化する(Group Policy 更新後に呼ぶ)</summary>
    public void Invalidate() => _cache.Clear();
}

/// <summary>Group Policy エントリ</summary>
public sealed record PolicyEntry(string Name, string Value, string Type);
