using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using MurtiWifiConnecter.Interfaces;

namespace MurtiWifiConnecter.Infrastructure.Validation
{
    /// <summary>
    /// Comprehensive input validation framework for security and data integrity
    /// </summary>
    public static class InputValidator
    {
        /// <summary>
        /// Validate SSID format and security
        /// </summary>
        public static ValidationResult ValidateSSID(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid))
                return ValidationResult.Failure("SSID cannot be empty");

            if (ssid.Length > 32)
                return ValidationResult.Failure("SSID cannot exceed 32 characters");

            if (ssid.Length < 1)
                return ValidationResult.Failure("SSID must be at least 1 character");

            // Check for potentially dangerous characters
            var dangerousChars = new[] { '<', '>', '"', '&', '\0' };
            if (ssid.Any(c => dangerousChars.Contains(c)))
                return ValidationResult.Failure("SSID contains invalid characters");

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validate WiFi password strength and format
        /// </summary>
        public static ValidationResult ValidateWiFiPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                return ValidationResult.Failure("Password cannot be empty");

            if (password.Length < 8)
                return ValidationResult.Failure("Password must be at least 8 characters");

            if (password.Length > 63)
                return ValidationResult.Failure("Password cannot exceed 63 characters");

            // Check for null bytes and control characters
            if (password.Any(c => char.IsControl(c)))
                return ValidationResult.Failure("Password contains invalid control characters");

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validate file path for security
        /// </summary>
        public static ValidationResult ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return ValidationResult.Failure("File path cannot be empty");

            try
            {
                var fullPath = System.IO.Path.GetFullPath(filePath);
                
                // Check for path traversal attempts
                if (filePath.Contains("..") || filePath.Contains("~"))
                    return ValidationResult.Failure("Path traversal detected");

                // Check for invalid characters
                var invalidChars = System.IO.Path.GetInvalidPathChars();
                if (filePath.Any(c => invalidChars.Contains(c)))
                    return ValidationResult.Failure("Path contains invalid characters");

                // Prevent access to system directories
                var systemPaths = new[]
                {
                    Environment.SystemDirectory,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    Environment.GetFolderPath(Environment.SpecialFolder.System)
                };

                if (systemPaths.Any(sysPath => fullPath.StartsWith(sysPath, StringComparison.OrdinalIgnoreCase)))
                    return ValidationResult.Failure("Access to system directories not allowed");

                return ValidationResult.Success(fullPath);
            }
            catch (Exception ex)
            {
                return ValidationResult.Failure($"Invalid file path: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate IP address format
        /// </summary>
        public static ValidationResult ValidateIPAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return ValidationResult.Failure("IP address cannot be empty");

            if (IPAddress.TryParse(ipAddress, out var parsedIP))
            {
                return ValidationResult.Success(parsedIP.ToString());
            }

            return ValidationResult.Failure("Invalid IP address format");
        }

        /// <summary>
        /// Validate URL format and security
        /// </summary>
        public static ValidationResult ValidateUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Failure("URL cannot be empty");

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return ValidationResult.Failure("Invalid URL format");

            // Only allow HTTP and HTTPS
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return ValidationResult.Failure("Only HTTP and HTTPS URLs are allowed");

            // Check for suspicious patterns
            var suspiciousPatterns = new[]
            {
                @"javascript:",
                @"data:",
                @"file:",
                @"ftp:",
                @"<script",
                @"onload=",
                @"onerror="
            };

            if (suspiciousPatterns.Any(pattern => url.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
                return ValidationResult.Failure("URL contains suspicious content");

            return ValidationResult.Success(uri.ToString());
        }

        /// <summary>
        /// Sanitize string input for safe display and storage
        /// </summary>
        public static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // Remove or escape potentially dangerous characters
            var sanitized = input
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#x27;")
                .Replace("&", "&amp;");

            // Remove null bytes and control characters (except newlines and tabs)
            sanitized = Regex.Replace(sanitized, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "");

            return sanitized;
        }

        /// <summary>
        /// Validate email format
        /// </summary>
        public static ValidationResult ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return ValidationResult.Failure("Email cannot be empty");

            const string emailPattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            
            if (!Regex.IsMatch(email, emailPattern))
                return ValidationResult.Failure("Invalid email format");

            if (email.Length > 254)
                return ValidationResult.Failure("Email address too long");

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validate numeric range
        /// </summary>
        public static ValidationResult ValidateNumericRange(string value, double min, double max)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult.Failure("Value cannot be empty");

            if (!double.TryParse(value, out var numericValue))
                return ValidationResult.Failure("Value must be numeric");

            if (numericValue < min || numericValue > max)
                return ValidationResult.Failure($"Value must be between {min} and {max}");

            return ValidationResult.Success(numericValue);
        }

        /// <summary>
        /// Validate configuration key format
        /// </summary>
        public static ValidationResult ValidateConfigurationKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return ValidationResult.Failure("Configuration key cannot be empty");

            // Only allow alphanumeric, dots, underscores, and dashes
            const string keyPattern = @"^[a-zA-Z0-9._-]+$";
            
            if (!Regex.IsMatch(key, keyPattern))
                return ValidationResult.Failure("Configuration key contains invalid characters");

            if (key.Length > 100)
                return ValidationResult.Failure("Configuration key too long");

            return ValidationResult.Success();
        }

        /// <summary>
        /// Comprehensive input sanitization for logging
        /// </summary>
        public static string SanitizeForLogging(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "[null/empty]";

            // Limit length for logging
            var sanitized = input.Length > 500 ? input.Substring(0, 497) + "..." : input;

            // Remove sensitive patterns
            var sensitivePatterns = new[]
            {
                (@"password[=\s]*[\""]?([^\""\s]+)", "password=***"),
                (@"key[=\s]*[\""]?([^\""\s]+)", "key=***"),
                (@"token[=\s]*[\""]?([^\""\s]+)", "token=***"),
                (@"secret[=\s]*[\""]?([^\""\s]+)", "secret=***")
            };

            foreach (var (pattern, replacement) in sensitivePatterns)
            {
                sanitized = Regex.Replace(sanitized, pattern, replacement, RegexOptions.IgnoreCase);
            }

            return SanitizeInput(sanitized);
        }
    }

    /// <summary>
    /// Validation result with success/failure status and optional value
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; }
        public string ErrorMessage { get; }
        public object ValidatedValue { get; }

        private ValidationResult(bool isValid, string errorMessage = null, object validatedValue = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            ValidatedValue = validatedValue;
        }

        public static ValidationResult Success(object value = null) => new(true, validatedValue: value);
        public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
        
        public T GetValue<T>() => ValidatedValue is T value ? value : default(T);
    }

    /// <summary>
    /// Validation attributes for automatic validation
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public class ValidateSSIDAttribute : Attribute
    {
        public ValidationResult Validate(string value) => InputValidator.ValidateSSID(value);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public class ValidatePasswordAttribute : Attribute
    {
        public ValidationResult Validate(string value) => InputValidator.ValidateWiFiPassword(value);
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    public class ValidateFilePathAttribute : Attribute
    {
        public ValidationResult Validate(string value) => InputValidator.ValidateFilePath(value);
    }

    /// <summary>
    /// Security-focused validation extensions
    /// </summary>
    public static class SecurityValidation
    {
        /// <summary>
        /// Check for SQL injection patterns
        /// </summary>
        public static bool ContainsSQLInjection(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var sqlPatterns = new[]
            {
                @"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|CREATE|ALTER|EXEC|UNION)\b)",
                @"(\b(OR|AND)\s+\d+\s*=\s*\d+)",
                @"(--|\#|\/\*|\*\/)",
                @"(\bxp_cmdshell\b|\bsp_executesql\b)"
            };

            return sqlPatterns.Any(pattern => 
                Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Check for XSS patterns
        /// </summary>
        public static bool ContainsXSS(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var xssPatterns = new[]
            {
                @"<script[^>]*>",
                @"javascript:",
                @"onload\s*=",
                @"onerror\s*=",
                @"onclick\s*=",
                @"<iframe[^>]*>",
                @"<object[^>]*>",
                @"<embed[^>]*>"
            };

            return xssPatterns.Any(pattern => 
                Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase));
        }

        /// <summary>
        /// Comprehensive security validation
        /// </summary>
        public static ValidationResult ValidateForSecurity(string input, string context = "general")
        {
            if (ContainsSQLInjection(input))
                return ValidationResult.Failure("Input contains potential SQL injection");

            if (ContainsXSS(input))
                return ValidationResult.Failure("Input contains potential XSS");

            return ValidationResult.Success();
        }
    }
}