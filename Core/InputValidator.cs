using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MurtiWifiConnecter.Core
{
    public static class InputValidator
    {
        private static readonly Regex ControlChars = new("[\x00-\x1F\x7F]", RegexOptions.Compiled);
        private static readonly Regex SsidAllowedPattern = new(@"^[A-Za-z0-9 _\-\.#!$%&'()*+,/:;<=>?@\[\\\]^`{|}~""]+$", RegexOptions.Compiled);
        private static readonly Regex PasswordAllowedPattern = new("^[\x20-\x7E]+$", RegexOptions.Compiled);
        private static readonly char[] InvalidFileNameCharacters = Path.GetInvalidFileNameChars();
        private static readonly string[] ReservedDeviceNames =
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static string EnsureValidSsid(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                throw new ArgumentException("SSID cannot be empty");

            var trimmed = ssid.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 32)
                throw new ArgumentException("SSID length must be between 1 and 32 characters");

            if (ControlChars.IsMatch(trimmed))
                throw new ArgumentException("SSID cannot contain control characters");

            if (!SsidAllowedPattern.IsMatch(trimmed))
                throw new ArgumentException("SSID contains unsupported characters");

            // SECURITY IMPROVEMENT: Additional injection protection
            if (ContainsInjectionPatterns(trimmed))
                throw new ArgumentException("SSID contains potentially dangerous patterns");

            // SECURITY: Block null bytes and other binary data
            if (trimmed.Contains('\0'))
                throw new ArgumentException("SSID cannot contain null bytes");

            // SECURITY: Prevent excessively long sequences of the same character
            if (HasSuspiciousRepetition(trimmed))
                throw new ArgumentException("SSID contains suspicious character patterns");

            return trimmed;
        }

        // SECURITY: Detect suspicious character repetition
        private static bool HasSuspiciousRepetition(string input)
        {
            if (string.IsNullOrEmpty(input) || input.Length < 10)
                return false;

            int maxRepetition = 0;
            char prevChar = '\0';
            int currentRepetition = 1;

            foreach (var c in input)
            {
                if (c == prevChar)
                {
                    currentRepetition++;
                    if (currentRepetition > maxRepetition)
                        maxRepetition = currentRepetition;
                }
                else
                {
                    currentRepetition = 1;
                    prevChar = c;
                }
            }

            // Flag if more than 70% of the input is the same character
            return maxRepetition > (input.Length * 0.7);
        }

        public static string EnsureValidPassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return null;

            var trimmed = password.Trim();
            if (trimmed.Length < 5 || trimmed.Length > 63)
                throw new ArgumentException("Password length must be between 5 and 63 characters");

            if (!PasswordAllowedPattern.IsMatch(trimmed))
                throw new ArgumentException("Password contains unsupported characters");

            return trimmed;
        }

        public static string EnsureSafeXmlValue(string value)
        {
            if (value == null)
                return string.Empty;

            return System.Security.SecurityElement.Escape(value) ?? string.Empty;
        }

        public static string QuoteForNetsh(string value)
        {
            if (value == null)
                return "\"\"";

            var escaped = value.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        }

        public static string EnsureSafeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty");

            var trimmed = path.Trim().Trim('"');

            foreach (var invalidChar in Path.GetInvalidPathChars())
            {
                if (trimmed.Contains(invalidChar))
                    throw new ArgumentException("Path contains invalid characters");
            }

            var fullPath = Path.GetFullPath(trimmed);

            var fileName = Path.GetFileName(fullPath);
            if (!string.IsNullOrEmpty(fileName) && IsReservedDeviceName(Path.GetFileNameWithoutExtension(fileName)))
                throw new ArgumentException("Path targets a reserved device name");

            return fullPath;
        }

        public static string SanitizeFileName(string fileName, string defaultExtension = ".json")
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("File name cannot be empty");

            var trimmed = fileName.Trim().Trim('"');
            var nameOnly = Path.GetFileName(trimmed);

            if (string.IsNullOrWhiteSpace(nameOnly))
                throw new ArgumentException("File name cannot be empty");

            var sanitized = new string(nameOnly.Select(c => InvalidFileNameCharacters.Contains(c) ? '_' : c).ToArray());

            if (string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(sanitized)))
                throw new ArgumentException("File name must contain alphanumeric characters");

            if (string.IsNullOrWhiteSpace(Path.GetExtension(sanitized)) && !string.IsNullOrWhiteSpace(defaultExtension))
            {
                sanitized += defaultExtension;
            }

            if (IsReservedDeviceName(Path.GetFileNameWithoutExtension(sanitized)))
                throw new ArgumentException("File name cannot use reserved device names");

            return sanitized;
        }

        private static bool IsReservedDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var normalized = name.Trim().TrimEnd('.').ToUpperInvariant();
            return ReservedDeviceNames.Contains(normalized);
        }

        // SECURITY IMPROVEMENT: Advanced injection pattern detection
        private static bool ContainsInjectionPatterns(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;
            var suspiciousPatterns = new[]
            {
                // Command injection patterns
                "&&", "||", "|", ";", "`", "$(", "${",
                // Path traversal patterns
                "../", "..\\",
                // Script injection patterns
                "<script", "javascript:", "vbscript:",
                // Encoding attacks
                "%00", "%2e%2e", "%c0%af",
                // Network injection
                "file://", "ftp://",
                // PowerShell injection
                "powershell", "cmd.exe", "cmd /c"
            };

            var normalizedInput = input.ToLowerInvariant();
            return suspiciousPatterns.Any(pattern => normalizedInput.Contains(pattern.ToLowerInvariant()));
        }

        // ENTERPRISE IMPROVEMENT: Enhanced path validation with UNC path protection
        public static string EnsureSecureFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty");

            var trimmed = path.Trim().Trim('"');

            // SECURITY: Block UNC paths to prevent network-based attacks
            if (trimmed.StartsWith("\\\\") || trimmed.StartsWith("//"))
                throw new ArgumentException("UNC paths are not allowed for security reasons");

            // Check for injection patterns
            if (ContainsInjectionPatterns(trimmed))
                throw new ArgumentException("Path contains potentially dangerous patterns");

            // Prevent directory traversal
            if (trimmed.Contains("..") || trimmed.Contains("~"))
                throw new ArgumentException("Path traversal patterns not allowed");

            // SECURITY: Block network drives and remote paths
            try
            {
                var fullPath = Path.GetFullPath(trimmed);

                // Check if it's a network drive (starts with \\)
                if (fullPath.StartsWith("\\\\"))
                    throw new ArgumentException("Network paths are not allowed");

                // Ensure it's within allowed directories
                var allowedPaths = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    Path.GetTempPath()
                };

                var isAllowed = allowedPaths.Any(allowed =>
                {
                    var allowedPath = Path.GetFullPath(allowed);
                    return fullPath.StartsWith(allowedPath, StringComparison.OrdinalIgnoreCase);
                });

                if (!isAllowed)
                    throw new ArgumentException("Path not in allowed directories");

                return fullPath;
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                throw new ArgumentException($"Invalid path format: {ex.Message}");
            }
        }

        // SECURITY IMPROVEMENT: Validate network profile names for safety
        public static string EnsureValidProfileName(string profileName)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("Profile name cannot be empty");

            var trimmed = profileName.Trim();

            if (trimmed.Length > 255)
                throw new ArgumentException("Profile name too long");

            if (ContainsInjectionPatterns(trimmed))
                throw new ArgumentException("Profile name contains potentially dangerous patterns");

            // Check for reserved names
            var reservedNames = new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "LPT1", "LPT2" };
            if (reservedNames.Contains(trimmed.ToUpperInvariant()))
                throw new ArgumentException("Profile name uses reserved system name");

            return trimmed;
        }

        // PERFORMANCE IMPROVEMENT: Validate time duration values
        public static int EnsureValidDuration(int duration, int min, int max, int defaultValue)
        {
            if (duration < min || duration > max)
                return defaultValue;

            return duration;
        }

        // UX IMPROVEMENT: Sanitize user-facing output
        public static string SanitizeForDisplay(string value, int maxLength = 100)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Remove control characters
            var sanitized = ControlChars.Replace(value, string.Empty);

            // Truncate if too long
            if (sanitized.Length > maxLength)
                sanitized = sanitized.Substring(0, maxLength) + "...";

            return sanitized;
        }
    }
}
