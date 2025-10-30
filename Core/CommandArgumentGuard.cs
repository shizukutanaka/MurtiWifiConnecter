using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace MurtiWifiConnecter.Core
{
    internal static class CommandArgumentGuard
    {
        private static readonly Regex ControlCharacterPattern = new("[\x00-\x1F\x7F]", RegexOptions.Compiled);

        private static readonly char[] ForbiddenCharacters =
        {
            '&', '|', ';', '`', '>', '<', '\u202E', '\u200B', '\u200C', '\u200D', '\uFEFF'
        };

        private static readonly string[] ForbiddenSequences =
        {
            "&&", "||", ">>", "<<", "$(", "%(", "${", "%{", "\u202A", "\u202B",
            "..\\", "../", "\\..", "/..", "\\\\", "//", "://"
        };

        private static readonly string[] SqlInjectionPatterns =
        {
            "union select", "drop table", "insert into", "update set", "delete from",
            "exec(", "execute(", "sp_", "xp_", "openrowset", "opendatasource",
            "--", "/*", "*/", "xp_cmdshell", "bcp ", "bulk insert"
        };

        private static readonly string[] XssPatterns =
        {
            "<script", "</script>", "javascript:", "vbscript:", "onload=", "onerror=",
            "onclick=", "onmouseover=", "eval(", "expression(", "url(", "data:text/html"
        };

        private const int MaxArgumentLength = 512;
        private const int MaxCombinedArgumentLength = 4096;
        private const int MaxArgumentCount = 64;

        public static void EnsureSafeArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return;
            }

            if (args.Length > MaxArgumentCount)
            {
                throw new ArgumentException($"Too many arguments provided ({args.Length}). Maximum allowed is {MaxArgumentCount}.");
            }

            var combinedLength = 0;

            for (int i = 0; i < args.Length; i++)
            {
                var original = args[i];

                if (string.IsNullOrWhiteSpace(original))
                {
                    args[i] = string.Empty;
                    continue;
                }

                if (ControlCharacterPattern.IsMatch(original))
                {
                    throw new ArgumentException($"Argument {i} contains control characters");
                }

                if (ContainsInvisibleFormatCharacters(original))
                {
                    throw new ArgumentException($"Argument {i} contains unsupported zero-width or formatting characters");
                }

                if (ContainsSuspiciousWhitespace(original))
                {
                    throw new ArgumentException($"Argument {i} contains unsupported whitespace characters");
                }

                if (HasSuspiciousTabPadding(original))
                {
                    throw new ArgumentException($"Argument {i} contains disallowed tab padding at boundaries");
                }

                if (ContainsMixedWhitespaceRun(original))
                {
                    throw new ArgumentException($"Argument {i} contains mixed tab/space padding sequences");
                }

                if (ContainsSuspiciousEncodedSequence(original))
                {
                    throw new ArgumentException($"Argument '{Summarize(original)}' contains suspicious encoded sequences");
                }

                var normalized = NormalizeWhitespace(original);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    args[i] = string.Empty;
                    continue;
                }

                var sanitized = normalized.Trim();

                combinedLength += sanitized.Length;

                if (combinedLength > MaxCombinedArgumentLength)
                {
                    throw new ArgumentException($"Total argument length exceeds {MaxCombinedArgumentLength} characters");
                }

                if (sanitized.Length > MaxArgumentLength)
                {
                    throw new ArgumentException($"Argument {i} exceeds the maximum length of {MaxArgumentLength} characters");
                }

                if (ControlCharacterPattern.IsMatch(sanitized))
                {
                    throw new ArgumentException($"Argument {i} contains control characters");
                }

                if (ContainsSqlInjectionPattern(sanitized))
                {
                    throw new ArgumentException($"Argument '{Summarize(sanitized)}' contains potential SQL injection patterns");
                }

                if (ContainsXssPattern(sanitized))
                {
                    throw new ArgumentException($"Argument '{Summarize(sanitized)}' contains potential XSS patterns");
                }

                args[i] = sanitized;
            }
        }

        private static bool ContainsInvisibleFormatCharacters(string value)
        {
            return value.Any(c =>
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                return category == UnicodeCategory.Format || category == UnicodeCategory.Control && c != '\t';
            });
        }

        private static bool ContainsForbiddenCharacters(string value)
        {
            return value.IndexOfAny(ForbiddenCharacters) >= 0;
        }

        private static bool ContainsForbiddenSequence(string value)
        {
            foreach (var sequence in ForbiddenSequences)
            {
                if (value.Contains(sequence, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsMixedWhitespaceRun(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                if (char.IsWhiteSpace(value[i]))
                {
                    var hasSpace = value[i] == ' ';
                    var hasTab = value[i] == '\t';
                    int j = i + 1;

                    while (j < value.Length && char.IsWhiteSpace(value[j]))
                    {
                        if (value[j] == ' ')
                        {
                            hasSpace = true;
                        }
                        else if (value[j] == '\t')
                        {
                            hasTab = true;
                        }

                        if (hasSpace && hasTab)
                        {
                            return true;
                        }

                        j++;
                    }

                    i = j - 1;
                }
            }

            return false;
        }

        private static bool HasSuspiciousTabPadding(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            int leadingTabs = 0;
            int trailingTabs = 0;

            foreach (var ch in value)
            {
                if (ch == '\t')
                {
                    leadingTabs++;
                    if (leadingTabs >= 2)
                    {
                        return true;
                    }
                }
                else if (char.IsWhiteSpace(ch))
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            for (int i = value.Length - 1; i >= 0; i--)
            {
                var ch = value[i];
                if (ch == '\t')
                {
                    trailingTabs++;
                    if (trailingTabs >= 2)
                    {
                        return true;
                    }
                }
                else if (char.IsWhiteSpace(ch))
                {
                    continue;
                }
                else
                {
                    break;
                }
            }

            return false;
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var normalized = value
                .Select(ch => char.IsWhiteSpace(ch) ? ' ' : ch)
                .ToArray();

            var collapsed = new System.Text.StringBuilder(normalized.Length);
            var previousWasSpace = false;

            foreach (var ch in normalized)
            {
                if (ch == ' ')
                {
                    if (!previousWasSpace)
                    {
                        collapsed.Append(ch);
                        previousWasSpace = true;
                    }
                }
                else
                {
                    collapsed.Append(ch);
                    previousWasSpace = false;
                }
            }

            return collapsed.ToString();
        }

        private static bool ContainsSuspiciousWhitespace(string value)
        {
            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch) && ch != ' ' && ch != '\t')
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSuspiciousEncodedSequence(string value)
        {
            foreach (var pattern in SuspiciousEncodingPatterns)
            {
                if (value.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSqlInjectionPattern(string value)
        {
            var lower = value.ToLowerInvariant();
            foreach (var pattern in SqlInjectionPatterns)
            {
                if (lower.Contains(pattern))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsXssPattern(string value)
        {
            var lower = value.ToLowerInvariant();
            foreach (var pattern in XssPatterns)
            {
                if (lower.Contains(pattern))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
