using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
    {
        private static readonly object _consoleLock = new();
        private static int _lastProgressLength = 0;

        // Atlassian Design System Color Palette
        public static class Colors
        {
            // Primary colors (Atlassian Blue)
            public const ConsoleColor Primary = ConsoleColor.Blue;
            public const ConsoleColor PrimaryLight = ConsoleColor.Cyan;
            public const ConsoleColor PrimaryDark = ConsoleColor.DarkBlue;

            // Semantic colors
            public const ConsoleColor Success = ConsoleColor.Green;
            public const ConsoleColor SuccessLight = ConsoleColor.DarkGreen;

            public const ConsoleColor Warning = ConsoleColor.Yellow;
            public const ConsoleColor WarningLight = ConsoleColor.DarkYellow;

            public const ConsoleColor Error = ConsoleColor.Red;
            public const ConsoleColor ErrorLight = ConsoleColor.DarkRed;

            public const ConsoleColor Info = ConsoleColor.Cyan;

            // Neutral colors (Atlassian Gray Scale)
            public const ConsoleColor Text = ConsoleColor.Gray;           // N800 - #172B4D
            public const ConsoleColor TextSubtle = ConsoleColor.DarkGray; // N200 - #6B778C
            public const ConsoleColor TextDisabled = ConsoleColor.DarkGray; // N100 - #8993A4

            // Surface colors
            public const ConsoleColor Surface = ConsoleColor.White;
            public const ConsoleColor SurfaceSubtle = ConsoleColor.Gray;  // N30 - #FAFBFC
            public const ConsoleColor SurfaceSunk = ConsoleColor.DarkGray; // N20 - #F4F5F7

            // Border colors
            public const ConsoleColor Border = ConsoleColor.DarkGray;      // N40 - #DFE1E6
            public const ConsoleColor BorderBold = ConsoleColor.Gray;      // N60 - #C1C7D0

            // Interactive colors
            public const ConsoleColor Interactive = ConsoleColor.Blue;     // B400 - #0052CC
            public const ConsoleColor InteractiveHover = ConsoleColor.Cyan; // B300 - #0065FF
            public const ConsoleColor InteractivePressed = ConsoleColor.DarkBlue; // B500 - #0747A6

            // Legacy colors for backward compatibility
            public const ConsoleColor Dim = TextSubtle;
            public const ConsoleColor Accent = Primary;
            public const ConsoleColor Header = Primary;
        }

        // Atlassian Design System Symbols
        public static class Symbols
        {
            // Status symbols
            public const string Check = "✓";
            public const string Cross = "✗";
            public const string Warning = "⚠";
            public const string Info = "ℹ";
            public const string Error = "✗";

            // UI symbols
            public const string Arrow = "→";
            public const string Bullet = "•";
            public const string Star = "★";
            public const string Lightning = "⚡";
            public const string Lock = "🔒";
            public const string Unlock = "🔓";

            // Network symbols
            public const string Signal0 = "◦";
            public const string Signal1 = "▰";
            public const string Signal2 = "▰▰";
            public const string Signal3 = "▰▰▰";
            public const string Signal4 = "▰▰▰▰";

            // Navigation symbols
            public const string ChevronRight = "›";
            public const string ChevronLeft = "‹";
            public const string ChevronUp = "˄";
            public const string ChevronDown = "˅";

            // Progress symbols
            public const string ProgressEmpty = "□";
            public const string ProgressPartial = "▨";
            public const string ProgressFull = "■";

            // Legacy symbols for backward compatibility
            public const string LegacyCheck = "[OK]";
            public const string LegacyCross = "[FAIL]";
            public const string LegacyWarning = "[WARN]";
            public const string LegacyInfo = "[INFO]";
        }
{{ ... }}

        public static void PrintHeader(string text, ConsoleColor color = ConsoleColor.Blue)
        {
            lock (_consoleLock)
            {
                var separator = new string('═', Math.Max(text.Length + 4, 30));
                var line = $"═ {text} ═";

                Console.ForegroundColor = color;
                Console.WriteLine(separator);
                Console.WriteLine(line);
                Console.WriteLine(separator);
                Console.ResetColor();
                Console.WriteLine();
            }
        }
{{ ... }}
        public static void ShowProgress(string message, int current, int total)
        {
            lock (_consoleLock)
            {
                var percentage = (int)((current * 100.0) / total);
                var barLength = 30;
                var filledLength = (int)((percentage * barLength) / 100.0);

                var bar = new StringBuilder();
                bar.Append('[');
                bar.Append(new string('#', filledLength));
{{ ... }}
                items.Add(("IP Address", status.IpAddress, null));
            }

            if (!string.IsNullOrEmpty(status.MacAddress))
            {
                items.Add(("MAC Address", status.MacAddress, Colors.Dim));
            }

            if (!string.IsNullOrEmpty(status.Band))
            {
                items.Add(("Band", status.Band, null));
            }

            if (!string.IsNullOrEmpty(status.Channel))
            {
                items.Add(("Channel", status.Channel, null));
            }

            if (!string.IsNullOrEmpty(status.Authentication))
            {
                var authColor = status.Authentication.Contains("WPA3") ? Colors.Success :
                               status.Authentication.Contains("WPA2") ? Colors.Info :
                               status.Authentication.Contains("Open") ? Colors.Error : Colors.Warning;
                items.Add(("Security", status.Authentication, authColor));
            }

{{ ... }}
        }

        public static void ShowPerformanceMetrics(dynamic dashboard)
        {
            PrintHeader("Performance Dashboard", Colors.Info);

            // System metrics
            var systemItems = new List<(string, string, ConsoleColor?)>
            {
                ("Uptime", FormatDuration(TimeSpan.FromSeconds(dashboard.UptimeSeconds)), null),
                ("Memory", $"{dashboard.CurrentMemoryMB:F1} MB / {dashboard.PeakMemoryMB:F1} MB", null),
                ("Threads", dashboard.ThreadCount.ToString(), dashboard.ThreadCount > 50 ? Colors.Warning : null),
                ("Health Score", $"{dashboard.HealthScore:F0}%", GetQualityColor(dashboard.HealthScore))
            };

            PrintBox("System Metrics", systemItems);

            // Top operations table
            if (dashboard.Operations.Any())
            {
                var headers = new List<string> { "Operation", "Calls", "Success", "Avg (ms)" };
                var rows = dashboard.Operations.Take(5).Select(op => new List<string>
                {
                    op.Name.Length > 20 ? op.Name.Substring(0, 20) + "..." : op.Name,
                    op.TotalCalls.ToString(),
                    $"{op.SuccessRate:F0}%",
                    $"{op.AverageDuration:F0}"
                }).ToList();

                PrintTable("Top Operations", headers, rows);
            }

            // Recommendations
            if (dashboard.Recommendations.Any())
            {
                Console.WriteLine("\nRecommendations:");
                foreach (var rec in dashboard.Recommendations)
                {
                    PrintInfo($"  {Symbols.Arrow} {rec}");
                }
            }
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m";
            if (duration.TotalHours >= 1)
                return $"{(int)duration.TotalHours}h {duration.Minutes}m";
            if (duration.TotalMinutes >= 1)
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            return $"{duration.TotalSeconds:F1}s";
        }

        public static void ClearLine()
        {
            lock (_consoleLock)
            {
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            }
        }

        public static void ShowLogo()
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = Colors.Primary;
                Console.WriteLine("╔══════════════════════════════════════╗");
                Console.WriteLine("║    MurtiWiFi Connecter v2.0.0       ║");
                Console.WriteLine("║  Enterprise-Grade WiFi Manager      ║");
                Console.WriteLine("╚══════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        public static int ShowMenu(string title, List<string> options, string prompt = "Select an option")
        {
            lock (_consoleLock)
            {
                Console.WriteLine($"\n{title}");
                Console.WriteLine(new string('─', title.Length));

                for (int i = 0; i < options.Count; i++)
                {
                    Console.WriteLine($"  {i + 1}. {options[i]}");
                }

                Console.Write($"\n{prompt}: ");

                if (int.TryParse(Console.ReadLine(), out var choice) &&
                    choice > 0 && choice <= options.Count)
                {
                    return choice - 1;
                }

                return -1;
            }
        }

        public static bool Confirm(string message, bool defaultValue = false)
        {
            lock (_consoleLock)
            {
                var defaultText = defaultValue ? "Y/n" : "y/N";
                Console.ForegroundColor = Colors.Text;
                Console.Write($"{message} ");
                Console.ForegroundColor = Colors.Interactive;
                Console.Write($"[{defaultText}]");
                Console.ResetColor();
                Console.Write(": ");

                var input = Console.ReadLine()?.Trim().ToLower();

                if (string.IsNullOrEmpty(input))
                    return defaultValue;

                return input == "y" || input == "yes";
            }
        }

        // New Atlassian-inspired UI Components

        public static void ShowModal(string title, string message, ModalType type = ModalType.Info)
        {
            lock (_consoleLock)
            {
                var borderColor = type switch
                {
                    ModalType.Success => Colors.Success,
                    ModalType.Warning => Colors.Warning,
                    ModalType.Error => Colors.Error,
                    ModalType.Info => Colors.Info,
                    _ => Colors.Primary
                };

                var icon = type switch
                {
                    ModalType.Success => Symbols.Check,
                    ModalType.Warning => Symbols.Warning,
                    ModalType.Error => Symbols.Cross,
                    ModalType.Info => Symbols.Info,
                    _ => Symbols.Info
                };

                var width = Math.Min(60, Console.WindowWidth - 4);
                var titleLine = $"{icon} {title}";
                var messageLines = WrapText(message, width - 4);

                // Top border
                Console.ForegroundColor = borderColor;
                Console.WriteLine("┌" + new string('─', width - 2) + "┐");
                Console.ResetColor();

                // Title
                Console.ForegroundColor = Colors.Text;
                Console.WriteLine($"│ {titleLine.PadRight(width - 3)}│");
                Console.ResetColor();

                // Separator
                Console.ForegroundColor = borderColor;
                Console.WriteLine("├" + new string('─', width - 2) + "┤");
                Console.ResetColor();

                // Message content
                foreach (var line in messageLines)
                {
                    Console.ForegroundColor = Colors.Text;
                    Console.WriteLine($"│ {line.PadRight(width - 3)}│");
                    Console.ResetColor();
                }

                // Bottom border
                Console.ForegroundColor = borderColor;
                Console.WriteLine("└" + new string('─', width - 2) + "┘");
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        public static void ShowLozenge(string text, LozengeType type = LozengeType.Default, LozengeAppearance appearance = LozengeAppearance.Default)
        {
            lock (_consoleLock)
            {
                var color = GetLozengeColor(type, appearance);

                Console.ForegroundColor = color;
                Console.Write($" {text} ");
                Console.ResetColor();
            }
        }

        public static void ShowBadge(string text, BadgeType type = BadgeType.Default)
        {
            lock (_consoleLock)
            {
                var color = type switch
                {
                    BadgeType.Success => Colors.Success,
                    BadgeType.Warning => Colors.Warning,
                    BadgeType.Error => Colors.Error,
                    BadgeType.Info => Colors.Info,
                    BadgeType.Primary => Colors.Primary,
                    _ => Colors.TextSubtle
                };

                Console.ForegroundColor = color;
                Console.Write($"[{text}] ");
                Console.ResetColor();
            }
        }

        public static void ShowInlineMessage(string message, MessageType type = MessageType.Info)
        {
            lock (_consoleLock)
            {
                var (color, icon) = type switch
                {
                    MessageType.Success => (Colors.Success, Symbols.Check),
                    MessageType.Warning => (Colors.Warning, Symbols.Warning),
                    MessageType.Error => (Colors.Error, Symbols.Cross),
                    MessageType.Info => (Colors.Info, Symbols.Info),
                    _ => (Colors.Text, Symbols.Info)
                };

                Console.ForegroundColor = color;
                Console.WriteLine($"{icon} {message}");
                Console.ResetColor();
            }
        }

        public static string ShowInputField(string label, string defaultValue = "", bool isPassword = false)
        {
            lock (_consoleLock)
            {
                Console.ForegroundColor = Colors.Text;
                Console.Write($"{label}: ");
                Console.ResetColor();

                if (isPassword)
                {
                    return ReadPasswordField(defaultValue);
                }
                else
                {
                    var input = Console.ReadLine();
                    return string.IsNullOrWhiteSpace(input) ? defaultValue : input;
                }
            }
        }

        public static void ShowFormSection(string title, List<FormField> fields)
        {
            lock (_consoleLock)
            {
                // Section header
                Console.ForegroundColor = Colors.Primary;
                Console.WriteLine($"┌─ {title} " + new string('─', Math.Max(0, 50 - title.Length)));
                Console.ResetColor();

                foreach (var field in fields)
                {
                    Console.ForegroundColor = Colors.TextSubtle;
                    Console.Write($"│ {field.Label.PadRight(20)} ");
                    Console.ResetColor();

                    if (field.IsPassword)
                    {
                        Console.Write(": ");
                        ReadPasswordField(field.DefaultValue ?? "");
                    }
                    else
                    {
                        var value = field.DefaultValue ?? "";
                        Console.ForegroundColor = Colors.TextDisabled;
                        Console.WriteLine($": {value}");
                        Console.ResetColor();
                    }
                }

                Console.ForegroundColor = Colors.Primary;
                Console.WriteLine($"└" + new string('─', 52));
                Console.ResetColor();
                Console.WriteLine();
            }
        }

        // Helper methods
        private static string ReadPasswordField(string defaultValue)
        {
            var password = new StringBuilder();
            ConsoleKeyInfo key;

            Console.ForegroundColor = Colors.TextDisabled;
            if (!string.IsNullOrEmpty(defaultValue))
            {
                Console.WriteLine($"[default: {new string('*', defaultValue.Length)}]");
            }
            Console.ResetColor();
            Console.Write("Enter value: ");

            do
            {
                key = Console.ReadKey(true);

                if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password.Remove(password.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    break;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    password.Append(key.KeyChar);
                    Console.Write("*");
                }
            } while (key.Key != ConsoleKey.Enter);

            Console.WriteLine();
            return password.Length > 0 ? password.ToString() : defaultValue;
        }

        private static List<string> WrapText(string text, int maxWidth)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                if (currentLine.Length + word.Length + 1 <= maxWidth)
                {
                    if (currentLine.Length > 0) currentLine.Append(' ');
                    currentLine.Append(word);
                }
                else
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine.ToString());
                        currentLine.Clear();
                    }

                    if (word.Length <= maxWidth)
                    {
                        currentLine.Append(word);
                    }
                    else
                    {
                        // Split long word
                        var remaining = word;
                        while (remaining.Length > 0)
                        {
                            var chunkSize = Math.Min(maxWidth, remaining.Length);
                            lines.Add(remaining.Substring(0, chunkSize));
                            remaining = remaining.Substring(chunkSize);
                        }
                    }
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
            }

            return lines;
        }

        private static ConsoleColor GetLozengeColor(LozengeType type, LozengeAppearance appearance)
        {
            return (type, appearance) switch
            {
                (LozengeType.Success, _) => ConsoleColor.Green,
                (LozengeType.Removed, _) => ConsoleColor.Red,
                (LozengeType.Current, _) => ConsoleColor.Blue,
                (LozengeType.New, _) => ConsoleColor.Cyan,
                (LozengeType.Moving, _) => ConsoleColor.Yellow,
                (LozengeType.Default, LozengeAppearance.Subtle) => ConsoleColor.DarkGray,
                (LozengeType.Default, _) => ConsoleColor.Gray,
                _ => ConsoleColor.Gray
            };
        }
    }

    // New Enums for Atlassian-inspired components
    public enum ModalType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public enum LozengeType
    {
        Default,
        Success,
        Removed,
        Current,
        New,
        Moving
    }

    public enum LozengeAppearance
    {
        Default,
        Subtle
    }

    public enum BadgeType
    {
        Default,
        Primary,
        Success,
        Warning,
        Error,
        Info
    }

    public enum MessageType
    {
        Info,
        Success,
        Warning,
        Error
    }

    public class FormField
    {
        public string Label { get; set; } = "";
        public string? DefaultValue { get; set; }
        public bool IsPassword { get; set; } = false;
        public bool IsRequired { get; set; } = false;
    }

    public class ConsoleSpinner : IDisposable
    {
        private readonly CancellationTokenSource _cts = new();
        private Task _spinnerTask;

        public Task StartAsync(string message = "")
        {
            _spinnerTask = Task.Run(() => UIHelper.ShowSpinner(message, _cts.Token));
            return Task.CompletedTask;
        }

        public void Stop()
        {
            _cts.Cancel();
            _spinnerTask?.Wait(1000);
            UIHelper.ClearLine();
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}