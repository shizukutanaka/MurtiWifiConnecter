using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Shell;
using Microsoft.Extensions.Logging;
using MWC.Core.Models;

namespace MWC.App.Services;

/// <summary>
/// Windows タスクバーの JumpList にネットワーク一覧を登録。
/// Apple の Dock クリック = JumpList 右クリックに相当。
///
/// タスクバー右クリック → 最近接続 / よく使う をクイック接続に。
/// `mwc connect "SSID"` を ShellTask として登録する。
/// </summary>
public sealed class JumpListService
{
    private readonly ILogger<JumpListService> _log;

    public JumpListService(ILogger<JumpListService> log) => _log = log;

    /// <summary>
    /// スキャン結果と接続履歴からジャンプリストを更新する。
    /// 最大 10 ネットワーク。
    /// </summary>
    public void Update(IReadOnlyList<WifiNetwork> networks, IReadOnlyList<string> recentSsids)
    {
        try
        {
            var jl = new JumpList { ShowRecentCategory = false, ShowFrequentCategory = false };

            // ── 接続中 ──
            var connected = Array.Find(networks.ToArray(), n => n.IsConnected);
            if (connected is not null)
            {
                jl.JumpItems.Add(new JumpTask
                {
                    Title            = MWC.App.Resources.L.Format("Jump_ConnectedSsid", connected.Ssid),
                    Description      = MWC.App.Resources.L.Get("Jump_CurrentNet"),
                    ApplicationPath  = GetCliPath(),
                    Arguments        = $"scan",
                    CustomCategory   = MWC.App.Resources.L.Get("Jump_CurrentCategory")
                });
            }

            // ── 最近使った (上位 5) ──
            int recent = 0;
            foreach (var ssid in recentSsids)
            {
                if (recent++ >= 5) break;
                jl.JumpItems.Add(new JumpTask
                {
                    Title           = ssid,
                    Description     = MWC.App.Resources.L.JumpConnectDescription(ssid),
                    ApplicationPath = GetCliPath(),
                    Arguments       = $"connect {EscapeArg(ssid)}",
                    CustomCategory  = MWC.App.Resources.L.Get("Jump_RecentCategory")
                });
            }

            // ── 再スキャン ──
            jl.JumpItems.Add(new JumpTask
            {
                Title           = MWC.App.Resources.L.Get("Jump_Rescan"),
                Description     = MWC.App.Resources.L.Get("Jump_RescanDesc"),
                ApplicationPath = GetCliPath(),
                Arguments       = "scan",
                CustomCategory  = MWC.App.Resources.L.Get("Jump_ActionCategory")
            });

            JumpList.SetJumpList(System.Windows.Application.Current, jl);
            jl.Apply();
            _log.LogDebug("JumpList updated: {count} items", jl.JumpItems.Count);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "JumpList update failed");
        }
    }

    private static string GetCliPath()
    {
        var dir = AppContext.BaseDirectory;
        return System.IO.Path.Combine(dir, "mwc.exe");
    }

    // Windows C-runtime quoting rules (per MSDN "Parsing C Command-Line Arguments"):
    // backslashes are literal unless they precede a quote; quotes inside must be \"-escaped.
    private static string EscapeArg(string s)
    {
        var sb = new System.Text.StringBuilder("\"");
        int pending = 0;
        foreach (var c in s)
        {
            if (c == '\\') { pending++; continue; }
            if (c == '"')
            {
                sb.Append('\\', pending * 2 + 1);
                sb.Append('"');
                pending = 0;
                continue;
            }
            sb.Append('\\', pending);
            sb.Append(c);
            pending = 0;
        }
        sb.Append('\\', pending * 2); // trailing backslashes before closing quote
        sb.Append('"');
        return sb.ToString();
    }
}
