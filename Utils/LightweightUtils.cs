using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace MurtiWifiConnecter.Utils
{
    /// <summary>
    /// 軽量で高速なユーティリティクラス
    /// </summary>
    public static class LightweightUtils
    {
        /// <summary>
        /// 高速な文字列比較（大文字小文字を無視）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool EqualsIgnoreCase(this string str, string other)
        {
            return string.Equals(str, other, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 高速な文字列検索（大文字小文字を無視）
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ContainsIgnoreCase(this string str, string value)
        {
            return str?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false;
        }

        /// <summary>
        /// 安全なファイル名生成
        /// </summary>
        public static string ToSafeFileName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "unnamed";

            var invalidChars = Path.GetInvalidFileNameChars();
            var result = new char[input.Length];
            var length = 0;

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (Array.IndexOf(invalidChars, c) == -1)
                {
                    result[length++] = c;
                }
                else
                {
                    result[length++] = '_';
                }
            }

            return new string(result, 0, length);
        }

        /// <summary>
        /// メモリ効率的な文字列分割
        /// </summary>
        public static void SplitInto(string input, char separator, List<string> output)
        {
            output.Clear();
            if (string.IsNullOrEmpty(input))
                return;

            var span = input.AsSpan();
            int start = 0;

            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == separator)
                {
                    if (i > start)
                        output.Add(span.Slice(start, i - start).ToString());
                    start = i + 1;
                }
            }

            if (start < span.Length)
                output.Add(span.Slice(start).ToString());
        }

        /// <summary>
        /// 高速なバイト配列比較
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;

            if (a == null || b == null || a.Length != b.Length)
                return false;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 安全な数値変換
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ToIntSafe(string value, int defaultValue = 0)
        {
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// 安全なbool変換
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ToBoolSafe(string value, bool defaultValue = false)
        {
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            return value.EqualsIgnoreCase("true") || 
                   value.EqualsIgnoreCase("yes") || 
                   value.EqualsIgnoreCase("1") ||
                   value.EqualsIgnoreCase("on");
        }

        /// <summary>
        /// タイムスタンプ文字列生成
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string GetTimestamp() => DateTime.Now.ToString("yyyyMMdd_HHmmss");

        /// <summary>
        /// ファイルサイズの可読表示
        /// </summary>
        public static string FormatFileSize(long bytes)
        {
            const long kb = 1024;
            const long mb = kb * 1024;
            const long gb = mb * 1024;

            return bytes switch
            {
                < kb => $"{bytes} B",
                < mb => $"{bytes / (double)kb:F1} KB",
                < gb => $"{bytes / (double)mb:F1} MB",
                _ => $"{bytes / (double)gb:F1} GB"
            };
        }

        /// <summary>
        /// 高速なハッシュコード計算
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetFastHashCode(string str)
        {
            if (string.IsNullOrEmpty(str))
                return 0;

            unchecked
            {
                int hash = 17;
                for (int i = 0; i < str.Length; i++)
                {
                    hash = hash * 31 + str[i];
                }
                return hash;
            }
        }
    }

    /// <summary>
    /// 軽量なタイマーユーティリティ
    /// </summary>
    public static class QuickTimer
    {
        private static readonly Dictionary<string, DateTime> _startTimes = new();

        /// <summary>
        /// タイマー開始
        /// </summary>
        public static void Start(string name)
        {
            _startTimes[name] = DateTime.Now;
        }

        /// <summary>
        /// タイマー停止と経過時間取得
        /// </summary>
        public static TimeSpan Stop(string name)
        {
            if (_startTimes.TryGetValue(name, out var startTime))
            {
                _startTimes.Remove(name);
                return DateTime.Now - startTime;
            }
            return TimeSpan.Zero;
        }

        /// <summary>
        /// タイマーリセット
        /// </summary>
        public static void Reset() => _startTimes.Clear();
    }
}