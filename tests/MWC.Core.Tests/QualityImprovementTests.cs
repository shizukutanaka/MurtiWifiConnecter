using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using MWC.App.Services;
using Xunit;

namespace MWC.Core.Tests;

// ═══════════════════════════════════════════════
//  ErrorHandlerService
// ═══════════════════════════════════════════════
public class ErrorCategoryTests
{
    [Fact]
    public void ErrorCategoryHasAllExpectedValues()
    {
        var values = Enum.GetValues<ErrorCategory>();
        values.Should().Contain(ErrorCategory.Permission);
        values.Should().Contain(ErrorCategory.Network);
        values.Should().Contain(ErrorCategory.Io);
        values.Should().Contain(ErrorCategory.Timeout);
        values.Should().Contain(ErrorCategory.InvalidInput);
        values.Should().Contain(ErrorCategory.InvalidState);
        values.Should().Contain(ErrorCategory.Unknown);
    }
}

public class TryResultTests
{
    [Fact]
    public void OkHasValue()
    {
        var r = TryResult<int>.Ok(42);
        r.Success.Should().BeTrue();
        r.Value.Should().Be(42);
        r.ErrorMessage.Should().BeNull();
        r.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void FailHasMessage()
    {
        var r = TryResult<int>.Fail("error");
        r.Success.Should().BeFalse();
        r.Value.Should().Be(0);
        r.ErrorMessage.Should().Be("error");
        r.IsCancelled.Should().BeFalse();
    }

    [Fact]
    public void CancelledFlagsCorrectly()
    {
        var r = TryResult<int>.Cancelled;
        r.Success.Should().BeFalse();
        r.IsCancelled.Should().BeTrue();
        r.ErrorMessage.Should().BeNull();
    }
}

// ═══════════════════════════════════════════════
//  KeyboardShortcutService
// ═══════════════════════════════════════════════
public class KeyboardShortcutServiceTests
{
    private readonly KeyboardShortcutService _svc = new();

    [Fact]
    public void ShortcutsHaveAllCategoriesRepresented()
    {
        var cats = _svc.Shortcuts.Select(s => s.Category).Distinct().ToList();
        cats.Should().Contain(Category.Navigation);
        cats.Should().Contain(Category.Action);
        cats.Should().Contain(Category.View);
    }

    [Fact]
    public void ShortcutsAllHaveDescriptions()
        => _svc.Shortcuts.Should().OnlyContain(s =>
            !string.IsNullOrWhiteSpace(s.Title) &&
            !string.IsNullOrWhiteSpace(s.Description));

    [Fact]
    public void ShortcutsNoDuplicateKeyBindings()
    {
        var bindings = _svc.Shortcuts
            .Select(s => $"{s.Modifiers}+{s.Key}")
            .ToList();
        bindings.Should().OnlyHaveUniqueItems(
            because: "Each key combination must trigger one action");
    }

    [Theory]
    [InlineData("F1")]   // ヘルプ
    [InlineData("R")]    // 再スキャン
    [InlineData("F")]    // 検索
    [InlineData("Q")]    // QR
    [InlineData("E")]    // エクスポート
    public void EssentialShortcutsExist(string keyName)
        => _svc.Shortcuts.Should().Contain(s => s.Key.ToString() == keyName);

    [Fact]
    public void DisplayKeyFormatsModifiers()
    {
        var ctrl = _svc.Shortcuts.First(s => s.Modifiers == System.Windows.Input.ModifierKeys.Control);
        ctrl.DisplayKey.Should().StartWith("Ctrl+");
    }

    [Fact]
    public void ShortcutsCountAtLeast15()
        => _svc.Shortcuts.Count.Should().BeGreaterOrEqualTo(15,
            because: "Apple HIG requires comprehensive keyboard support");
}

// ═══════════════════════════════════════════════
//  ShortcutDefinition.DisplayKey
// ═══════════════════════════════════════════════
public class ShortcutDefinitionTests
{
    [Theory]
    [InlineData(System.Windows.Input.ModifierKeys.None,    System.Windows.Input.Key.F1, "F1")]
    [InlineData(System.Windows.Input.ModifierKeys.Control, System.Windows.Input.Key.R,  "Ctrl+R")]
    [InlineData(System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift,
                System.Windows.Input.Key.Tab, "Ctrl+Shift+Tab")]
    public void DisplayKeyIsHumanReadable(System.Windows.Input.ModifierKeys mods,
        System.Windows.Input.Key key, string expected)
    {
        var s = new ShortcutDefinition(Category.View, key, mods, "test", "test");
        s.DisplayKey.Should().Be(expected);
    }
}
