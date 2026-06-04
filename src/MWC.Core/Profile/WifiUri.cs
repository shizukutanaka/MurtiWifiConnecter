using System;
using System.Text;
using MWC.Core.Models;

namespace MWC.Core.Profile;

/// <summary>
/// Wi-Fi Alliance "WIFI:" URI スキーム生成・パース。
/// 仕様: https://www.wi-fi.org/file/wpa3-specification-v3-0
/// 形式: WIFI:T:&lt;auth&gt;;S:&lt;ssid&gt;;P:&lt;password&gt;;H:&lt;hidden&gt;;;
/// 特殊文字 \ ; , : " はバックスラッシュエスケープ
/// </summary>
public static class WifiUri
{
    /// <summary>WifiProfileSpec を WIFI: URI スキーム文字列に変換する (QRコード用)。</summary>
    public static string Build(WifiProfileSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        string t = spec.Auth switch
        {
            AuthMethod.Open => "nopass",
            AuthMethod.OWE  => "OWE",
            AuthMethod.WEP  => "WEP",
            AuthMethod.WPA3SAE or AuthMethod.WPA3Transition => "SAE",
            _ => "WPA"
        };

        var sb = new StringBuilder("WIFI:");
        sb.Append("T:").Append(t).Append(';');
        sb.Append("S:").Append(Escape(spec.Ssid)).Append(';');
        if (!string.IsNullOrEmpty(spec.Passphrase))
            sb.Append("P:").Append(Escape(spec.Passphrase)).Append(';');
        if (spec.NonBroadcast)
            sb.Append("H:true;");
        sb.Append(';');
        return sb.ToString();
    }

    /// <summary>
    /// WIFI: URI スキームを解析する防御的ラッパー。
    /// どんな不正入力でも例外を投げず、解析できなければ null を返す。
    /// </summary>
    public static WifiProfileSpec? TryParse(string? uri)
    {
        if (uri is null) return null;
        try { return Parse(uri); }
        catch { return null; }
    }

    /// <summary>WIFI: URI スキームを解析して WifiProfileSpec を返す。不正な形式なら null。</summary>
    public static WifiProfileSpec? Parse(string uri)
    {
        if (string.IsNullOrEmpty(uri) || !uri.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
            return null;

        string body = uri[5..].TrimEnd(';');
        string? type = null, ssid = null, password = null;
        bool hidden = false;

        int i = 0;
        while (i < body.Length)
        {
            // key
            if (i + 1 >= body.Length || body[i + 1] != ':') return null;
            char key = body[i];
            i += 2;
            // value (until unescaped ;)
            var sb = new StringBuilder();
            while (i < body.Length && body[i] != ';')
            {
                if (body[i] == '\\' && i + 1 < body.Length) { sb.Append(body[i + 1]); i += 2; }
                else { sb.Append(body[i]); i++; }
            }
            if (i < body.Length && body[i] == ';') i++;

            string val = sb.ToString();
            switch (char.ToUpperInvariant(key))
            {
                case 'T': type = val; break;
                case 'S': ssid = val; break;
                case 'P': password = val; break;
                case 'H': hidden = string.Equals(val, "true", StringComparison.OrdinalIgnoreCase); break;
            }
        }

        if (string.IsNullOrEmpty(ssid)) return null;

        AuthMethod auth = (type ?? "").ToUpperInvariant() switch
        {
            "" or "NOPASS" => AuthMethod.Open,
            "WEP"          => AuthMethod.WEP,
            "WPA"          => AuthMethod.WPA2PSK,
            "WPA2"         => AuthMethod.WPA2PSK,
            "SAE"          => AuthMethod.WPA3SAE,
            "WPA3"         => AuthMethod.WPA3SAE,
            "OWE"          => AuthMethod.OWE,
            _              => AuthMethod.WPA2PSK
        };

        return new WifiProfileSpec
        {
            Ssid = ssid,
            Auth = auth,
            Passphrase = password,
            NonBroadcast = hidden
        };
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is '\\' or ';' or ',' or ':' or '"') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
