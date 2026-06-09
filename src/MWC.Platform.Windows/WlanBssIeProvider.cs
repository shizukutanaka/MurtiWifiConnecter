using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MWC.Core.Abstractions;

namespace MWC.Platform.Windows;

/// <summary>
/// <see cref="IBeaconIeProvider"/> の Windows 実装。
/// Win32 <c>WlanGetNetworkBssList</c> で各 BSS の生 802.11 IE と TSF を取得する。
///
/// ManagedNativeWifi 3.0.2 は <c>WLAN_BSS_ENTRY</c> の <c>ulIeOffset</c>/<c>ulIeSize</c>
/// が指す生 IE を公開しないため、ここだけ P/Invoke で直接叩く。
/// netsh / WMI は使わない (WlanAPI 直叩き、CLAUDE.md 準拠)。
///
/// ⚠ 本クラスはネイティブ構造体マーシャリングを含み、**実機 Windows での検証が必須**。
/// 既定の DI には登録せず (WindowsWifiService は NullBeaconIeProvider にフォールバック)、
/// 明示的に登録・検証してから有効化すること。
/// 全読み出しは確保バッファ長 (dwTotalSize) で境界検査し、不正オフセットでも
/// AccessViolation を起こさず空を返す防衛的設計。
/// </summary>
public sealed class WlanBssIeProvider : IBeaconIeProvider
{
    private readonly ILogger<WlanBssIeProvider> _log;

    public WlanBssIeProvider(ILogger<WlanBssIeProvider> log) => _log = log;

    public IReadOnlyDictionary<string, RawBeaconData> GetRawBeacons(Guid adapterId)
    {
        var result = new Dictionary<string, RawBeaconData>(StringComparer.OrdinalIgnoreCase);
        IntPtr clientHandle = IntPtr.Zero;
        IntPtr bssListPtr = IntPtr.Zero;
        try
        {
            if (WlanOpenHandle(WLAN_CLIENT_VERSION, IntPtr.Zero, out _, out clientHandle) != 0)
                return result;

            // pDot11Ssid = NULL → 全 BSS を列挙。bSecurityEnabled は無視される。
            int ret = WlanGetNetworkBssList(
                clientHandle, ref adapterId,
                IntPtr.Zero, DOT11_BSS_TYPE_ANY, false,
                IntPtr.Zero, out bssListPtr);
            if (ret != 0 || bssListPtr == IntPtr.Zero)
                return result;

            ParseBssList(bssListPtr, result);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "WlanGetNetworkBssList failed");
        }
        finally
        {
            if (bssListPtr != IntPtr.Zero) WlanFreeMemory(bssListPtr);
            if (clientHandle != IntPtr.Zero) WlanCloseHandle(clientHandle, IntPtr.Zero);
        }
        return result;
    }

    private void ParseBssList(IntPtr listPtr, Dictionary<string, RawBeaconData> result)
    {
        // WLAN_BSS_LIST: DWORD dwTotalSize; DWORD dwNumberOfItems; WLAN_BSS_ENTRY[1]
        long totalSize = (uint)Marshal.ReadInt32(listPtr, 0);
        int  numItems  = Marshal.ReadInt32(listPtr, 4);
        if (numItems <= 0 || numItems > 4096) return;   // 妥当性チェック

        int entrySize = Marshal.SizeOf<WLAN_BSS_ENTRY>();
        const int entriesBase = 8;   // 2 × DWORD ヘッダ後

        for (int i = 0; i < numItems; i++)
        {
            long entryOffset = entriesBase + (long)i * entrySize;
            // 構造体本体が確保域を超える → 中断 (境界保護)
            if (entryOffset + entrySize > totalSize) break;

            IntPtr entryPtr = IntPtr.Add(listPtr, (int)entryOffset);
            var entry = Marshal.PtrToStructure<WLAN_BSS_ENTRY>(entryPtr);

            string bssid = FormatMac(entry.dot11Bssid);

            // IE 領域の境界検査: entryOffset + ulIeOffset + ulIeSize <= totalSize
            long ieAbs = entryOffset + entry.ulIeOffset;
            long ieSize = entry.ulIeSize;
            byte[] ies = Array.Empty<byte>();
            if (ieSize > 0 && ieSize <= 4096 && ieAbs + ieSize <= totalSize)
            {
                ies = new byte[ieSize];
                Marshal.Copy(IntPtr.Add(listPtr, (int)ieAbs), ies, 0, (int)ieSize);
            }

            result[bssid] = new RawBeaconData(
                InformationElements: ies,
                TsfTimestamp:        entry.ullTimestamp,
                BeaconIntervalTu:    entry.usBeaconPeriod);
        }
    }

    private static string FormatMac(MacAddress6 m)
        => $"{m.B0:x2}:{m.B1:x2}:{m.B2:x2}:{m.B3:x2}:{m.B4:x2}:{m.B5:x2}";

    // ── P/Invoke ─────────────────────────────────────────────────────
    private const uint WLAN_CLIENT_VERSION = 2;
    private const int  DOT11_BSS_TYPE_ANY  = 3;

    [DllImport("wlanapi.dll")]
    private static extern int WlanOpenHandle(
        uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport("wlanapi.dll")]
    private static extern int WlanGetNetworkBssList(
        IntPtr hClientHandle, ref Guid pInterfaceGuid,
        IntPtr pDot11Ssid, int dot11BssType, [MarshalAs(UnmanagedType.Bool)] bool bSecurityEnabled,
        IntPtr pReserved, out IntPtr ppWlanBssList);

    // ── ネイティブ構造体 (WLAN_BSS_ENTRY のレイアウト厳密一致) ──────────
    [StructLayout(LayoutKind.Sequential)]
    private struct MacAddress6
    {
        public byte B0, B1, B2, B3, B4, B5;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Dot11Ssid
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WlanRateSet
    {
        public uint uRateSetLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
        public ushort[] usRateSet;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_BSS_ENTRY
    {
        public Dot11Ssid   dot11Ssid;
        public uint        uPhyId;
        public MacAddress6 dot11Bssid;
        public int         dot11BssType;
        public int         dot11BssPhyType;
        public int         lRssi;
        public uint        uLinkQuality;
        [MarshalAs(UnmanagedType.U1)]
        public bool        bInRegDomain;
        public ushort      usBeaconPeriod;
        public ulong       ullTimestamp;
        public ulong       ullHostTimestamp;
        public ushort      usCapabilityInformation;
        public uint        ulChCenterFrequency;
        public WlanRateSet wlanRateSet;
        public uint        ulIeOffset;
        public uint        ulIeSize;
    }
}
