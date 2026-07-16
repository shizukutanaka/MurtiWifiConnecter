using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  Theme dictionary contract guard.
//
//  Regression cover for a defect where the WPF app neither compiled nor
//  launched: every theme ResourceDictionary except Fluent.xaml was missing,
//  Fluent.xaml + MainWindow.xaml had malformed XAML (attributes glued with no
//  separating space), and the surviving dictionary did not define the full
//  brush set the views consume. The Core-only CI build never caught it because
//  pack-URI resources resolve at runtime.
//
//  These tests read the theme .xaml SOURCE files directly (no WPF Application
//  needed) and assert the three invariants that were violated:
//    1. every theme dictionary referenced by App.xaml / ThemeService exists,
//    2. every theme dictionary is well-formed XML,
//    3. every theme dictionary defines the complete brush-key contract.
// ══════════════════════════════════════════════════════════════
public class ThemeContractTests
{
    // The theme dictionaries App.xaml merges (Generic, Dark) and ThemeService
    // switches between (Dark, Light, Fluent, Solarized, Nord, Catppuccin).
    private static readonly string[] ExpectedThemeFiles =
    {
        "Generic.xaml", "Dark.xaml", "Light.xaml",
        "Fluent.xaml", "Solarized.xaml", "Nord.xaml", "Catppuccin.xaml",
    };

    // The brush keys every theme must define — the contract the views consume
    // via {DynamicResource …}. Kept in sync with the views by
    // ReferencedBrushKeys_AreAllInTheContract below.
    private static readonly string[] RequiredBrushKeys =
    {
        "BgBrush", "SurfaceBrush", "SurfaceHoverBrush", "SurfaceSelBrush",
        "FgBrush", "FgMutedBrush", "FgVeryMutedBrush", "DividerBrush",
        "AccentBrush", "AccentHoverBrush", "AccentTextBrush", "FocusBrush",
        "SuccessBrush", "WarnBrush", "DangerBrush", "DangerTextBrush",
    };

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // Locate src/MWC.App via this test file's compile-time path so the tests are
    // independent of the runtime working directory.
    private static string AppDir([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "src", "MWC.App"));

    private static string ThemesDir() => Path.Combine(AppDir(), "Themes");

    public static IEnumerable<object[]> ThemeFileNames()
        => ExpectedThemeFiles.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(ThemeFileNames))]
    public void EveryExpectedThemeFile_Exists(string fileName)
    {
        var path = Path.Combine(ThemesDir(), fileName);
        File.Exists(path).Should().BeTrue(
            because: $"{fileName} is referenced by App.xaml or ThemeService and must exist on disk");
    }

    [Theory]
    [MemberData(nameof(ThemeFileNames))]
    public void EveryThemeDictionary_IsWellFormedXml(string fileName)
    {
        var path = Path.Combine(ThemesDir(), fileName);
        // XDocument.Load throws on malformed XML such as glued attributes
        // (x:Key="…"Color="…"), which is exactly what broke the original files.
        var act = () => XDocument.Load(path);
        act.Should().NotThrow(because: $"{fileName} must be well-formed XAML the markup compiler accepts");
    }

    [Theory]
    [MemberData(nameof(ThemeFileNames))]
    public void EveryThemeDictionary_DefinesTheFullBrushContract(string fileName)
    {
        var keys = BrushKeysIn(Path.Combine(ThemesDir(), fileName));
        keys.Should().Contain(RequiredBrushKeys,
            because: $"{fileName} must self-completely define every brush the views consume");
    }

    [Fact]
    public void ReferencedBrushKeys_AreAllInTheContract()
    {
        // Every {DynamicResource …Brush} the views reference must be covered by
        // the contract, so a newly referenced brush forces the contract (and all
        // theme files) to be updated rather than silently resolving to nothing.
        var referenced = new HashSet<string>();
        foreach (var xaml in Directory.GetFiles(AppDir(), "*.xaml", SearchOption.AllDirectories))
        {
            if (Path.GetDirectoryName(xaml)!.EndsWith("Themes")) continue;
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(
                         File.ReadAllText(xaml), @"DynamicResource\s+(\w+Brush)\b"))
            {
                referenced.Add(m.Groups[1].Value);
            }
        }

        referenced.Should().NotBeEmpty("the views reference theme brushes via DynamicResource");
        referenced.Should().BeSubsetOf(RequiredBrushKeys,
            because: "every brush a view references must be in the theme contract every dictionary defines");
    }

    private static IReadOnlyCollection<string> BrushKeysIn(string path)
        => XDocument.Load(path)
            .Descendants(Xaml + "SolidColorBrush")
            .Select(e => (string?)e.Attribute(X + "Key"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToList();
}
