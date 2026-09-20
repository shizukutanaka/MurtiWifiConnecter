using System.Diagnostics.CodeAnalysis;

// CA1707: enum member names intentionally mirror the names used by the
// underlying specifications — renaming them would obscure the mapping and
// break the public SDK surface.
//   EapType:        IANA EAP method names (EAP-TLS, EAP-TTLS, PEAP-MSCHAPv2, EAP-AKA)
//   WifiBand / BandPreference: Wi-Fi band notation (2.4GHz etc.)
//   WcagCriterion:  WCAG 2.1 criterion numbers (1.1.1 → C1_1_1)
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "IANA EAP method names are normative identifiers.",
    Scope = "type", Target = "~T:MWC.Core.Models.EapType")]
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "Wi-Fi band notation (2.4GHz) is the standard spelling.",
    Scope = "type", Target = "~T:MWC.Core.Models.WifiBand")]
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "WCAG criterion numbers map directly to the spec (1.4.3 → C1_4_3).",
    Scope = "type", Target = "~T:MWC.Core.Services.WcagCriterion")]
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "Wi-Fi band notation (2.4GHz) is the standard spelling.",
    Scope = "type", Target = "~T:MWC.Core.Services.BandPreference")]
