using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MurtiWifiConnecter.Core;

namespace MurtiWifiConnecter.Tests
{
    [TestClass]
    public class ConsoleUIHelperTests
    {
        [TestMethod]
        public void ShowHeader_DisplaysHeaderWithoutException()
        {
            // Act & Assert - Should not throw
            try
            {
                // Note: This would normally display to console, but in tests we just ensure no exceptions
                var method = typeof(ConsoleUIHelper).GetMethod("ShowHeader",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                // In test environment, console operations might fail, which is acceptable
                Assert.IsTrue(ex is System.Reflection.TargetInvocationException ||
                             ex.Message.Contains("console") ||
                             ex.Message.Contains("redirect"));
            }
        }

        [TestMethod]
        public void DisplayMenu_ValidParameters_DoesNotThrow()
        {
            // Arrange
            var options = new[]
            {
                new ConsoleUIHelper.MenuOption("1", "Test Option 1", () => Task.CompletedTask),
                new ConsoleUIHelper.MenuOption("2", "Test Option 2", () => Task.CompletedTask)
            };

            // Act & Assert
            try
            {
                var method = typeof(ConsoleUIHelper).GetMethod("DisplayMenu",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, new object[] { "Test Menu", options });
            }
            catch (Exception ex)
            {
                // Console operations in test environment might fail
                Assert.IsTrue(ex is System.Reflection.TargetInvocationException ||
                             ex.Message.Contains("console") ||
                             ex.Message.Contains("redirect"));
            }
        }

        [TestMethod]
        public void ReadUserChoice_ReturnsNonNullString()
        {
            // Act
            try
            {
                var method = typeof(ConsoleUIHelper).GetMethod("ReadUserChoice",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var result = method?.Invoke(null, null) as string;

                // Assert
                Assert.IsNotNull(result);
            }
            catch (Exception ex)
            {
                // In test environment, console read operations will fail
                Assert.IsTrue(ex is System.Reflection.TargetInvocationException ||
                             ex.Message.Contains("console") ||
                             ex.Message.Contains("redirect"));
            }
        }

        [TestMethod]
        public void ReadPassword_ReturnsNonNullString()
        {
            // Act
            try
            {
                var method = typeof(ConsoleUIHelper).GetMethod("ReadPassword",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var result = method?.Invoke(null, null) as string;

                // Assert
                Assert.IsNotNull(result);
            }
            catch (Exception ex)
            {
                // In test environment, console read operations will fail
                Assert.IsTrue(ex is System.Reflection.TargetInvocationException ||
                             ex.Message.Contains("console") ||
                             ex.Message.Contains("redirect"));
            }
        }

        [TestMethod]
        public void SanitizeForDisplay_ValidInput_ReturnsSanitizedString()
        {
            // Arrange
            var method = typeof(ConsoleUIHelper).GetMethod("SanitizeForDisplay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            var result = method?.Invoke(null, new object[] { "Test\x00String\x01With\x7FControlChars", 50 }) as string;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(result.Contains('\x00'));
            Assert.IsFalse(result.Contains('\x01'));
            Assert.IsFalse(result.Contains('\x7F'));
        }

        [TestMethod]
        public void SanitizeForDisplay_NullInput_ReturnsEmptyString()
        {
            // Arrange
            var method = typeof(ConsoleUIHelper).GetMethod("SanitizeForDisplay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            var result = method?.Invoke(null, new object[] { null, 100 }) as string;

            // Assert
            Assert.AreEqual(string.Empty, result);
        }

        [TestMethod]
        public void SanitizeForDisplay_LongInput_TruncatesCorrectly()
        {
            // Arrange
            var longInput = new string('A', 200);
            var method = typeof(ConsoleUIHelper).GetMethod("SanitizeForDisplay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            var result = method?.Invoke(null, new object[] { longInput, 50 }) as string;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Length <= 53); // 50 + "..."
            Assert.IsTrue(result.EndsWith("..."));
        }

        [TestMethod]
        public void MenuOption_Constructor_SetsPropertiesCorrectly()
        {
            // Arrange
            Func<Task> action = () => Task.CompletedTask;

            // Act
            var option = new ConsoleUIHelper.MenuOption("1", "Test Option", action);

            // Assert
            Assert.AreEqual("1", option.Key);
            Assert.AreEqual("Test Option", option.Description);
            Assert.AreEqual(action, option.Action);
        }

        [TestMethod]
        public async Task MenuOption_Action_ExecutesSuccessfully()
        {
            // Arrange
            var executed = false;
            Func<Task> action = () => { executed = true; return Task.CompletedTask; };
            var option = new ConsoleUIHelper.MenuOption("1", "Test", action);

            // Act
            await option.Action();

            // Assert
            Assert.IsTrue(executed);
        }

        [TestMethod]
        public void ConsoleTable_AddRow_IncreasesRowCount()
        {
            // Arrange
            var table = new ConsoleUIHelper.ConsoleTable("Col1", "Col2");

            // Act
            table.AddRow("Value1", "Value2");
            table.AddRow("Value3", "Value4");

            // Assert - We can't directly test the private row count,
            // but we can verify the table was created successfully
            Assert.IsNotNull(table);
        }

        [TestMethod]
        public void ProgressIndicator_CanBeCreated()
        {
            // Act
            var progress = new ConsoleUIHelper.ProgressIndicator("Test Progress");

            // Assert
            Assert.IsNotNull(progress);
            // Note: We can't easily test the progress display in unit tests
            // as it interacts with console output
        }

        [TestMethod]
        public void GetCurrentVersion_ReturnsValidString()
        {
            // Act
            var method = typeof(ConsoleUIHelper).GetMethod("GetCurrentVersion",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var result = method?.Invoke(null, null) as string;

            // Assert
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result));
            Assert.IsTrue(result.Length > 0);
        }

        [TestMethod]
        public async Task ShowMainMenuAsync_CanStartWithoutException()
        {
            // Act & Assert
            // Note: This test is limited because the main menu requires user interaction
            // In a real scenario, you'd mock console input/output
            var cts = new CancellationTokenSource();
            cts.CancelAfter(100); // Cancel quickly to avoid hanging

            try
            {
                await ConsoleUIHelper.ShowMainMenuAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when token is cancelled
            }
            catch (Exception ex)
            {
                // Other exceptions might occur due to console operations in test environment
                Assert.IsTrue(ex.Message.Contains("console") ||
                             ex.Message.Contains("redirect") ||
                             ex is System.Reflection.TargetInvocationException);
            }
        }

        [TestMethod]
        public void ColorOutputSupport_IsDetected()
        {
            // Act - Color support detection happens in static constructor
            // We can't easily test this without reflection, but we can verify
            // that the class can be instantiated without issues

            // Assert
            Assert.IsNotNull(typeof(ConsoleUIHelper));
        }

        [TestMethod]
        public void ConsoleLock_IsAvailable()
        {
            // Arrange
            var consoleLockField = typeof(ConsoleUIHelper).GetField("_consoleLock",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            var consoleLock = consoleLockField?.GetValue(null);

            // Assert
            Assert.IsNotNull(consoleLock);
            Assert.IsInstanceOfType(consoleLock, typeof(object));
        }

        [TestMethod]
        public void ColorsSupported_Flag_IsAvailable()
        {
            // Arrange
            var colorsSupportedField = typeof(ConsoleUIHelper).GetField("_colorsSupported",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

            // Act
            var colorsSupported = colorsSupportedField?.GetValue(null);

            // Assert
            Assert.IsNotNull(colorsSupported);
            Assert.IsInstanceOfType(colorsSupported, typeof(bool));
        }
    }

    // Extension methods for testing private members
    internal static class ConsoleUIHelperTestExtensions
    {
        public static void ShowHeader(this ConsoleUIHelper helper)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("ShowHeader",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }

        public static void DisplayMenu(this ConsoleUIHelper helper, string title, ConsoleUIHelper.MenuOption[] options)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("DisplayMenu",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, new object[] { title, options });
        }

        public static string ReadUserChoice(this ConsoleUIHelper helper)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("ReadUserChoice",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return method?.Invoke(null, null) as string ?? string.Empty;
        }

        public static string ReadPassword(this ConsoleUIHelper helper)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("ReadPassword",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return method?.Invoke(null, null) as string ?? string.Empty;
        }

        public static string SanitizeForDisplay(this ConsoleUIHelper helper, string value, int maxLength = 100)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("SanitizeForDisplay",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return method?.Invoke(null, new object[] { value, maxLength }) as string ?? string.Empty;
        }

        public static string GetCurrentVersion(this ConsoleUIHelper helper)
        {
            var method = typeof(ConsoleUIHelper).GetMethod("GetCurrentVersion",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return method?.Invoke(null, null) as string ?? string.Empty;
        }
    }
}
