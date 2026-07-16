using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace MWC.Core.Tests;

// ══════════════════════════════════════════════════════════════
//  Locale key-set consistency guard.
//
//  Context (2026-07 audit): ROADMAP.md claimed "14 languages, 100%
//  translated", but a key-by-key comparison found Strings.bn.resx,
//  Strings.hi.resx, and Strings.ta.resx each had 274 of 426 entries
//  (64%) that were byte-for-byte identical English placeholder text —
//  the keys existed (so satellite-assembly fallback silently hid the
//  gap) but had never actually been translated. Separately,
//  Captive_NavigationFailed was missing from every non-neutral locale
//  including ja, the otherwise most complete translation.
//
//  This test only checks KEY PRESENCE (every locale has every key the
//  neutral Strings.resx defines), not translation quality/value
//  content — a strict "value differs from English" check would have
//  false positives for legitimate cross-language technical terms
//  (PHY, BSSID, WEP, AES, MLO, "2.4 GHz" etc. are correctly identical
//  even in fully-translated locales like ja/de, confirmed during the
//  audit). Key-presence coverage is the mechanically verifiable half
//  of "100% translated"; it would have caught the missing-key defects
//  above, though not the value-copied-verbatim defect, which required
//  the one-time manual comparison this test class documents.
// ══════════════════════════════════════════════════════════════
public class LocaleKeyConsistencyTests
{
    private static readonly string[] LocaleFiles =
    {
        "Strings.ar.resx", "Strings.bn.resx", "Strings.de.resx", "Strings.en.resx",
        "Strings.es.resx", "Strings.fr.resx", "Strings.hi.resx", "Strings.ja.resx",
        "Strings.ko.resx", "Strings.pt-BR.resx", "Strings.ru.resx", "Strings.ta.resx",
        "Strings.zh-Hans.resx", "Strings.zh-Hant.resx",
    };

    private static string ResourcesDir([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "src", "MWC.App", "Resources"));

    private static HashSet<string> KeysIn(string path)
        => XDocument.Load(path)
            .Descendants("data")
            .Select(e => (string?)e.Attribute("name"))
            .Where(k => k is not null)
            .Select(k => k!)
            .ToHashSet();

    public static IEnumerable<object[]> LocaleFileNames() => LocaleFiles.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(LocaleFileNames))]
    public void EveryLocale_DefinesEveryNeutralKey(string fileName)
    {
        var neutralKeys = KeysIn(Path.Combine(ResourcesDir(), "Strings.resx"));
        var localeKeys = KeysIn(Path.Combine(ResourcesDir(), fileName));

        var missing = neutralKeys.Except(localeKeys).ToList();

        missing.Should().BeEmpty(
            because: $"{fileName} is missing {missing.Count} key(s) that Strings.resx (neutral/" +
                     $"English fallback) defines — a missing key silently falls back to English " +
                     $"rather than failing loudly, so this must be checked mechanically");
    }
}
