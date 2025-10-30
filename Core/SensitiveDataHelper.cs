using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MurtiWifiConnecter.Core
{
    internal static class SensitiveDataHelper
    {
        private static readonly string[] SensitiveKeywords =
        {
            "password",
            "passcode",
            "pwd",
            "secret",
            "token",
            "key",
            "apikey",
            "api_key",
            "clientsecret",
            "credential",
            "auth",
            "bearer",
            "session",
            "private",
            "certificate"
        };

        internal static readonly string[] SensitiveKeywordSignatures = Array.ConvertAll(SensitiveKeywords, NormalizeToken);

        internal const string Redacted = "[redacted]";

        internal static string RedactValue(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(empty)";
            }

            return IsSensitiveKey(key) ? Redacted : value;
        }

        internal static bool IsSensitiveKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var normalized = NormalizeToken(key);
            if (string.IsNullOrEmpty(normalized))
            {
                return false;
            }

            return SensitiveKeywordSignatures.Any(normalized.Contains);
        }

        internal static string[] RedactArguments(int[] sensitiveArgumentIndexes, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return Array.Empty<string>();
            }

            if (sensitiveArgumentIndexes == null || sensitiveArgumentIndexes.Length == 0)
            {
                return args.ToArray();
            }

            var sanitized = args.ToArray();
            foreach (var index in sensitiveArgumentIndexes)
            {
                if (index >= 0 && index < sanitized.Length && !string.IsNullOrEmpty(sanitized[index]))
                {
                    sanitized[index] = Redacted;
                }
            }

            return sanitized;
        }

        internal static void RedactCommandTokens(IList<string> tokens)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return;
            }

            var command = tokens[0].ToLowerInvariant();

            if (command == "config" && tokens.Count >= 4 && tokens[1].Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 3; i < tokens.Count; i++)
                {
                    tokens[i] = Redacted;
                }
            }
            else if ((command == "connect" || command == "c" || command == "quick" || command == "q") && tokens.Count >= 3)
            {
                tokens[2] = Redacted;
            }
        }

        internal static string NormalizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(char.ToLowerInvariant(ch));
                }
            }

            return builder.ToString();
        }
    }
}
