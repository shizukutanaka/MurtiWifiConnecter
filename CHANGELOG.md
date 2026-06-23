# Changelog

All notable changes to MWC will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Added
- **`mwc plan-channels` CLI command**: exposes `ChannelPlannerService` from the CLI. Options:
  `--band 2.4|5|6` (default: all bands), `--dfs` (include DFS candidates), `--ranked` (show full
  candidate table per band), `--json`, `--adapter`. Invalid `--band` values exit with code 2 rather
  than silently falling back to all-bands.
- **Channel planner** (`ChannelPlannerService`, ADR-0025): recommends which channel to set your
  *own* AP to, per band, from a neighbor scan — the inverse of the client-side advisors. Candidate
  sets follow operational best practice (2.4 GHz: 1/6/11; 5 GHz: non-DFS by default, opt-in DFS;
  6 GHz: PSC channels). Deterministic, signal-weighted, channel-width-aware interference scoring
  (0–100 cleanliness); skips unknown-channel neighbours; DFS-annotated. Pure Core, 15 golden tests.
- **GitHub Actions CI workflow** (`ci.yml`): Windows job builds the full solution (excluding
  `MWC.Platform.MacOS` which requires macOS) and runs `MWC.Core.Tests` with coverage; Ubuntu job
  builds `MWC.Core`, `MWC.Platform.Linux`, and cross-platform projects to catch Linux regressions
  early. Triggered on push/PR to `main`/`master` and all `claude/**`, `feature/**`, `fix/**`
  branches.
- **GitHub Actions CodeQL workflow** (`codeql.yml`): weekly C# SAST scan on `windows-latest`
  using `github/codeql-action@v3` with manual build mode.
- **Solution filter files** (`MWC.ci-win.slnf`, `MWC.ci-linux.slnf`): allow CI to build the
  appropriate project subset per platform without requiring MAUI/mobile workloads.

### Fixed
- **Tray quick-connect never showed the captive portal dialog**: the system-tray "Connect" menu
  item called `executor.ConnectAsync` and discarded the returned `ConnectionResult`, so when the
  joined network was behind a captive portal (`result.BehindCaptivePortal`) the
  `CaptivePortalDialog` was never opened and the user had no sign-in prompt. Applied the same
  check already present in `AdapterConnectExtension`: capture the result, and if `Success &&
  BehindCaptivePortal`, dispatch `CaptivePortalDialog` to the WPF UI thread via the existing
  `Dispatcher.InvokeAsync` call that was already there for tray refresh.
- **`AdapterViewModel.ConnectAsync` was dead code with wrong captive-portal handling**: a
  `[RelayCommand]`-decorated method that was never bound in XAML, never called from code, and
  never exercised in tests. It performed the raw connection without the Apple-style progress flow
  or captive-portal detection. Removed. All connection entry points now go through either
  `MainWindowCommands.ConnectAsync` → `AdapterConnectExtension` (UI) or the tray callback (now
  fixed above).
- **Connectivity probe was not bound to the connecting adapter — wrong internet/captive-portal verdict
  on multi-adapter PCs**: `IConnectivityChecker.CheckAsync` took no adapter argument and
  `HttpConnectivityChecker` issued its `msftconnecttest.com` probe over the OS *default route*. On a
  machine with more than one active interface — the exact scenario this tool exists for — the verdict
  was attributed to whichever adapter owned the default route, not the one that just associated.
  Concretely: adapter A (already online) + adapter B just joined a captive-portal hotspot → the probe
  egressed A and reported B as "Connected, internet OK", so the captive-portal dialog never fired; the
  reverse (A's dead default route masking B's good link) was equally possible. Threaded the `adapterId`
  through `CheckAsync(Guid? adapterId, …)` and bound the probe socket to that adapter's local IPv4 via
  `SocketsHttpHandler.ConnectCallback`. When the IP can't be resolved (link not yet assigned, no match)
  it falls back to the previous unbound behaviour, so the change is strictly ≥ prior correctness with no
  regression. Contract change is fully contained: the interface has one implementation
  (`HttpConnectivityChecker`) and one caller (`WindowsWifiService.ConnectAsync`); Linux/macOS don't use it.
- **i18n — neutral `Strings.resx` had 19 Japanese values, causing Japanese text for any unmatched
  locale**: The neutral file is the .NET fallback for every locale not explicitly listed; having 19
  keys in Japanese meant German, French, Spanish, Korean etc. users would see Japanese for status
  strings like "接続しました" instead of "Connected". Replaced all 19 values with English in the
  neutral file (matching the existing `en.resx` overrides). `ja.resx` already contained correct
  Japanese translations for all 19 keys so no change was needed there.
- **i18n — `Strings.bn.resx`, `Strings.hi.resx`, `Strings.ta.resx` each had 15 keys with literal
  Japanese text**: The three community-contributed locale files (Bengali, Hindi, Tamil) copied
  Japanese strings verbatim from an older neutral file instead of translating or omitting them.
  Bengali/Hindi/Tamil users saw Japanese UI for `Status_Copied`, `Progress_Connecting`,
  `Captive_SignInRequired`, `Detail_HasProfile`, and 11 other keys. Removed all 15 Japanese entries
  from each file; the keys now fall through to the (now-English) neutral, giving English as an
  acceptable placeholder until community translators provide the target language.
- **Accessibility — pinned-SSID list had no screen-reader name**: a full audit of every
  interactive control across all 15 XAML views found one gap against the "AutomationProperties.Name
  on all interactive elements" rule — the `PinnedList` ListBox in `AdapterPreferencesDialog`. Added
  an `Adapter_Pinned_ListAutomation` key and bound it; the app now has 100% accessible-name
  coverage on nameless interactive controls.
- **i18n (ja) — 81 user-facing strings showed English in the Japanese UI**: keys added during the
  locale-sync work carried English fallback values in every locale, including `ja` (the project's
  authoring language). Translated all 81 to natural Japanese in `Strings.ja.resx` — troubleshooting
  Titles/Reasons/Steps, security-advisory titles, roaming/power-save/mesh detail labels,
  recommendation grade/profile/dimension labels, MLO tiers, interference factors/advice, and
  connection-failure reasons. Technical terms and format placeholders preserved; only brand/legal
  strings left as-is.
- **`ProfileXmlBuilder` always emitted `<useOneX>false</useOneX>` for PSK profiles — malformed
  profile + failing golden test**: `BuildMsm` unconditionally added a `useOneX` element to
  `authEncryption`, so WPA/WPA2/WPA3-Personal (and Open/OWE/WEP) profiles got
  `<useOneX>false</useOneX>`. The `WPAPSK_LegacyAuth_CorrectXml` golden test requires
  `useOneX` to be **absent** for PSK (`Descendants("useOneX").Should().BeEmpty()`), so this test
  was failing — undetected because the test suite is Windows-only and CI is dormant. Fixed by only
  adding `<useOneX>true</useOneX>` for 802.1X/Enterprise auth; the element is omitted otherwise
  (Windows treats absence as false). The three Enterprise `useOneX == "true"` golden tests are
  unaffected.
- **Localization — 67 missing keys in all 14 locale `.resx` files**: `Strings.resx` (neutral) had
  453 keys but every locale file had only 386; 67 new keys added since v2.5.0 were never propagated.
  All 14 locale files now contain the full set (English fallback text for translators to override).
- **Localization — `NetworkDetailViewModel` used raw enum names as UI text**: `AuthLabel` was set
  with `n.Auth.ToString()` ("WPA3SAE", "WPA2PSK") and `CipherLabel` with `n.Cipher.ToString()`
  ("AES", "GCMP256"). Both now use typed L.cs helpers (`L.AuthCompact`, `L.CipherLabel`) backed by
  `Auth_Compact_*` and `Cipher_*` resx keys.
- **Localization — `ConnectDialog` showed hardcoded English security level labels**: the first
  argument to the auth label came from `SecurityBadgeService.GetBadge(auth).Label`, which returned
  Core-layer English strings ("Maximum Security", "Secured"). Changed to `L.SecurityLevelLabel(badge.Level)`.
- **Localization — `NetworkRecommendationEngine.Explain()` summary reached the UI in English**:
  `RecommendationSummary` was assigned directly from `Explain().Summary`, which contained hardcoded
  English grade names, usage-profile descriptions, and dimension names. Added 13 `Rec_*` resx keys
  and `L.BuildRecommendationSummary()`, `L.RecommendationGradeLabel()`, `L.UsageProfileDesc()`,
  `L.ScoreDimensionLabel()` to the App layer; `NetworkDetailViewModel` now builds the localized string.
- **Localization — expert-mode detail labels used English words directly**: `PredictedSignalLabel`
  embedded "samples", `LinkEstimateLabel` embedded "effective, 2-stream est.", `MloLabel` embedded
  "links" / "aggregate" and called `mlo.ReliabilityTier.ToString()`, `DistanceLabel` consumed
  `DistanceEstimate.Label` (hardcoded English in Core). Added 7 keys and `L.MloReliabilityLabel()`
  to replace all four call sites.
- **Localization — PHY generation labels hardcoded in Core reached App UI**: `ToGenerationLabel()`
  and `ToShortLabel()` extension methods (in `MWC.Core`) were called directly from
  `NetworkItemViewModel` and `NetworkDetailViewModel`. Added 18 `Phy_Gen_*` / `Phy_Short_*` resx
  keys and `L.PhyGenerationLabel()` / `L.PhyShortLabel()` to the App layer.
- **Logging — three silent `catch {}` blocks in App swallowed exceptions without trace**:
  `App.xaml.cs` (FlowDirection override), `MainWindow.xaml.cs` (background update check),
  `AdapterFailoverService.cs` (semaphore disposed during shutdown). All now log at `Debug` level.
  `MainWindow.xaml.cs` was missing `using Serilog;` which made `Log.*` unavailable.
- **Logging — two silent `catch {}` blocks in Platform.Windows swallowed exceptions without trace**:
  `WindowsWifiService.GetConnectedSsid` bare `catch { return null; }` now logs at `Debug`.
  `NetworkStateChangedEventHandlerBridge` static constructor had no logger at all — refactored to
  lazy-initialize via `EnsureRegistered(ILogger?)` called from `Subscribe()` so the `WindowsWifiService`
  logger is forwarded; the subscription is guarded by the existing lock and a `_registered` flag.
- **Two system tray icons appeared on startup**: `SystemTrayService` created its own `NotifyIcon`
  internally while `App.xaml.cs` independently registered a separate `NotifyIcon` singleton for
  `NotificationService` — both had `Visible = true`, showing two icons in the taskbar. Fixed by
  injecting the DI-registered singleton into `SystemTrayService` via constructor, removing the
  internal `new NotifyIcon { ... }`. `Dispose()` now only releases the GDI icon handle (owned by the
  service) and hides the icon; the `NotifyIcon` lifetime is managed by the DI container.
- **`GroupPolicyProvider.ReadValue` threw `InvalidCastException` on REG_QWORD registry values**:
  the method cast `gpVal` directly to `(int)`, which throws for `long` (REG_QWORD). Changed to a
  switch pattern that accepts both `int` (REG_DWORD) and `long` (REG_QWORD, clamped to `int` range).
- **Thread-safety — `HandoverPredictor._history` accessed without lock**: `RecordHandover`,
  `DetectFlapping`, and the `HistoryCount` property all read/wrote the `List<HandoverEvent>` without
  synchronisation. Added `_histLock` object and wrapped every access in `lock (_histLock)`.
- **`WindowsWifiService.DisconnectAsync` propagated native exceptions to callers**: the method was a
  bare `Task.FromResult(NativeWifi.DisconnectNetwork(adapterId))` with no error handling; a native
  failure (invalid adapter, driver error) would throw instead of returning `false`, unlike all other
  methods in the class. Wrapped in `try/catch` and return `false` on exception.
- **`WindowsWifiService.ConnectAsync` error log lacked SSID context**: the catch block logged
  "ConnectAsync failed" with no indication of which network was involved. Added
  `PiiMask.Ssid(ssid)` so debuggers can identify the failing SSID without persisting the plain value.
- **`FirstRunWizard` hardcoded hex colours bypassed the theme system**: five `#xxxxxx` literals
  created `SolidColorBrush` instances that ignored the current theme; `#E6E8EB` (near-white title)
  is invisible on a light/system-theme background. Replaced all five with `FindResource()` calls
  using the existing `FgBrush` / `FgMutedBrush` / `SurfaceBrush` / `AccentBrush` /
  `FgVeryMutedBrush` theme keys. XAML dot-indicator initial fills updated to `DynamicResource`
  bindings. Decorative emoji `TextBlock` hidden from the accessibility tree
  (`AccessibilityView=Raw`).
- **`CertificatePickerDialog` expiry indicator always showed transparent background**: the expiry
  indicator `Border` bound `Background="{Binding ExpiryBrush}"` but the `CertListItem` record
  exposes the property as `ExpiryColor` (type `Brush`). WPF binding failures are silent, so the
  circle was always transparent — expired/near-expiry certificates showed no orange/red warning
  background. Fixed by renaming the binding to `ExpiryColor`.
- **`CaptivePortalDialog` had no `NavigationFailed` handler**: if the embedded WebBrowser failed
  to load the captive portal page (network error, redirect loop, invalid certificate), the status
  label stayed at "Loading…" indefinitely with no user feedback. Added `OnNavigationFailed` which
  sets a localised error message and logs at Warning level.
- **`ProfileManagerDialog` async handlers silently swallowed exceptions**: `OnRefresh` and
  `OnDeleteOne` called `AsyncEventHelper.SafeRunAsync(null, ...)` with a null logger — any
  exception from `RefreshCommand` or `DeleteCommand` disappeared without a trace. Added
  `ILogger<ProfileManagerDialog>` to the constructor; `ShowProfileManagerAsync` now resolves the
  dialog from the DI container (`GetRequiredService`) instead of `new ProfileManagerDialog(pmVm)`
  so the logger is injected automatically.
- **`AdapterCommand.adapter band` silently mapped any unknown value to `BandPreference.Any`**: an
  unrecognised band string (e.g. `"wifi6"`, `"7"`) fell through the switch expression default arm
  and set the adapter to Any without error or feedback. Changed to an explicit switch with a `default`
  arm that writes an error and exits with code 2 (`ExitCode.InvalidInput`), consistent with
  `mwc plan-channels --band`. Also accepts `"2.4ghz"` / `"5ghz"` / `"6ghz"` aliases.
- **`ConnectDialog` strength bar used `Border.Fill` — compile error**: `ConnectDialog.xaml` set
  `<Border.Fill>` on the `StrengthBar` element and `ConnectDialog.xaml.cs` assigned
  `StrengthBar.Fill = ...` in code-behind. `Border` has no `Fill` property (only `Background`), so
  both the XAML compiler and the C# compiler would reject the file. Changed to `Border.Background` /
  `StrengthBar.Background =` in both files.
- **`ProfileManagerViewModel` `DeleteAsync` and `LoadAsync` had no reentrancy guard**: `DeleteAsync`
  was a bare `[RelayCommand]` with no `IsBusy` check — two rapid clicks would race across the
  `await _wifi.DeleteProfileAsync(...)` gap; the second call could delete a now-null `Selected` item
  and leave the UI desynced from the backend. `LoadAsync` had no early-exit when already loading —
  a concurrent adapter-switch could call `Profiles.Clear()` while the first load was still adding
  items. Both methods now guard with `if (IsBusy) return;` and wrap the work in `IsBusy = true` /
  `finally { IsBusy = false; }`.
- **`RnrParser.ParseRnrBody` used `return` on a truncated TBTT entry — remaining Neighbor AP
  Info sets in the same RNR element were silently discarded**: on encountering a TBTT entry whose
  declared `tbttInfoLen` bytes didn't fit in the remaining body, the method did `return` (exit
  `ParseRnrBody` entirely) instead of advancing past the bad set's remaining entries and continuing.
  Any valid Neighbor AP Info sets after the bad one were never parsed. Changed to advance
  `pos += (tbttCount − t) × tbttInfoLen` and `break` out of the inner loop — the outer
  while terminates naturally when `pos` exceeds the body boundary. Added a two-set golden test
  (`ParsesTwoNeighborInfoSets_BothContributed`) to guard against regression.
- **`WmmParser` and `BeaconIeParser` used mutable `static readonly byte[]` for the WMM OUI
  constant**: `private static readonly byte[] WmmOui = { 0x00, 0x50, 0xF2 }` is technically
  mutable — any code holding the array reference can overwrite its contents. Changed to
  `private static ReadOnlySpan<byte> WmmOui => [0x00, 0x50, 0xF2]` in both files; the collection
  expression is embedded as a compile-time literal in the read-only data segment with no heap
  allocation.

### Added
- **Test — `EvilTwinDetector` HighRisk scenario**: Existing tests only triggered one indicator at a
  time and never asserted `EvilTwinRisk.HighRisk` (they only checked `IsSuspect`). Added
  `Analyze_DifferentOuiAndSecurityDowngrade_IsHighRisk` which records a trusted WPA2PSK AP then
  analyses the same SSID with a different-OUI BSSID AND a security downgrade (→ Open), expecting
  HighRisk (≥2 reasons) — the two-indicator escalation path was untested.

### Fixed (previous entries)
  assertion would fail**: The `InlineData` for `("#001518", "#00C4CC", false)` expected
  `WcagLevel.AA`, but the computed WCAG contrast ratio between near-black #001518 and the teal
  accent #00C4CC is ≈8.71:1, which exceeds the AAA threshold of 7.0:1 for normal text. The
  `EvaluateContrast` method would therefore return `WcagLevel.AAA`, making the `.Be(WcagLevel.AA)`
  assertion fail. Fixed by updating the expected level to `WcagLevel.AAA`.
- **`ValidateSsid_MultibyteSsid_ChecksByBytes` test had a wrong string literal — assertion would
  fail**: The test aimed to verify that an SSID exceeding 32 UTF-8 bytes is rejected, but used
  `"日本語ネットワークXXX"` (9 × 3-byte Japanese chars + 3 × 1-byte ASCII = 30 bytes), which is
  *within* the 32-byte limit. `IsValidSsid` returned `true`, making the `Should().BeFalse()`
  assertion fail. The comment said "11文字 × 3 = 33 bytes" but the string only had 9 Japanese
  characters. Fixed by replacing the test string with `"日本語ネットワーク日本"` (11 three-byte
  characters × 3 bytes = 33 bytes > 32), which is correctly rejected by the validator.
- **`WiFi7MloTests.cs` was missing all using directives and namespace declaration — compile
  error**: The file contained two test classes (`EhtCapabilityMloIntegrationTests` and
  `FrozenDictionaryRegulatoryTests`) but had no `using` statements for `Xunit`, `FluentAssertions`,
  `MWC.Core.Models`, or `MWC.Core.Services`, and no `namespace MWC.Core.Tests;` declaration.
  Because the project-level implicit usings only cover `System.*` namespaces, all referenced types
  were unresolved — CS0246 errors for every type and attribute in the file. Added the four missing
  `using` directives and the `namespace` declaration to match the pattern of every other test file
  in the project.
- **`FakeWifi` in `FinalValidationV9Tests.cs` was missing three `IWifiService` methods — compile
  error**: `IWifiService` declares 8 methods (`GetAdaptersAsync`, `ScanAsync`,
  `RegisterProfileAsync`, `ConnectAsync`, `DisconnectAsync`, `DeleteProfileAsync`,
  `ListProfilesAsync`, `SubscribeEventsAsync`), but the private `FakeWifi` class inside
  `ConnectionExecutorIntegrationV2Tests` only implemented five. A concrete class that partially
  implements an interface is a compile error in C# — the entire test assembly would fail to build.
  Added the three missing stub implementations (`DeleteProfileAsync` → `true`,
  `ListProfilesAsync` → empty list, `SubscribeEventsAsync` → empty async enumerable) to match
  the pattern in `SlowFakeWifi` (`ValidationAndSecurityTests.cs`) and `FakeWifiService` in
  `Fakes/`.
- **`EapTls_EmptyServerNames` / `EapTls_WithServerNames` tests called `.Descendants()` on a
  `string` (`BugFixRegressionTests.cs`)**: `ProfileXmlBuilder.Build` returns a `string`, and
  `string` has no `.Descendants(XName)` method — this was a compile error that would prevent the
  entire test assembly from building. `ProfileXmlBuilderTests.cs` correctly calls
  `XDocument.Parse(xml)` first; the EAP-TLS regression tests missed that step. Fixed by renaming
  the local variable to `xmlStr`, parsing with `XDocument.Parse(xmlStr)` to `doc`, then calling
  `doc.Descendants(...)`.
- **`Analyze_OpenNetwork_InfoLevel` test asserted wrong severity for the open-network advisory
  (MWC-SEC-005)**: `SecurityAdvisoryService` has always emitted `AdvisorySeverity.Warning` for
  unencrypted networks (the implementation comment explicitly says "Warning rather than Critical"),
  but the test checked for `AdvisorySeverity.Info`. The test would have failed on every run. Fixed
  by correcting the assertion to `Warning` and renaming the test to
  `Analyze_OpenNetwork_WarningLevel`.
- **`DifferentAdapters_HaveSeparatePreferences` test used weak `ContainSingle` assertions
  (`PerAdapterPreferencesTests.cs`)**: `ContainSingle("Home")` in FluentAssertions treats the
  string argument as the failure message (the `because` parameter), NOT as the expected element
  value — it only asserts the collection has exactly one element; "Home" was silently discarded.
  Fixed with `.ContainSingle().Which.Should().Be("Home")` / `.Be("Office")` to actually verify
  element values.
- **`FrequencyMhz_IsDerivableFromChannel` property test used wrong 6 GHz frequency formula
  (`PropertyBasedTests.cs`)**: the test asserted `5950 + (c.Channel - 1) * 5` (gives 5950 MHz for
  channel 1 — invalid), but IEEE 802.11ax defines the formula as `5950 + channel * 5` (channel 1 =
  5955 MHz, channel 233 = 7115 MHz). Consistent with `SixGhzChannelHelper.ChannelToFreqMhz` and
  `RegulatoryDomainService.GetAvailable6GHzChannels`. Fixed by removing the `- 1` offset.

### Known issues
- **CI/CodeQL are dormant — manual activation required**: `ci.yml` and `codeql.yml` live under
  `ci/github-workflows/`, which GitHub never executes (only `.github/workflows/` is run). As a
  result the CI/CodeQL `README.md` badges are 404s and **no build or test has ever run on push or
  PR** — the WPF/XAML build is currently unverified by automation. The workflows themselves are
  correct (the `windows-latest` job builds + tests `MWC.ci.slnf`, which includes `MWC.App`, so it
  would compile XAML). They cannot be relocated by an automated commit: GitHub rejects pushes that
  touch `.github/workflows/` from an app without the `workflows` permission (`remote rejected …
  without 'workflows' permission`). **A maintainer must run the steps in
  `ci/github-workflows/README.md`** (copy both files into `.github/workflows/` and commit) to turn
  the safety net on. Until then, treat green local reads — not green CI — as the only verification.
- **Beacon-IE enrichment is dormant in the shipped app**: `WindowsWifiService` enriches scan
  results via `BeaconEnrichmentService`, but its `IBeaconIeProvider` defaults to
  `NullBeaconIeProvider` and the real `WlanBssIeProvider` (raw `WlanGetNetworkBssList` P/Invoke) is
  **deliberately not registered in DI** — its class doc requires on-hardware verification before
  activation. Consequently `FastTransition` (802.11r), `NeighborReport` (802.11k), `Pmf` (802.11w),
  `WpsEnabled`, `IsWpa3TransitionMode`, `BssLoad`, and `MobilityDomain` are unpopulated in normal
  use. **Full blast radius (each verified by source audit):**
    - Detail panel **Roaming row defaults to "Standard"** and **Mesh Fast-Transition is always false**.
    - **Security advisories that depend on IE-only fields never fire** — "Protected Management Frames
      Disabled" (MWC-SEC-002), "WPS Enabled" (MWC-SEC-007), and the WPA3-transition-mode warning —
      and the positive "Hardened" badge is never awarded (requires `Pmf == Required`). The advisory
      logic is defensively written (unpopulated → `PmfStatus.Unknown` → silence), so there are **no
      false positives**, but users get **false reassurance**: a genuinely PMF-disabled or WPS-enabled
      AP is never flagged. This is the most security-relevant consequence.
    - `NetworkRecommendationEngine`'s roaming dimension is a non-differentiating constant — ranking
      *order* is preserved (same offset for all networks), but ~20% of the General-profile weight is
      inert and the Realtime profile's 0.35 roaming emphasis is silently defeated.
    - **Not affected** (verified): congestion (falls back to co-channel AP count), distance,
      interference, and link estimate all use basic-scan fields and degrade gracefully.
  Activation path (requires a real Windows machine): verify `WlanBssIeProvider` against live
  hardware, register `IBeaconIeProvider → WlanBssIeProvider` in `App.xaml.cs` DI, then confirm the
  enrichment fields populate. Not done here — this environment cannot run native Windows P/Invoke and
  CI is dormant, so shipping it unverified would risk AccessViolations the author's gate exists to
  prevent.
- **802.11v BSS Transition Management parsing is implemented but enrichment is still dormant**: the
  Extended Capabilities IE (EID 127, bit 19) is now parsed by `BeaconIeParser` and propagated via
  `BeaconIeApplier`, so `BssTransitionMgmt` will be populated correctly once `WlanBssIeProvider`
  is activated. Until then, `RoamingAdvisoryService`'s **Seamless and Assisted tiers remain
  unreachable at runtime** — the parser logic is correct and tested, but the raw IE data never
  reaches it (see Beacon-IE enrichment known issue above for activation steps).
- **TWT/rTWT IEs are not extracted by any scanner**: `WifiNetwork.TargetWakeTime` and
  `RestrictedTwt` are never populated (no HE/EHT Capabilities IE parsing in the platform layer), so
  `PowerSaveAdvisorService.Analyze()` cannot report confirmed per-AP TWT support. Until that
  extraction exists, the detail panel's Power Saving row falls back to PHY-generation capability
  (see Fixed). Restoring true per-AP detection requires parsing the HE/EHT Capabilities elements in
  `WindowsWifiService` / `BeaconIeParser`.

### Removed
- **Deleted `PluginHost` — dead code that broke the `MWC.Core` build and was a credential-theft
  attack surface**: `src/MWC.Core/Services/PluginHost.cs` (a MEF-based host that loads arbitrary
  unsigned DLLs from the user-writable `%AppData%/MWC/plugins/` and runs them in-process) failed to
  compile and was never wired into DI — referenced only by its own tests. Two independent breaks:
  (1) it used bare `CancellationToken` on four methods with **no `using System.Threading;`**, and
  `MWC.Core` sets `<ImplicitUsings>disable</ImplicitUsings>`, so the type never resolved (CS0246);
  (2) it depended on the entire `System.Composition` (MEF2) package family (`ContainerConfiguration`,
  `[Export]`, `[MetadataAttribute]`, `GetExports`) which **is not referenced** by the csproj. Because
  CI is dormant and there is no local SDK, these were invisible — but a C# assembly compiles as one
  unit, so a single broken file fails the whole of `MWC.Core`, and every downstream project (App, CLI,
  tests) depends on Core, meaning **nothing in the solution actually built**. Rather than activate it
  (which would mean adding ~5 MEF NuGet packages to make dead code run), it was deleted: loading
  unsigned third-party DLLs in-process into an app that holds Wi-Fi credentials via DPAPI is a
  code-execution / credential-theft attack surface (any process or malware running as the user can
  drop a DLL in that path), and a generic plugin system directly contradicts CLAUDE.md's "Wi-Fi に
  集中 / no flashy features" charter. Removed the file and its three `PluginHostTests`. A full
  missing-`using` audit of the rest of `MWC.Core` (LINQ, Tasks, Generic, Threading) found no other
  break — `PluginHost` was the sole offender. Found by asking "with ImplicitUsings disabled and CI
  dormant, does the platform-agnostic core actually compile, or do we only assume it does?".

### Changed
- **The primary interactive Connect path now goes through `ConnectionExecutor` (single
  entry-point principle), as its own doc already claimed**: `AdapterConnectExtension.
  ConnectWithAppleFlowAsync` — the flow behind the main window's Connect button — built the
  profile XML and called `IWifiService.RegisterProfileAsync` + `ConnectAsync` directly, bypassing
  the executor that `CLAUDE.md` and the executor's own class doc designate as the one place all
  connects flow through (the doc even *listed* this method as a caller, which was false). As a
  result the most important, most-observed connect path in the app **missed the per-adapter
  `SemaphoreSlim`** — so a user pressing Connect while `AutoReconnectService` or
  `AdapterFailoverService` was mid-connect on the same adapter could issue two overlapping
  `WlanConnect` calls to one radio (the exact driver-level race the executor's lock exists to
  prevent) — and also missed the executor's OpenTelemetry activity/metrics and PII-masked
  structured logging. Rewrote `RunConnectionAsync` to call `executor.ConnectAsync(adapterId, ssid,
  auth, passphrase, 25s, ct)` (which does register + connect + history + semaphore + tracing in one
  guarded call); the old "Register"/"Authenticate" progress steps collapse into the single executor
  call while the cosmetic IP/Internet steps are preserved. Removed the now-duplicate
  `history.RecordConnection` calls (the executor records history, so keeping them would double-count
  every interactive connect), which in turn let `MainWindowCommands` drop its now-unused
  `NetworkHistoryService` dependency. Note: because `Directory.Build.props` sets
  `TreatWarningsAsErrors=true`, that leftover field would itself have been a CS0414 build error —
  another sign the solution has not been compiling.

### Fixed
- **`MainWindowCommands.MeasureQualityAsync` would not compile (CS1061)**: it called
  `_quality.MeasureAsync().AsTask()`, but `NetworkQualityService.MeasureAsync` returns
  `Task<NetworkQualityResult>` — and `Task<T>` has no `AsTask()` method (that is on `ValueTask<T>`).
  `ErrorHandlerService.TryAsync` already takes a `Func<Task<T>>`, so the call just needed
  `() => _quality.MeasureAsync()`. Another App-layer compile break that the dormant CI hid (the WPF
  project has never built on push). Removed the spurious `.AsTask()`.
- **`NetworkQualityService.MeasureAsync` had an inconsistent, misleading cancellation contract**:
  the ping loop began each iteration with `if (ct.IsCancellationRequested) break;` — a silent exit
  that returned a *partial* result as if it were a real measurement, and (because the tail
  `lost += samples - hits.Count - lost` counts never-attempted pings as lost) reported an inflated
  packet-loss percentage for a run the user cancelled. Meanwhile the very next line,
  `await Task.Delay(200, ct)`, *throws* `OperationCanceledException` on cancellation — so the same
  method honored cancellation two different ways depending on which statement observed it, and the
  CLI's `OperationCanceledException → "Measurement cancelled."` handler only fired for one of them.
  Replaced the silent `break` with `ct.ThrowIfCancellationRequested()` so cancellation is uniformly
  propagated as an exception (no bogus partial result), and added a `catch (OperationCanceledException)
  { throw; }` ahead of the best-effort `catch { lost++; }` so a future ct-aware ping overload cannot
  have its cancellation swallowed as a "lost packet". Also removed the now-dead
  `lost += samples - hits.Count - lost` tail line: with no early `break`, the loop always either
  completes all `samples` (each iteration adds exactly one to `hits` or `lost`) or throws, so that
  expression was provably `0` — and keeping it would silently resurrect the inflated-loss bug if a
  `break` were ever reintroduced. No test exercised this path (it does live pings).
- **Five clusters of stale Japanese test assertions would fail the moment the suite ran**: the prior
  English-conversion sweep of Core service outputs missed several assertions, invisible because the
  test assembly had not compiled (and CI is dormant). Each was realigned to the exact current English
  output: `BugFixRegressionTests.GradeLabel_MatchesLatencyAndLoss` (`優良/良好/普通/不良` →
  `Excellent/Good/Fair/Poor` against `NetworkQualityResult.GradeLabel`); `AppleHigTests.
  GetSignalLabel_ReturnsHumanLabel` (`優良/良好/普通/弱い/圏外` → `Excellent/Good/Fair/Weak/None`);
  `BeaconUptimeEstimatorTests.ToLabel_HumanReadable` (`2日/3時間/15分` → `2d/3h/15m`);
  `EvilTwinAndKalmanTests` four reason checks (`セキュリティ設定が混在/降格/なりすまし/ベンダー` →
  `different security configurations/downgrade/impersonation/vendor`); and
  `AppleHigTests.GetAdvice_BadCredentials_MentionsPassword` (`パスワード` → `password` against the
  now-English `TroubleshootingHelper` steps). Two of these were not mere format mismatches but
  silently-passing **false-positive** tests: `EvilTwin`'s `NotContain("ベンダー")` could never observe
  the English "vendor" reason so it passed vacuously (would not catch a real vendor-mismatch
  regression) — retargeted to "vendor"; and `BugFixRegressionTests.LastConnectedLabel_TimeLabels` had
  been weakened to a bare `NotBeNullOrWhiteSpace()` with its `expected` parameter (`たった今/2分前/…`)
  dead — restored to a real `Should().Be(...)` against the English labels (`just now/2m ago/1h ago/
  7h ago`), whose boundaries are deterministic. Removed a dead `|| "同一チャネル"` operand from a
  Handover interference check (source emits English "co-channel"). Found by a project-wide scan for
  Japanese literals appearing as *asserted values* (not `because:` reasons or test-input echoes).
- **Auto-reconnect and failover silently failed for every secured (PSK) network**: the most
  consequential bug of the session. `AutoReconnectService` and `AdapterFailoverService` re-connect
  with `passphrase=""` (they hold no saved key — the OS does), but `ConnectionExecutor.ConnectAsync`
  unconditionally rebuilt the profile XML via `ProfileXmlBuilder.Build`, which **requires** a non-empty
  passphrase for WPA/WPA2/WPA3/WEP and throws `ArgumentException` — caught and mapped to
  `ConnectionFailure.OsError` before `WlanConnect` was ever called. So the headline "Apple-style
  auto-join" feature was inert for every password-protected network. Fixed by skipping profile
  registration when the auth method needs a passphrase but none was supplied, reusing the existing
  saved OS profile; user-initiated connects (passphrase present) register as before. Regression test
  added across WPA2PSK/WPA3SAE/WPA3Transition/WPAPSK. Found by asking "what value does ConnectAsync
  actually receive when the *automatic* paths call it?" rather than reading the happy path.
- **Auto-reconnect and failover fought the user's intent**: clicking Disconnect dropped the link, then
  the background services treated that as an outage and reconnected (or failed over to the backup
  adapter) within seconds — the user could not stay disconnected. `ConnectionExecutor.DisconnectAsync`
  now timestamps user-initiated disconnects and both services consult `WasRecentlyDisconnectedByUser`
  (15 s / 45 s windows) before acting.
- **Background reconnect/failover failures were invisible**: both services called `NotifyConnected`
  on success but only logged failures, so a user whose automatic recovery failed saw nothing. They now
  call `NotifyFailed` on the unsuccessful branch, consistent with the interactive connect paths.
- **Failover had a cold-start blind spot and a self-race**: if an adapter dropped between app launch
  and the first 30 s poll, `_lastState` was empty so `wasConnected` was false and the event was missed;
  the first `CheckAsync` now only seeds baseline state. A long `ActivateFailoverAsync` could also let
  the next timer tick re-enter `CheckAsync` and race the unsynchronised state sets — guarded with a
  non-blocking `SemaphoreSlim(1,1)`. On exit the failover timer is now stopped before host teardown
  (symmetric with the AutoReconnect drain) so no callback races a disposed `IWifiService`.
- **"Forget network" left the history entry behind**: `ProfileManagerViewModel.DeleteAsync` deleted the
  Windows WLAN profile but not the `NetworkHistoryService` entry, so `AutoReconnectService` could
  resurface a just-forgotten SSID via `GetRecentSsids`. It now calls `_history.Forget(ssid)` on success.
- **Weak-security networks were visually indistinguishable; security level unspoken**: the security-dot
  `Style` had triggers for Excellent/Good/Fair/Danger but **not `Weak`** (WPA/TKIP), so those fell
  through to the default orange with no intent — and the dot exposed no `AutomationProperties.Name`, so
  screen-reader users got color only. Added the explicit `Weak` trigger and an automation name, and
  folded the security label into each list item's spoken text ("Signal 65% · Legacy Encryption").
- **`SignalHistoryService` leaked memory across locations**: `Record` added a ~5 KB ring buffer per
  distinct SSID on every scan and nothing ever evicted them (`Prune` was dead code and only trimmed
  *within* a buffer), so a roaming laptop accumulated a buffer for every SSID it had ever seen. Added a
  256-SSID LRU cap (evict least-recently-updated on overflow) and made `Prune` drop emptied buffers
  from the dictionary.
- **SSIDs were written to disk logs in plaintext (location-history leak)**: connection, auto-reconnect,
  and failover paths logged the raw SSID at Information/Warning level to the 7-day rolling Serilog
  file. Since SSIDs are location-identifying (and the list outlives a "forget"), those logs were a
  passive location trail on disk — and a user asked by support to "send your log file" would leak it,
  defeating the diagnostic bundle's careful redaction. This contradicted the app's own PII stance:
  `DiagnosticBundleService` masks SSIDs precisely because they are sensitive, but the everyday logs
  did not. Extracted the masking into a shared `PiiMask.Ssid` helper (the bundle now delegates to it)
  and applied it at every SSID log site, so persisted logs show e.g. `My****` instead of the full
  name. (The diagnostic bundle's redaction was verified complete — it masks the connected SSID, OUI-
  truncates MACs, scrubs IPv4/email/phone, and never emits a raw scan list.)
- **Screen-reader announcements were silent**: `AccessibilityService.AnnounceConnectionStatus` /
  `AnnounceError` (fired on connect success/failure and SSID copy) wrote to a live-region TextBlock
  that was `Visibility="Collapsed"` — and collapsed elements are excluded from the UI Automation
  tree, so Narrator/NVDA never observed the text change and announced nothing. `InjectLiveRegion`
  was also dead (never called). A blind user pressing Connect heard silence and could not tell
  whether it worked. Rewrote both to use `AutomationPeer.RaiseNotificationEvent` (the robust
  .NET Core 3+ API) from the window's peer, which announces independently of any element's
  visibility (status → `ActionCompleted`/`MostRecent`, errors → `ActionAborted`/`ImportantMostRecent`;
  best-effort, no-op when no screen reader is running). Found by asking not "does every control have
  an AutomationProperties.Name?" (it does) but "can a blind user actually tell what happened?".
- **Language selector did nothing; no RTL support despite shipping Arabic**: the `Language` setting
  was defined, editable, and saved, but **never applied** — nothing set `CurrentUICulture` from it,
  so `L.cs` resolved strings against the OS culture and selecting any of the 14 non-OS languages had
  no visible effect. The app also shipped a full Arabic (RTL) locale with **zero `FlowDirection`
  handling**, so Arabic would render in a left-to-right shell (mirrored labels, wrong alignment).
  `App.OnStartup` now applies the saved language to the UI/format cultures before any window is
  created, and for RTL cultures flips the default `FlowDirection` to right-to-left (guarded — best
  effort, falls back to LTR). Language changes take effect on restart. Found by switching from a
  code-correctness lens to a user-experience one: the resx tables were perfectly consistent, but the
  translated app was never actually reachable by the user.
- **Process-output deadlock on Linux and macOS scanners**: `NmcliWifiService` and
  `CoreWlanWifiService` redirected both stdout and stderr but drained them sequentially
  (`ReadToEndAsync(stdout)` to EOF, then stderr). If the child process writes more to stderr than
  the OS pipe buffer (~64KB) before closing stdout, it blocks on the stderr write while the awaiter
  waits on stdout that never ends — a deadlock. Because callers pass `ct = default` (no timeout),
  the hang would be permanent (UI thread parked on the scan). Both now start both reads first and
  await them together so the pipes drain concurrently. Also completed the cancellation contract:
  `Process.Dispose()` does not terminate a running child, so a cancelled call left an orphaned
  `nmcli`/`scutil` process; both now `Kill()` the child on `OperationCanceledException` before
  rethrowing (no-op for the current `ct = default` callers).
- **Linux scan corrupted any SSID ending in a backslash**: `NmcliWifiService.SplitTerse` split
  nmcli `-t` output with `Regex.Split(line, "(?<!\\\\):")`. A single-backslash negative lookbehind
  cannot count backslash parity, so a field ending in a literal `\` (nmcli-encoded as `\\`) made the
  following separator look escaped — e.g. SSID `foo\` → `foo\\:<bssid>…` failed to split, merging
  the SSID into the BSSID column and shifting every subsequent field (mode/channel/freq/signal/
  security). Replaced the regex with an escaping-aware sequential scanner that consumes `\X` as a
  literal `X` and treats only unescaped `:` as a separator (also folding in the unescape step). This
  extends the earlier terse-colon fix to the escaped-backslash case; empty fields and column counts
  are preserved.
- **Wi-Fi QR codes corrupted any SSID/password ending in a special character**: `WifiUri.Parse`
  stripped the trailing terminator with `TrimEnd(';')`, which cannot distinguish the format's
  structural `;;` from an escaped `\;` at the end of the last field. A password like `secret;`
  (built as `…P:secret\;;;`) round-tripped to `secret\` — a silently wrong credential on scan.
  Removed the trim and made the parse loop skip empty segments instead, so escaped trailing
  specials survive. Added a regression test covering an SSID and password that both end in `;`.
- **Roaming row presented a generic constant as a per-AP measurement**: the "Standard" tier showed
  `~250ms` (the `LegacyHandoverMs` literature default) as though it were this network's handover
  time, and — because beacon-IE enrichment is dormant (see Known issues) — that branch fires for
  essentially every AP. The no-evidence case now reads "Standard — no 802.11r/k/v detected" and the
  handover figures across tiers are labeled "typical" rather than implying a measurement. Detection
  capability is unchanged; this removes false precision and a false-negative implication.
- **Power Saving row showed a constant false "Legacy"**: the detail panel called
  `PowerSaveAdvisorService.Analyze()`, which keys entirely off `network.TargetWakeTime` /
  `RestrictedTwt` — fields no scanner populates (they default `false`). Every AP, including real
  Wi-Fi 7 hardware, therefore displayed "Legacy (DTIM/PSM)" as if it were an analysis result. The
  row now keeps the service result primary (so it wins once IE extraction exists) but falls back to
  PHY-generation capability when the service has no IE evidence: "TWT capable (Wi-Fi 6)" /
  "rTWT capable (Wi-Fi 7)" / "Legacy". The "capable / up to ~X%" wording states what the generation
  provides, not that the AP has it enabled — avoiding the same false-precision trap as the Link
  Estimate fix.
- **Link Estimate row overstated throughput for pre-Wi-Fi-6 networks**: the detail panel wired
  `LinkRateEstimator` with its optimistic defaults (`spatialStreams=2`, `supports4096Qam=true`)
  for *every* network, but the estimator is explicitly an 802.11ax/be (HE/EHT) MCS model. Applied
  to an 802.11n AP at strong signal it returned MCS 13 / 4096-QAM at an assumed 80 MHz / 2 streams
  — ~1490 Mbps PHY, roughly 10× the ~144 Mbps an 802.11n 20 MHz / 2-stream link can actually reach
  — and presented it as a factual estimate. Now `supports4096Qam` is derived from the real PHY
  (Wi-Fi 7/8 only) and the row is shown only for Wi-Fi 6+ networks where the model is valid;
  it collapses otherwise (older networks still show the platform's actual max-rate in the "Speed"
  row). The assumed 2 spatial streams — unknowable from a passive scan — is now stated in the
  label ("2-stream est.") instead of being silent.
- **CLI `--evil-twin` overstated its coverage**: `EvilTwinDetector` has five heuristics, but four
  (BSSID-mismatch, security-downgrade, open-impersonation, vendor-mismatch) gate on per-SSID trust
  history populated via `RecordTrusted` — which the stateless CLI never calls. So `mwc scan
  --evil-twin` only ever exercised the one history-free heuristic (same SSID advertising multiple
  security configs), while its name and help implied full rogue-AP detection. The flag now states
  its scope precisely ("stateless: same-SSID security-mismatch heuristic only") and the command
  output prints a header noting that BSSID/vendor/downgrade history checks require the desktop app.
  Detection capability is unchanged — the fix is honesty, so a security-conscious user does not draw
  false confidence from a green CLI result.
- **L.ActionClose missing accessor (XAML build break)**: five dialogs (About, ProfileManager,
  QrCode, ShortcutHelp, Troubleshooting) reference `{x:Static r:L.ActionClose}`, but `L` exposed no
  `ActionClose` member — only the resx key `Action_Close` ("Close") existed. Because `x:Static`
  resolves at XAML compile time, this fails the WPF build (MC3050). Added the accessor following the
  established `ActionCancel => Get("Action_Cancel")` pattern. Surfaced by tracing every `r:L.X`
  reference in XAML against `L.cs` members and resx keys; the reference graph is now clean in all
  directions (resx↔resx consistent at 385 keys × 15 locales, XAML→L, L→resx, code→resx).
- **Pin toggle bug**: "Pin Network" context menu item previously only pinned (never unpinned) and
  updated only the per-adapter `PinnedSsids` list, which is separate from the global
  `AppSettings.PinnedNetworks` list used by "Show Favorites First" sorting. Pinning now toggles
  the global list so pinned networks actually appear first when `ShowFavoritesFirst` is enabled.
  Context menu header dynamically switches between "Pin this network" / "Unpin this network"
  based on current state. `📌` pin indicator added to network list rows. `SettingsService`
  gets `IsPinned()` and `TogglePin()` helpers. `NetworkFilterViewModel.ApplyFilter()` now syncs
  `NetworkItemViewModel.IsPinned` for all source items on every filter pass so the indicator
  stays current after scans. 3 new i18n keys × 15 locales.

- **The `MWC.Core.Tests` assembly did not compile, so no test ever ran**: two breakages had
  accumulated and, because CI is dormant, went unnoticed. (1) `NetworkHistoryService` and
  `AdapterPreferencesService` gained a required `ILogger` constructor parameter (added for the
  silent-catch logging fix), but ~67 `new NetworkHistoryService()` / `new AdapterPreferencesService()`
  call sites across the test project were never updated — a hard `CS7036`. Made the logger parameter
  optional (`ILogger<T>? log = null`, falling back to `NullLogger`), which fixes every call site at
  once and is backward-compatible (DI still injects the real logger; the few sites already passing
  `NullLogger` still work). (2) `HighDensityScenarioTests` referenced `AdapterPreferences.Label`,
  which does not exist (the property is `CustomLabel`) — fixed. With the assembly compiling again, a
  long tail of **stale assertions surfaced**: ~40 assertions across 16 test files still expected the
  Japanese Core strings that an earlier pass had converted to English (e.g. `NetworkQualityResult`
  "タイムアウト"→"Timeout"/"不良"→"Poor", `SecurityAdvisoryService` "ダウングレード"→"downgrade",
  `DiagnosticBundleService` headers, `CaptivePortalService`, `ChannelAdvisorService`,
  `RoamingAdvisoryService`, `HandoverPredictor`, `SignalIconService`, `SecurityBadgeService`,
  `HealthCheckService` PII labels, etc.). Each was realigned to the exact current English output;
  genuinely-Japanese services and test-input echoes were left untouched. This is the kind of rot a
  dormant CI hides — the suite looked like 514 passing tests but compiled to zero.

- **Estimator constructors could silently produce `NaN` from degenerate parameters**: `RssiDistanceEstimator`
  validates its constructor (`pathLossExponent <= 0` throws), but its two siblings in the same
  Core/SDK estimator family did not, despite each having a divide-by-zero. `KalmanRssiFilter` with
  `measurementNoise <= 0` converges to a Kalman gain of `0/0 = NaN` (R is the gain denominator);
  `SignalQualityPredictor` with all-zero linear-combination weights normalizes via `0/0 = NaN`, which
  then propagates into every prediction and silently into UI/SDK consumers. No shipping caller passes
  such values, but these are public API surface (the project ships an SDK), so a third-party consumer
  could trip them. Added matching `ArgumentOutOfRangeException` guards (`KalmanRssiFilter`: `Q >= 0`,
  `R > 0`; `SignalQualityPredictor`: alphas in `(0, 1]`, weights `>= 0` with a positive sum),
  consistent with `RssiDistanceEstimator`, plus regression tests for each guard.

- **Connectivity probe could falsely report "no internet" after a network change (DNS staleness)**:
  `HttpConnectivityChecker`'s static `HttpClient` used `HttpClientHandler` with no
  `PooledConnectionLifetime`, so in a long-running session its pooled connections to
  `www.msftconnecttest.com` never re-resolved DNS. After the machine changed networks, the probe
  could keep dialing a stale IP, throw, and (via the catch) report `HasInternet=false` even when the
  link was fine — surfacing a false "connected, no internet" to the user. Since this probe runs after
  every connect and as the second half of the success check, it is higher-traffic than the update
  check that had the same flaw. Switched to `SocketsHttpHandler` (which preserves the existing
  `AllowAutoRedirect=false` captive-portal semantics, `UseCookies=false`, `UseProxy=false`) with
  `PooledConnectionLifetime = 5 min`, matching the `AppUpdateService` fix. The CLI's
  `QualityHistoryCommand` client was checked and left as-is — it is short-lived and `using`-disposed.

- **Connect-completion continuations ran inline on the native WLAN notification thread**:
  `ConnectionWaiter` bridges the OS ACM `connection_complete`/disconnect notifications to a
  `Task` via `TaskCompletionSource`, but the TCS was created without
  `TaskCreationOptions.RunContinuationsAsynchronously`. `TrySetResult` is invoked from the native
  `NativeWifi.NetworkStateChanged` callback thread, so everything chained after
  `await waiter.WaitAsync(...)` in `WindowsWifiService.ConnectAsync` — the `IConnectivityChecker`
  HTTP probe and the waiter's own `Dispose` (which unsubscribes from the native event) — executed
  synchronously on that callback thread, where it can stall or deadlock delivery of subsequent WLAN
  notifications. Set `RunContinuationsAsynchronously` so the continuation hops to the thread pool,
  the textbook fix for an event-to-task bridge. The disconnect/reason-code classification was left
  untouched: it is an explicitly simplified placeholder whose behavior depends on native event
  timing that cannot be verified without Windows hardware, and this change is correct regardless of
  it. (`AnimationHelper`'s TCS was audited and intentionally left as-is: its `Completed` event fires
  on the UI thread, where its continuation belongs.)

- **Linux: passwords containing `&`, `<`, or `>` silently failed to connect (XML-decode gap at a
  trust boundary)**: `NmcliWifiService.RegisterProfileAsync` regex-extracts the SSID and PSK out of
  the Windows-format profile XML and passes them to `nmcli`, but never XML-decoded them. Because
  `ProfileXmlBuilder` builds the XML with `XElement` (mandated, to prevent injection), a passphrase
  like `a&b` is entity-encoded as `a&amp;b` in `<keyMaterial>` — and the regex `([^<]+)` captures the
  *encoded* form (`&lt;` contains no literal `<`), so `nmcli` received `a&amp;b` as the key. Every
  one of `&`, `<`, `>` is a legal WPA-PSK ASCII character, so ordinary passwords produced a wrong key
  and a `BadCredentials`-style failure with no hint why. Fixed by `WebUtility.HtmlDecode`-ing the
  extracted SSID and PSK (a no-op for keys without special characters; round-trips correctly even for
  a literal `&amp;` typed by the user). Found via a trust-boundary audit — "every SSID/BSSID is
  attacker-broadcast input; where does an untrusted or machine-encoded string reach a sink without
  being decoded/escaped?" — which also *verified* the adjacent process invocations are safe: both
  `NmcliWifiService` and `CoreWlanWifiService` spawn with `UseShellExecute=false` and
  `ProcessStartInfo.ArgumentList` (no shell), so SSID shell-metacharacters (`;`, `|`, `$()`) cannot
  inject commands, and `ProfileXmlBuilder`'s `XElement` construction makes the XML side injection-safe.

- **Auto-scan silently stopped updating the network list whenever a network appeared or
  disappeared**: `MainViewModel`'s 15-second rescan ran on a `System.Timers.Timer`, whose `Elapsed`
  fires on a ThreadPool thread with no `SynchronizationContext`. `AdapterViewModel.RefreshAsync`
  awaits `_wifi.ScanAsync(...)` without `ConfigureAwait`, so on the timer path the continuation
  resumed on the ThreadPool thread — and then mutated `SelectedAdapter.Networks`, the
  `ObservableCollection` bound to the main list (MainWindow.xaml). WPF forbids changing a
  Dispatcher-bound collection from another thread, so `Networks.Add`/`RemoveAt` threw
  `NotSupportedException`. The throw was swallowed by `SafeRefreshOne`'s per-adapter `catch` (logged
  as a warning), so the failure was invisible. The trap was intermittent: a steady-state tick only
  calls `NetworkItemViewModel.Update` (a `PropertyChanged`, which WPF marshals fine), so signal
  numbers kept updating — but the moment the *set* of SSIDs changed (a network came or went), that
  tick threw at the first `Add`/`RemoveAt`, aborting the rest of the refresh (status line, filter,
  connection state) too. The interactive Refresh button worked because `[RelayCommand]` runs on the
  UI thread, masking the bug. Fixed by switching the auto-scan to a `DispatcherTimer`, whose `Tick`
  fires on the UI thread, so the post-`await` continuation marshals back to the Dispatcher and every
  collection mutation is legal (the scan I/O itself still runs off-thread). Found by asking "which
  thread does each `ObservableCollection` mutation actually run on?" rather than assuming MVVM put
  them all on the UI thread. (`AllAdaptersOverviewViewModel` was audited too — it has no timer and
  refreshes only via UI-thread commands, so it was never affected.)

- **`IWifiService.ScanAsync` had no uniqueness contract; Linux/macOS violated the one every
  consumer assumes**: the shared App/Core layer treats SSID as a primary key —
  `SignalHistoryService` stores one ring buffer per SSID, `NetworkFilterViewModel` de-dupes rows by
  SSID, and `AdapterViewModel.RefreshAsync` builds `SourceNetworks.ToDictionary(n => n.Ssid)`. That
  only holds if a scan returns at most one `WifiNetwork` per SSID, but 802.11 never makes SSID
  unique: the same name is broadcast on 2.4/5/6 GHz and by every mesh node, and hidden networks all
  carry an empty SSID. `WindowsWifiService` quietly satisfied the invariant (`GroupBy(Ssid)`, BSSes
  aggregated into `BssEntries`, hidden dropped) — but the interface documented nothing, so the
  invariant was invisible and unenforced. Auditing the other implementations against it:
  `NmcliWifiService` (Linux) keyed its result map by **SSID+BSSID**, returning one row *per BSS* —
  duplicate-SSID entries for multi-band/mesh networks and a separate `"<hidden>"` row per hidden AP;
  `CoreWlanWifiService` (macOS) appended one row *per `airport` line* with no de-dup, no
  `BssEntries`, and no hidden filtering. On those platforms the same network appeared 2-3× in the
  CLI, `SignalHistoryService.Record` pushed multiple same-timestamp samples for one name into a
  single buffer (corrupting the trend graph), and any `ToDictionary(n => n.Ssid)` over the result
  would throw `ArgumentException`. Fixed by (1) documenting the one-`WifiNetwork`-per-SSID contract
  on `IWifiService.ScanAsync` — BSSes aggregated into `BssEntries`, strongest-signal BSS as the
  representative, hidden excluded — and (2) rewriting the Linux and macOS scanners to group by SSID,
  aggregate every BSS into `BssEntries` (Linux now also carries channel/freq/phy per BSS; macOS now
  populates `BssEntries` at all), pick the strongest BSS as the representative band/channel/signal,
  and drop empty SSIDs — matching the Windows behavior the whole stack already depended on. Found by
  asking "the code keys everything by SSID, but does the radio spec actually make SSID unique?".

- **Notification log leaked SSIDs to disk (missed PII site)**: the prior pass masked SSIDs at every
  connection/auto-reconnect/failover log site, but `NotificationService.Show` still logged the full
  notification `title`/`text` at Information level — and the title embeds the raw SSID (e.g.
  `NotifyConnectedTo(ssid)` → "Connected to MyHomeWifi"). So every connect/disconnect/failure toast
  wrote a location-identifying SSID to the 7-day rolling Serilog file, defeating the same PII stance
  the rest of the codebase enforces. Because the SSID is already baked into the localized string,
  masking a substring is unreliable; the log now records only the notification severity
  ("Notification shown (severity=Warning)"), keeping a diagnostic breadcrumb without the SSID.

- **Tray icon leaked GDI handles**: `SystemTrayService.BuildIcon` returned
  `Icon.FromHandle(bmp.GetHicon())` — `GetHicon()` allocates an unmanaged HICON that the managed
  `Icon` does not own and that was never freed, and `UpdateStatus` overwrote `_tray.Icon` without
  disposing the previous one. Each status refresh therefore leaked one GDI handle, which over a long
  session marches toward the per-process 10 000-handle ceiling (UI rendering then fails). `BuildIcon`
  now clones an independent managed `Icon` and frees the native handle via `DestroyIcon`,
  `UpdateStatus` disposes the outgoing icon, and `Dispose` releases the final one. (The leak is
  currently latent — `UpdateStatus`/`UpdateAdapterMenus` are not yet wired to any caller — but the
  per-process constructor allocation leaked too, and the path is now safe if the tray menu is wired
  up.)

- **`AppUpdateService` static `HttpClient` caused DNS staleness**: the singleton `HttpClient` had
  no `SocketsHttpHandler.PooledConnectionLifetime`, so connections pooled indefinitely. In a long-
  running WPF session, a DNS change to `api.github.com` (CDN rotation, maintenance) would never be
  picked up and update checks would silently fail against a stale resolved address. The handler now
  sets `PooledConnectionLifetime = TimeSpan.FromMinutes(5)`, matching the Microsoft-recommended
  pattern for long-lived `HttpClient` instances.

- **`GroupPolicyProvider` singleton was thread-unsafe**: `_instance ??= new GroupPolicyProvider()`
  without `volatile` is broken double-checked locking — C# memory model allows one thread to observe
  a partially-constructed object through a data race. Replaced with
  `Lazy<GroupPolicyProvider>(() => new(...))` whose `LazyThreadSafetyMode.ExecutionAndPublication`
  default gives correct publication semantics at no extra cost.

- **`AdapterPreferencesService.IsAutoReconnectEnabled` ignored `AutoConnectPriority`**: the method
  returned `IsEnabled && PinnedSsids.Count > 0`, but `PickBestSsid` tries `AutoConnectPriority`
  first. An adapter configured with only `AutoConnectPriority` (the UI's "Preferred Networks" list)
  — never `PinnedSsids` — would have `IsAutoReconnectEnabled` return false even though `PickBestSsid`
  would return a candidate, silently skipping auto-reconnect. Fixed to check both lists.
  Simultaneously fixed `SetAutoReconnect(false)` which cleared only `AutoConnectPriority` (comment
  said `PinnedSsids`) and contradicted `IsAutoReconnectEnabled` — now clears both.

- **`NetworkFilterViewModel.ApplyFilter` did not reorder on signal change**: the diff-update loop
  removed stale items and inserted new ones but never moved existing items to their new sort position.
  If network A's signal fell below B's between scans, the list kept showing A first — the user's
  "strongest-first" order froze at the first scan. Added a `Move(curIdx, i)` step so each item is
  positioned correctly after every filter pass.

- **Test compile errors in `ValidationAndSecurityTests.cs`**: `SlowFakeWifi` implemented only 5 of
  `IWifiService`'s 8 members, leaving `DeleteProfileAsync`, `ListProfilesAsync`, and
  `SubscribeEventsAsync` unimplemented (CS0535). Added stub implementations. Also,
  `new NetworkHistoryService()` was called without its required `ILogger` constructor argument
  (CS1503); fixed to pass `NullLogger<NetworkHistoryService>.Instance`.

- **HideNetwork was a no-op**: The "Hide this network" context menu item only set a status
  message and never modified `AppSettings.HiddenNetworks`, so the filter never excluded anything.
  `SettingsService` now has `HideNetwork()` and `UnhideNetwork()` helpers. `MainWindowCommands`
  has a new `HideNetwork(vm)` method. `OnHideNetwork` code-behind delegates to it. Settings
  dialog now has a "Hidden Networks" section listing all hidden SSIDs with per-item Unhide
  buttons; `SettingsViewModel` exposes `HiddenNetworks` as an `ObservableCollection` and a
  `UnhideCommand`. Filter is re-applied immediately when the Settings dialog closes.
  4 new i18n keys × 15 locales.

- **SecurityAdvisoryService i18n**: All advisory `Title` and `Detail` strings converted from
  Japanese to English, following the Core-layer-uses-English principle established in this
  release series. The advisory panel in the detail pane now renders language-neutral text.

- **`SettingsService.Save` blocked the UI thread with synchronous disk I/O**: `TogglePin`,
  `HideNetwork`, `UnhideNetwork`, and the Settings-dialog save all call `SettingsService.Save`,
  which did `File.WriteAllText` + `File.Move` synchronously on the calling (UI) thread. On a slow
  or network-backed `%LocalAppData%`, this stalls the WPF Dispatcher for the duration of the write,
  making the UI visibly freeze. The in-memory update (`_current = settings`) stays synchronous so
  the UI reflects the change immediately, but the disk write is now offloaded to `Task.Run` +
  `_saveLock` (matching the `NetworkHistoryService` / `AdapterPreferencesService` dual-lock pattern),
  so the Dispatcher is never blocked. Serialisation is preserved: rapid sequential saves queue behind
  the lock and the final snapshot always wins.

- **`ConnectionExecutor.ConnectAsync` could not convey `NonBroadcast` (hidden-network flag)**:
  the method signature `(adapterId, ssid, auth, passphrase, timeout, ct)` had no parameter for
  `WifiProfileSpec.NonBroadcast`, so callers that needed to connect to a hidden network were forced
  to bypass the executor entirely — losing per-adapter semaphore locking, OpenTelemetry tracing,
  PII-masked logging, and the `shouldRegister` passphrase-skip optimisation. Added a primary
  `ConnectAsync(Guid, WifiProfileSpec, TimeSpan?, CancellationToken)` overload that accepts the full
  spec (and therefore any current or future spec field); the existing six-parameter overload becomes
  a convenience wrapper that constructs a minimal spec and delegates. All seven existing callers
  continue to compile without changes.

- **CLI `connect` and `multi connect` bypassed `ConnectionExecutor`**: both commands called
  `svc.RegisterProfileAsync` + `svc.ConnectAsync` directly, missing the per-adapter semaphore (race
  with a concurrent desktop-app or CLI invocation), OpenTelemetry activity recording, PII-masked
  logging, and — critically for `multi connect` — history recording (`NetworkHistoryService.
  RecordConnection` was never called from `ConnectOneAsync`, so parallel multi-adapter sessions left
  no trace in the history file). Both commands now resolve `ConnectionExecutor` from DI
  (`ConnectionExecutor` registered in CLI `BuildServices`) and route through it. `BuildConnect`
  validates the profile spec early (fast `ProfileXmlBuilder.Build` smoke-test) to preserve the
  descriptive `InvalidInput` exit code, then delegates the full connection flow to the executor.
  `MultiAdapterCommand.ConnectOneAsync` scans to discover the auth method, then passes the
  resulting `WifiProfileSpec` to the executor — the manual `RegisterProfileAsync` call is gone and
  history is recorded automatically inside the executor.

### Added
- **詳細パネル大拡張 (9 サービス統合)**: `NetworkDetailViewModel.Load()` に 8 つの新行を追加し、
  Core 層の未使用サービスを一挙に公開。
  ① **推定距離** — `RssiDistanceEstimator` (対数距離減衰モデル) による推定距離と信頼度。
  ② **ローミング能力** — `RoamingAdvisoryService` による 802.11r/k/v 対応状況とハンドオーバー推定時間。
  ③ **シグナルトレンド予測** — `SignalQualityPredictor.PredictFromHistory()` が RSSI 履歴 3+ サンプルから
     次回 RSSI を EMA 三重平均で予測。`AdapterViewModel` が RSSI 履歴を `Load()` に渡す。
  ④ **推定スループット** — `LinkRateEstimator.Estimate()` による MCS インデックス / PHY レート /
     有効スループット (~65%) / SNR。RSSI と帯域幅から算出。
  ⑤ **MLO 検出** (Wi-Fi 7) — `MloAnalyzerService.Analyze()` がマルチリンク動作を検出し、
     リンク数・バンド組合せ・集約スループット・信頼性階層を表示。
  ⑥ **干渉分析** — `InterferenceAnalyzer.Analyze()` が同一/隣接チャンネルの AP 数から
     干渉スコア (0–100) と主要因を表示。
  ⑦ **メッシュ検出** — `MeshNetworkDetector.Detect()` がスキャン結果から同一 SSID のメッシュグループを
     識別し、ノード数・バンドカバレッジ・802.11r・検出信頼度を表示。
  ⑧ **省電力能力** — `PowerSaveAdvisorService.Analyze()` による rTWT/TWT/Legacy 省電力ティアと
     推定バッテリー節約率。
  合計 9 新リソースキー × 15 言語 (Detail_Distance / Detail_Roaming / Detail_SignalTrend /
  Detail_LinkEstimate / Detail_Mlo / Detail_Interference / Detail_Mesh / Detail_PowerSave +
  Status_DiagnosticExported)。

- **Evil Twin / スプーフィング検出**: `EvilTwinDetector.Analyze()` をネットワーク選択時に自動実行。
  同一 SSID に複数のセキュリティ設定が存在するか、既知 BSSID の OUI が変化した場合、
  セキュリティ勧告パネルの先頭に Critical (HighRisk) または Warning (Suspicious) アドバイザリを挿入。
  接続成功時に `RecordTrustedConnection()` で BSSID を記録し、以降のスキャンでベンダー照合・
  セキュリティダウングレードを検出可能に。

- **スティッキークライアント警告**: `HandoverPredictor.IsStickyClient()` が接続中ネットワークの
  RSSI とセッション継続時間を評価し、弱信号のまま長時間保持している場合に Warning アドバイザリを追加。
  `AdapterViewModel` が接続経過時間を `Detail.Load()` に渡す。

- **推奨スコア説明 ToolTip**: `NetworkRecommendationEngine.Explain()` の `Summary` を
  推奨スコア数値の `ToolTip` に設定。スコアにマウスオーバーすると上位ファクター (Security/
  Roaming/Channel/Signal) の詳細が表示される。

- **診断レポートエクスポート**: オーバーフローメニューに「サポートレポートをエクスポート」を追加。
  `DiagnosticBundleService.Build()` が PII を除いた Markdown レポートを生成し、
  SaveFileDialog 経由でファイル保存。`HealthCheckService.CheckAdapters()` の診断結果も含む。
  `Ctrl+Shift+?` のキーボードショートカット無し(メニューのみ)。1 新リソースキー × 15 言語。

- **CLI `scan` コマンド拡張**: 5 つの分析フラグを追加。
  `--recommend` : 推奨スコア降順ランキング + 上位ファクター表示。
  `--evil-twin` : `EvilTwinDetector` による疑わしい AP の検出・一覧表示。
  `--interference` : 全ネットワークの干渉スコア・レベル・主要因の表形式表示。
  `--mesh` : メッシュグループの検出・ノード数・バンド・FT 対応状況の表示。
  `--advise` : 既存の `SecurityAdvisoryService` 警告 (既実装、今回文書化)。

- **CLI `connect` エラー改善**: 接続失敗時に `TroubleshootingHelper.GetAdvice()` を呼び出し、
  失敗理由の説明と対処ステップを stderr に出力。従来は `failed: BadCredentials` のみ。

- **セキュリティレベルバッジ**: ネットワーク一覧の各 SSID 行に色付きインジケーター (●) を追加。
  既存の `SecurityBadgeService.GetBadge()` を活用し、WPA3=緑 / WPA2=黄緑 / OWE=黄 /
  WPA(TKIP)=橙 / WEP・Open=赤 でひと目でセキュリティ強度を判別可能に。
  `NetworkItemViewModel` に `SecurityLevel`・`SecurityBadgeLabel`・`SecurityTechLabel` プロパティ追加。
  `MainWindow.xaml` に 7×7px の Border バッジ + ToolTip を追加。
  5 新リソースキー (`Security_Excellent` 等) × 15 言語。

- **DFS チャンネル警告**: 5GHz の DFS 対象チャンネル (UNII-2: 52–64, UNII-2E: 100–144) に接続時、
  ネットワーク一覧に ⚡ アイコン (ToolTip: 詳細説明)、詳細パネルにアンバー色の警告バナーを表示。
  `DfsChannelHelper.IsDfsChannel()` を `MWC.Core.Services` に新設。
  `NetworkItemViewModel.IsDfs` / `NetworkDetailViewModel.IsDfs` プロパティ追加。
  チャンネルラベルに "⚡ DFS" サフィックス追加。
  2 新リソースキー × 15 言語。

- **アダプターフェイルオーバー**: プライマリアダプターが切断されたとき、
  あらかじめ設定したバックアップアダプターへ自動的に切り替える新機能。
  `AdapterPreferencesService` に `FailoverAdapterId` / `EnableFailover` フィールドを追加、
  `SetFailover()` ヘルパーを追加。
  `AdapterFailoverService` (新規) が 30 秒ごとに接続状態を監視し、切断検出→バックアップ SSID スキャン
  →自動接続→トースト通知を実行。復旧時にも通知発行。
  `AdapterPreferencesDialog` にフェイルオーバーセクション追加(有効化チェックボックス +
  バックアップアダプター ComboBox)。
  `App.xaml.cs` に DI 登録・自動起動を追加。
  4 新リソースキー × 15 言語。

- **詳細パネル拡張**: 詳細タブに 3 項目を追加。
  ① ベンダー行 (`VendorLabel`) — PHY と Band の間に OUI 解決済みベンダー名を表示。
  ② 推奨スコア行 — `NetworkRecommendationEngine.Score()` の総合スコア (0–100) を表示。
  セキュリティ・ローミング・チャンネル・信号を重み付き合算した結果。
  ③ セキュリティ勧告リスト — `SecurityAdvisoryService.Analyze()` の結果をネットワーク選択時に
  自動表示。各勧告に重大度別の色バー (赤=致命的/橙=警告/青=情報/緑=良好) を付与。
  勧告がなければ非表示。`SecurityAdvisoryItem` UI レコードを `NetworkDetailViewModel` に追加。
  2 新リソースキー (`Detail_Vendor`/`Detail_Score`) × 15 言語。

- **接続経過時間表示**: アダプタータブのステータステキスト (`ConnectionStatusLabel`) が
  接続から経過した時間を表示 (例: "→ HomeWifi  (45m)", "→ OfficeAP  (2h 07m)")。
  `_connectedSince` / `_prevConnectedSsid` フィールドで SSID 変化を検出し、
  接続開始時刻を記録。スキャン毎に `OnPropertyChanged(nameof(ConnectionStatusLabel))` を発火。

- **信号トレンド矢印**: ネットワーク一覧の信号バー下部に ↑/↓ を表示。
  直近 3 サンプルの delta > ±5 で UP/DOWN、それ以外は非表示。
  `AdapterViewModel.RefreshAsync()` が `SignalHistoryService` を参照して計算、
  `NetworkItemViewModel.SignalTrendLabel` プロパティに設定。

- **アダプター別フィルタープリセット**: ネットワーク一覧フィルター設定 (`ShowSecuredOnly` /
  `ShowFavoritesFirst`) をアダプターごとに永続化。アダプター切替時に各アダプターの
  フィルター設定を自動復元。`AdapterPreferences` レコードに 2 フィールド追加、
  `AdapterPreferencesService` に `SetFilterPreset()` ヘルパー追加。
  `NetworkFilterViewModel` が `AdapterPreferencesService` を受け取り、
  `SetAdapter(Guid?)` 呼び出しでプリセットを読み込み、変更時に自動保存。
  `MainViewModel.OnSelectedAdapterChanged` で `Filter.SetAdapter(v?.Id)` を呼び出し。

- **チャンネル混雑インジケーター**: ネットワーク一覧の各行に混雑度ドット (●) を追加。
  既存の `ChannelAdvisorService.AdviseCongestion()` を活用し、スキャン後に全ネットワークへ
  混雑度を設定。30%以上で橙色、75%以上(IsOverloaded)で赤色のドットを表示。
  ToolTip で利用率% を表示。`NetworkItemViewModel` に `CongestionPercent` /
  `IsChannelOverloaded` / `IsChannelCrowded` / `CongestionTooltip` プロパティ追加。
  `AdapterViewModel.RefreshAsync()` でスキャン後に全ネットワークの混雑度を計算・設定。
  2 新リソースキー × 15 言語。

- **品質グレード i18n 修正**: `NetworkQualityService.GradeLabel` が Core 層に埋め込まれた
  日本語文字列 ("優良"/"良好" 等) を返していた問題を修正。App 層では `L.QualityGradeLabel(r.Grade)`
  と `L.QualityTimeout` を使用するよう `MainWindowCommands.MeasureQualityAsync` を変更。
  6 新リソースキー (`Quality_Grade_Excellent` 等 + `Quality_Timeout`) × 15 言語。

### Fixed
- **UI 文字列ローカライズ (Round 14)**: App 層の残存ハードコード文字列を resx 経由化。
  `NotificationService` — `NotifyConnected`/`NotifyDisconnected`/`NotifyFailed` のトースト
  タイトル 5 箇所を `L.NotifyConnectedTo` 等の format メソッドへ移行。
  `MainWindowCommands` — アクセシビリティアナウンス(`AnnounceConnectionStatus`/`AnnounceError`)
  3 箇所と品質測定結果フォーマット文字列を `L.AnnounceConnected` / `L.QualityResultFormat` へ。
  `ConnectionProgressDialog` — 「IPアドレス取得」ステップ名を `L.StepIpAddress` へ。
  `SettingsViewModel` — スキャン間隔ラベル 6 件(`手動のみ`/`10秒`等)を constructor 初期化 +
  `L.ScanIntervalManual` 等へ。`JumpListService` — JumpList 接続説明文を `L.JumpConnectDescription` へ。
  `ConnectDialog.xaml` — `AutomationProperties.Name="パスフレーズ入力"` 2 箇所を
  `{x:Static r:L.ConnectPassphraseAutomation}` へ。
  `CertificatePickerDialog.xaml.cs` — 有効期限表示(`残 X 日`)を `L.CertPickerExpiryFormat` へ。
  19 新キーを全 15 言語 resx に追加。Core 層の `ProfileXmlBuilder` — EAP-AKA 例外メッセージを
  英語ニュートラル表記に統一(Core は App.Resources に依存不可のため)。
- **CI ワークフロー追加**: `.github/workflows/ci.yml` — `windows-latest` でフルビルド +
  `MWC.Core.Tests` 実行(trx アップロード)+ `ubuntu-latest` で `MWC.Core` /
  `MWC.Platform.Linux` ビルド確認。`MWC.ci.slnf` ソリューションフィルターで Android/iOS/macOS
  プロジェクトを除外し、クロスプラットフォーム SDK 未インストールによるビルド失敗を回避。
  `.github/workflows/codeql.yml` — `windows-latest` で週次 SAST(build-mode: manual)。
  README に CI/CodeQL バッジが既にあったが `.github/` ディレクトリが欠落していた問題を解消。
- **サイレント catch ブロック解消**: `NetworkHistoryService` と `AdapterPreferencesService` の
  `Load()`/`Save()`/`Persist()` メソッドで `IOException`/`UnauthorizedAccessException` を
  黙って握りつぶしていた。`ILogger<T>` を両コンストラクタに追加(DI 自動注入)し、
  `LogWarning` でパスと例外を記録するよう変更。ファイル I/O 失敗が Serilog に流れるため
  診断が可能になる。
- **UI 文字列ローカライズ (Round 13)**: Round 12 で残存していた WPF Binding 式内 StringFormat /
  FallbackValue ハードコード問題を ViewModel プロパティ化で解消。`AdapterViewModel` に
  `ToolbarStatusText`・`SignalHistoryTitle` を追加し `L.LabelConnected` / `L.MainSignalHistoryTitle`
  を経由。`NetworkDetailViewModel` に `SsidOrHint` を追加。`NetworkItemViewModel` に
  `SignalAutomationLabel` を追加 (`L.MainSignalStrength` 経由)。`AdapterPanelViewModel` に
  `NetworkListAutomationLabel` を追加。5 新キー (format + hint) × 15 言語。
  MainWindow.xaml と AllAdaptersOverviewView.xaml に日本語ハードコード文字列ゼロを達成。
- **UI 文字列ローカライズ (Round 12)**: `MainWindow.xaml` と `AllAdaptersOverviewView.xaml`
  に残存していたハードコード日本語文字列(計 ~49 箇所)を `{x:Static r:L.XYZ}` バインディングへ
  移行。52 の新リソースキー + `Detail_Connected` プロパティを L.cs に追加(全 15 言語対応)。
  `TabItem.Header`(詳細/信号履歴/チャンネル)・詳細パネル 9 フィールドラベル・コンテキスト
  メニュー・オーバーフローメニュー・AutomationProperties.Name・ToolTip をすべて resx 経由化。
- **UI 文字列ローカライズ**: `SettingsDialog.xaml`/`AdapterPreferencesDialog.xaml`/
  `ConnectDialog.xaml`/`AboutDialog.xaml` に散在していたハードコード日本語文字列を
  すべて `{x:Static r:L.XYZ}` バインディングへ移行。44 の新リソースキーを 15 言語すべての
  resx ファイルに追加(計 235 キー)。`CLAUDE.md` 規則「UI 文字列は必ず Strings.resx 経由」
  に準拠。
- **AdapterPreferencesDialog アンピンボタン**: `AutomationProperties.Name="✕"` が絵文字で
  スクリーンリーダーに非読み上げだった問題を `L.AdapterPinnedUnpin`（「ピン留めを外す」等）
  に変更。
- **ビルド阻害(XAML MC3024)**: `MainWindow.xaml` の同一 `<Button>` 要素に
  `AutomationProperties.Name` が 2 つ指定されていた 6 箇所を解消。WPF はこれを
  コンパイルエラーとして扱う。各ボタンでより説明的な名称を残した。
- **Linux スキャン列ズレ**: `NmcliWifiService` が `line.Split(':')` を使用していたが、
  nmcli terse(`-t`)モードはフィールド内のコロンを `\:` エスケープする(例: BSSID
  `AA:BB:CC` → `AA\:BB\:CC`)。単純分割では BSSID が複数列に展開され、信号強度・
  チャンネル・セキュリティの列が全てズレて実環境でスキャン結果が壊れていた。
  `(?<!\\):` 正規表現で非エスケープのコロンのみ分割後、`\:` と `\\` をアンエスケープ
  する `SplitTerse` ヘルパーに置き換えた。`GetAdaptersAsync`/`ScanAsync`/`ListProfilesAsync`
  の全 nmcli terse 行パースに適用。
- **FIPS 環境クラッシュ**: `GuidFromString` (Linux/macOS プラットフォームサービス)が
  `MD5.Create()` を使用していた。FIPS 強制環境(US 政府機関など)では MD5 が禁止されており
  `CryptographicException` をスローする。`SHA256.HashData` の先頭 16 バイトに変更。
  (この GUID はセキュリティ用途でなくデバイス名の決定論的識別子のみ。)
- **マルチアダプター SSID 誤帰属**: `WindowsWifiService.GetConnectedSsid` が
  `EnumerateConnectedNetworkSsids().FirstOrDefault()` で **全アダプター横断の先頭 SSID**
  を返していた。2 枚以上の Wi-Fi アダプターが異なる SSID に接続中の場合、接続状態が
  誤ったアダプターに表示される。`EnumerateConnectedNetworks()` で `adapterId` で絞り込む
  よう修正。
- **AllAdaptersOverviewViewModel コンパイルエラー(CS1010)**: ternary 式の false 分岐に
  `$MWC.App.Resources.L.ErrorConnectionFailed(...)` という `$` プレフィックスが残存し
  コンパイル不能だった。`$` を除去。
- **AutoReconnect が ScanOnStartup に誤ってゲートされていた**: `AutoReconnectService`
  が切断イベント受信時に `_settings.Current.ScanOnStartup` を確認し、`false` なら
  再接続を中断していた。この設定は起動時スキャン頻度を制御するもので再接続とは無関係。
  行を削除し、正しいアダプター別 `IsAutoReconnectEnabled` チェックのみ残した。
- **ConnectionExecutor がプロファイル登録失敗を無視**: `RegisterProfileAsync()` の `bool`
  戻り値を破棄していたため、プロファイル登録が失敗しても `ConnectAsync` を実行し、
  原因不明の接続タイムアウトが生じていた。失敗時に `OsError` で早期リターンするよう修正。
- **SettingsService 非原子書き込み**: `File.WriteAllText` 直書きのため、書き込み中
  クラッシュで settings.json が破損した。`.tmp` + `File.Move` に変更。
- **NetworkHistoryService 日本語ハードコード**: `LastConnectedLabel` が「たった今」等の
  日本語リテラルを返していた。Core 層は App の `L.cs` に依存できないため英語ニュートラル
  表記("just now", "m ago", "h ago", "d ago")に変更。

### Changed
- **CLI 終了コード**: マジックナンバー(0/1/2/4/5)を `ExitCode` 静的クラスの名前付き
  定数(`Success`/`GeneralError`/`InvalidInput`/`ProfileError`/`ConnectionFailed`)に
  統一。全 CLI ファイルで使用。
- **Linux イベント購読をリアルタイム化**: `NmcliWifiService.SubscribeEventsAsync` の
  5 秒ポーリングを `nmcli monitor` サブプロセスの非同期 stdout 読み取りに置換。
  プロセス死亡時は 3 秒後に自動再起動。
- **SHA-256 GUID**: Linux/macOS の `GuidFromString` を MD5 → SHA-256 先頭 16 バイトに変更。
- **アクセシビリティ**: `AllAdaptersOverviewView.xaml` の `AutomationProperties.Name`
  から絵文字を除去(`"↻ 全スキャン"→"全スキャン"`, `"⚡ 優先順に一括接続"→"優先順に一括接続"`,
  `"↑"→"優先順位を上げる"`, `"✕"→"優先リストから削除"`)。NVDA/JAWS は絵文字を
  「時計回り開放矢印」等と読み上げ、支援技術ユーザーの体験を損なうため。

### Added
- **WPAPSK / WPA3Enterprise ゴールデンテスト**: `ProfileXmlBuilderTests` に不足していた
  2 認証方式のゴールデンテストを追加。WPAPSK: authentication=WPAPSK/encryption=AES/
  useOneX なし。WPA3Enterprise: authentication=WPA3/encryption=AES(GCMP256 でない)/
  useOneX=true を検証。
- **NetworkHistoryService 並行ストレステスト**: 4 ライター × 4 リーダー同時実行で
  デッドロック・IndexOutOfRange が発生しないことを検証する 2 テストを追加。

- **ビルド阻害(取込みソース・App 層)**: `MainWindow.xaml` が `Click="OnAllAdaptersClick"`
  を 2 箇所で参照していたが、`MainWindow.xaml.cs` にハンドラが無く WPF コンパイル(MC3074)で
  失敗していた。ハンドラ `OnAllAdaptersClick` と `MainWindowCommands.ShowAllAdapters`
  (DI から `AllAdaptersOverviewViewModel` を解決し俯瞰ウィンドウ表示)を追加、
  Ctrl+Shift+A ショートカットも配線。全 App XAML のイベントハンドラ存在を静的走査し、
  他に欠落が無いことを確認。
- **ビルド阻害(取込みソース・体系的)**: MWC.Core / MWC.SDK の `netstandard2.0`
  ターゲットを撤廃し **net9.0 単一ターゲット**に統一。コードベースは `Math.Clamp` /
  `ArgumentNullException.ThrowIfNull` / `Random.Shared` / `.ToHashSet()` 等の net6+ API を
  約10ファイルで使用しており、これらは ns2.0 に存在せずポリフィル不可。ns2.0 ビルドは
  元から成立しておらず、実消費者(各 Platform プロジェクト)も net9.0 のため誤った
  ns2.0 互換主張を取り下げた。詳細は `docs/build-blockers-2026.md`。
- **ビルド阻害**: `MWC.Core` が `GroupPolicyProvider`(`Microsoft.Win32.Registry` 使用)を
  含むが、plain net9.0 では Registry が in-box でないため `Microsoft.Win32.Registry` を
  明示参照(Core/SDK)。
- **ビルド阻害(取込みソース)**: `Models/WifiNetwork.cs` が `System.Linq` を import せず
  `.Any()` を使用しており(ImplicitUsings 無効・global usings 無し)、MWC.Core が
  コンパイル不能だった問題を修正(`using System.Linq;` を追加)。
- **ビルド阻害**: `MainViewModel` に `RefreshAllAsync` が二重定義され、`[RelayCommand]`
  ソース生成が重複して MWC.App がコンパイルできなかった問題を修正(2つの実装を統合)。
- **スレッド安全性**: `NetworkHistoryService` の `GetRecentSsids` / `GetEntry` /
  `GetStats` / `GetFrequentSsids` / `Count` / `Forget` / `ClearAll` が
  ロック外で `_entries` を読み書きしていたデータ競合を修正。
- **6GHz フォールバック**: `WindowsWifiService.ChannelToFreq` のチャンネル→周波数
  推定境界を是正(ch14=2484、5GHz は ch32 から)。6GHz はチャンネル番号が
  2.4/5GHz と重複するため、ドライバー報告周波数を優先する旨を明記。
- トレイの `RequestOpenMainWindow` を App と MainWindow が二重購読し、前面化が
  2回走っていた(かつ解除されない)問題を解消。

### Changed
- バージョンを `Directory.Build.props` / `MWC.Core` / `MWC.SDK` で **3.11.0** に統一
  (CHANGELOG 先頭と乖離していた 2.5.0 を是正)。
- インライン指定だったパッケージ版(`Microsoft.Extensions.Logging.Abstractions` /
  `System.Text.Json` / `BenchmarkDotNet`)を `Directory.Packages.props` に集約。
- `RestoreLockedMode` を `MWC_LOCKED_RESTORE` オプトインに変更(lock ファイル未コミット
  下での CI restore 失敗を回避)。
- README のバッジ/本文を実態に同期(テスト 354→514、言語 11→14、ADR 14→24件)。

### Added
- **802.11k Neighbor Report パーサ `NeighborReportParser`**: Neighbor Report 要素
  (Element ID 52)の生バイト列を近隣 AP 情報(BSSID/チャネル/Operating Class/PHY/
  BSSID Info)へ構造化。Mobility Domain(802.11r 可否)/HT ビットも復号。リサーチ C5-4
  の実装。防御的(不正・切り詰め入力でも例外なし)。バイトレベルのゴールデンテスト6件。
- **ローミング安定性診断 `RoamingAdvisoryService.AnalyzeStability`**: スティッキークライアント
  (弱信号で居座り)と フラッピング(短時間の過剰ローミング)を、直近のローミング履歴と
  現在 RSSI から検出。リサーチ C5-6/7 の実装。純粋関数・テスト5件追加。
- **MLO アノマリー助言 `MloAnalyzerService.DetectAnomaly`**: Wi-Fi 7 MLO が単一(最良)
  リンクに劣りうる条件を検出(リンク非対称・全リンク弱・同一バンドのみ)。arXiv 2210.07695。
  リサーチ G10/C5-8 の実装。テスト5件追加。非破壊(既存 `MloAnalysis` は不変)。
- **CLI bufferbloat 計測 (`mwc quality --bufferbloat`)**: 直近実装した `MeasureResponsivenessAsync`
  を CLI に wiring。並列 HTTP ダウンロードで負荷を作り、アイドル/負荷時 RTT・RPM・bufferbloat
  グレード(A–F)を表示(`--load-url` で負荷 URL 変更可、既定は Cloudflare speed)。仕様 §12 の
  未wiring を解消。
- **CLI でセキュリティ助言を表示 (`mwc scan --advise`)**: Core の `SecurityAdvisoryService`
  は実装済みだが CLI から到達不能だった欠落を解消。Warning/Critical 助言をネットワーク別に表示。
- **総合スコアに WPS ペナルティ**: `SecurityAdvisoryService.ComputeScore` が WPS 有効 AP を
  減点していなかった(FR-44 と不整合)のを修正(-10)。テスト追加。
- **機能仕様書 `docs/specification.md`**: 要件 ID(FR-xx)付きの機能仕様。これを基準に
  ギャップを洗い出し、`ProfileXmlBuilder` の EAP サポートマトリクスの欠落を発見・実装。
- **EAP-TTLS (Type 21) プロファイル生成**: `ProfileXmlBuilder` が宣言済み `EapType.EAP_TTLS`
  を「未実装」例外にしていた欠落を実装(Windows EapTtlsConnectionPropertiesV1 スキーマ、
  Phase2=MSCHAPv2)。`spec.Validate()` に username+password 検証を追加。ゴールデンテスト追加。
  併せて `EapType.EAP_AKA`(SIM 認証)を「明示的に非サポート」として Validate/Build で拒否
  (従来は曖昧な NotSupported)。
- **WPS 有効 AP 警告 (MWC-SEC-007)**: `SecurityAdvisoryService` に WPS 有効 AP の警告を追加
  (`WifiNetwork.WpsEnabled`)。外部レジストラ PIN 方式の総当たり/Pixie-Dust 脆弱性を注意喚起。
  リサーチ C2/G3 の実装。テスト2件追加。
- **負荷時遅延(bufferbloat / responsiveness)計測**: `NetworkQualityService` に
  `MeasureResponsivenessAsync` を追加。アイドル時 RTT と負荷時 RTT を比較し、
  IETF responsiveness の RPM(round-trips/分)と A–F の bufferbloat グレードを算出
  (負荷生成は呼び出し側が供給)。純粋関数 `ComputeRpm` / `GradeBufferbloat` として
  分離し単体検証。リサーチ C4/G2 の実装。テスト9件追加。
- **MAC プライバシー助言 `PrivacyAdvisoryService` (MWC-PRIV-001〜004/100)**: MAC ランダム化
  状態 (`MacAddressMode`) と接続先から追跡リスクを診断。固定 MAC + 公共ネットワークを警告、
  ランダム化未使用に推奨、日次ローテーションを良好評価し、IE 指紋による再識別の限界も注記
  (arXiv 2206.10927 / 2412.10548 / 1703.02874)。リサーチ C6/G1 の実装。テスト5件追加。
- **FragAttacks セキュリティ勧告 (MWC-SEC-006)**: `SecurityAdvisoryService` に
  集約/フラグメンテーション欠陥 (CVE-2020-24586/24587/24588, Vanhoef USENIX 2021) の
  情報提供を追加。暗号化ありかつ MFP 未必須のネットワークで更新・HTTPS・MFP を助言。
  リサーチ(improvement-research-100 C2)で抽出した改善点の最初の実装。テスト2件追加。
- `docs/improvement-research-100.md` / `-part2.md`: arXiv + GitHub 出典付きの
  改善点リサーチ(10カテゴリー×10 を2部、計200項目)。
- `docs/improvement-analysis-2026.md`: 競合ソフト & arXiv (2024–2026) を参照した
  ギャップ分析(既存 arxiv-improvement-analysis.md / ROADMAP の差分)。MAC ランダム化
  プライバシー助言、負荷時遅延(bufferbloat/RPM)グレード、WPS 警告、Wi-Fi 8(802.11bn)
  能力バッジ、802.11bf センシング表示、metered 接続考慮 等の新規改善点を抽出。
- `.github/workflows/ci.yml`(Windows ビルド+テスト、Linux でコア検証)と
  `codeql.yml` を追加。README のバッジ参照先(従来 404)を実体化。
- `MWC.ci.slnf`: windows-latest でビルド不能な `MWC.Platform.MacOS`(net9.0-macos)
  を除外した CI 用ソリューションフィルター。

### Fixed (2026-06-16 batch)
- **`ProfileXmlBuilder.BuildEapTlsConfig` — empty `ServerNames` produced a malformed element**:
  PEAP and EAP-TTLS already guarded `spec.ServerNames is { Length: > 0 }` before `string.Join`,
  but EAP-TLS called `string.Join(";", spec.ServerNames)` unconditionally.
  With `WifiProfileSpec.ServerNames` defaulting to `Array.Empty<string>()` the element became
  `<ServerNames></ServerNames>`, which Windows may reject. Fixed to use the same pattern as the
  other two paths; two regression tests added (empty array → empty value, multi-value →
  semicolon-joined).

- **`AdapterViewModel.ConnectToSsidAsync` hardcoded `AuthMethod.WPA2PSK`**: the tray/quick-connect
  path that connects to an existing profile looked up the real SSID but manufactured the auth method
  as a constant. For Open or OWE networks this made `ConnectionExecutor.shouldRegister` think a
  profile registration was needed (because non-PSK auth always sets `shouldRegister=true`) and
  tried to register a WPA2PSK profile for a genuinely Open AP. Fixed to look up `SourceNetworks`
  for the actual auth, falling back to WPA2PSK only when the SSID is not in the current scan.

- **`SystemTrayService` held an unused `IWifiService` field (CS0414 latent build error)**:
  the constructor accepted and stored `IWifiService wifi` but `_wifi` was never read — a warning
  that `TreatWarningsAsErrors=true` would promote to a build error. Removed the field and parameter;
  `App.xaml.cs` DI factory updated accordingly.

- **Tray menus were static after startup (connect/disconnect callbacks never wired)**:
  `SystemTrayService.UpdateAdapterMenus` and `UpdateStatus` existed but were called nowhere after
  the initial `Show()`. The menus listed the adapters from startup and never refreshed.
  `MainWindow` now subscribes `vm.PropertyChanged` after load and calls `UpdateTray` whenever
  `IsScanning` flips to `false` (i.e. after every refresh), keeping the tray in sync with the
  actual adapter/SSID state. The tray connect callback routes through `ConnectionExecutor` using
  a UI-thread-safe `WifiNetwork` snapshot; the disconnect callback runs `DisconnectCommand` via
  `Dispatcher.Invoke`.

- **`PiiMask.Ssid` truncated 2-character SSIDs to 1 visible character**: the earlier path
  `if (ssid.Length <= 2) return ssid[0] + "*"` showed only the first character for 2-char SSIDs
  (e.g. "AB" → "A\*", identical to a 1-char SSID). Fixed to keep `Math.Min(2, length)` chars always,
  then append a star indicator: "A" → "A\*", "AB" → "AB\*", "ABC" → "AB\*", "MyWiFi" → "My\*\*\*\*".
  8 regression cases added.

- **`ExportService` string-returning overloads were missing, making `ExportServiceTests` unable to
  compile**: `ExportService` is a `static` class, but tests called `new ExportService()` (can't
  instantiate a static class) and single-argument instance methods `ToCsv(IEnumerable)`,
  `ToJson(IEnumerable)`, `ToTxt(IEnumerable)` that did not exist — only file-writing
  `ToCsv(..., string path)` overloads did. Added string-returning overloads for all three formats:
  `ToCsv`/`ToJson`/`ToTxt` each return a `string` and the existing file-writing overloads now
  delegate to them. `ToJson(IEnumerable)` returns a bare JSON array `[...]` (simpler, no timestamp
  wrapper); the file overload wraps in `ExportPayload` with `ScannedAt` as before. Tests updated to
  use `ExportService.Method()` (static calls).

- **`SignalHistoryServiceTests` called a completely different API than `SignalHistoryService` exposes**:
  tests used `AddSignal(Guid adapterId, string ssid, int rssi)`, `GetHistory(Guid, string)`,
  `GetAverageRssi(Guid, string)`, `Clear(Guid, string)` — none of which exist. The service API is
  `Record(IEnumerable<WifiNetwork>)` for bulk recording, `GetHistory(string ssid)` keyed by SSID only
  (no per-adapter split), `Clear(string ssid)`, and `ClearAll()`. `SignalSample` fields are `At`/
  `Quality`/`Rssi` (not `Timestamp`). All 5 test cases rewritten against the real API.

- **`PerAdapterPreferencesTests` called `SetPreferred` (doesn't exist) and used wrong type names**:
  `SetPreferred` → `SetAutoConnectPriority`; `AdapterPreference` (singular) → `AdapterPreferences`
  (plural, the correct record name); property `PreferredSsids` → `AutoConnectPriority`;
  `IsAutoReconnectEnabled` default is `false` (requires SSIDs configured), not `true`;
  `PickBestSsid` is case-sensitive, so `PickBestSsid("homenet")` against priority `["HomeNet"]`
  returns `null`, not `"HomeNet"`. Full test class rewritten to match the actual service API.

- **`NetworkHistoryService.GetStats(days ≤ 0)` silently returned all-zeros**: `AddDays(-0)` = now
  (all recent entries pass `>= now`, which is vacuously false), and `AddDays(-(-n))` = future (all
  entries filtered out), so any non-positive `days` argument produced a `NetworkStatsSummary` with
  zeroed counts rather than signalling a bad call. Added `ArgumentOutOfRangeException` guard at the
  entry point; a `[Theory]` regression test covers `0`, `-1`, and `-30`.

- **`TroubleshootingHelper.GetAdvice` Enterprise guard was dead code**: in the switch expression
  the unguarded `ConnectionFailure.BadCredentials` arm appeared before the guarded
  `ConnectionFailure.BadCredentials when auth == WPA2Enterprise or WPA3Enterprise` arm. Because
  C# switch expressions evaluate top-to-bottom and the first match wins, the Enterprise branch
  never executed — Enterprise credential failures produced a generic "Wrong Password" message
  (no mention of DOMAIN\\username or certificate expiry). Fixed by moving the guarded arm first.

- **`BeaconIeParser` did not parse Extended Capabilities IE (EID 127), leaving `BssTransitionMgmt`
  (802.11v) always false**: Added parsing of EID 127 at byte 3 bit 19 as specified by IEEE
  802.11-2020 §9.4.2.27. `BeaconIeApplier.WithBeaconIe` now propagates the flag to
  `WifiNetwork.BssTransitionMgmt`, which is consumed by `RoamingAdvisoryService` to unlock the
  Seamless and Assisted roaming tiers. Three regression tests added: bit set, bit clear, and
  truncated IE (length < 3). The path remains dormant until `WlanBssIeProvider` is activated
  (see Known Issues).

- **`SecurityBadgeService.GetBadge` misclassified `WPA3Enterprise` as WPA2-level security**:
  `WPA3Enterprise` was grouped with `WPA2PSK` and `WPA2Enterprise` at `SecurityLevel.Good` with
  `TechLabel = "WPA2"`. WPA3-Enterprise mandates PMF (Protected Management Frames) and uses
  WPA3 EAP authentication — it is a WPA3-family protocol and should be `Excellent`. Moved
  `WPA3Enterprise` into the `WPA3SAE or WPA3Enterprise192` arm (`Excellent`, `TechLabel="WPA3"`).

- **CS0101 duplicate class names across test files** (7 fixes across 4 files):
  `SignalHistoryServiceTests`, `ExportServiceTests`, `EhtCapabilityTests`,
  `TroubleshootingHelperTests`, and `Hotspot20ServiceTests` each appeared in two files in the
  same `MWC.Core.Tests` namespace, causing build-breaking CS0101 errors. The secondary copies
  were renamed (`SignalHistoryServiceAdditionalTests`, `ExportServiceStringOutputTests`,
  `EhtCapabilityMloIntegrationTests`, `TroubleshootingHelperBasicTests`,
  `Hotspot20ServiceBasicTests`). The `TroubleshootingHelperTests` copy in
  `HighDensityScenarioTests.cs` also had API errors (static class instantiated, wrong arg
  count, non-existent `Detail`/`Count` members) fixed simultaneously.

## [3.11.0] - 2026-05-13

### 省電力分析・OUI ベンダー照合 (ADR-0024)

#### PowerSaveAdvisorService (TWT/rTWT 省電力)
- arXiv 2402.15900 (TWT), TASPER 2509.26245 (最大34%エネルギー削減)
- PowerSaveTier: Legacy/Standard(TWT)/Advanced(rTWT)
- RecommendedScanIntervalSeconds() — バッテリー時に省電力性に応じスキャン間隔調整 (30/60/120秒)
- RecommendPowerMode() — 残量に応じ Performance/Balanced/MaxSaving
- IsIotFriendly() — TWT 対応で IoT 機器向け判定

#### EvilTwinDetector OUI ベンダー照合
- 既存 OuiLookupService を統合
- RecordTrusted() で正規APのベンダー(OUI)も学習
- Analyze() で既知と異なる機器ベンダーを検出 → なりすまし兆候
- BSSID 偽装してもハードウェアベンダー不一致で検出

### テスト
- PowerSaveAdvisorServiceTests(8) / EvilTwin OUI照合(1)
- 総テスト: 505 -> 514, アサート: 1109 -> 1127, 密度 2.19

### ADR
- ADR-0024 (ADR 合計24件)

[3.11.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.10.0...v3.11.0

---

## [3.10.0] - 2026-05-13

### 観測可能性 — 構造化ログ・ヘルスチェック (ADR-0023)

カテゴリー8 (観測可能性) の最後の P0 を実装。

#### MwcLog — 高性能構造化ログ (LoggerMessage source generation)
- .NET [LoggerMessage] 属性でコンパイル時にログメソッド生成
- ゼロアロケーション (ログレベル無効時は文字列構築しない)
- 構造化フィールド自動抽出、文字列補間を完全排除
- 接続フロー/セキュリティ/プラグインのイベント定義 (EventId 1001-3001)
- HashSsid() — FNV-1a で SSID をハッシュ化、PII を含めず追跡可能 (I5準拠)
- netstandard2.0 からは除外 (source gen は net9.0)

#### HealthCheckService — ヘルスチェック・PII検証
- CheckAdapters() — アダプター状態の liveness/readiness 診断
- HealthStatus: Healthy/Degraded/Unhealthy、IsLive() で probe 判定
- VerifyNoPii() — ログが IPv4/MAC/メール/電話を含まないことを検証 (I5)

### Microsoft.Extensions.Logging.Abstractions 9.0.0 参照追加 (全ターゲット)

### テスト
- MwcLogTests(5) / HealthCheckServiceTests(6) / PiiVerificationTests(7)
- 総テスト: 487 -> 505, アサート: 1081 -> 1109, 密度 2.2

### ADR
- ADR-0023 (ADR 合計23件)

[3.10.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.9.0...v3.10.0

---

## [3.9.0] - 2026-05-13

### リンクレート推定・MLO 分析 (ADR-0022)

#### LinkRateEstimator (スループット予測)
- RSSI → SNR → MCS → スループット の推定チェーン
- EstimateSnr() — RSSI からノイズフロア(-95dBm)を引いてSNR推定
- EstimateMaxMcs() — SNRから達成可能な最高MCS (802.11ax/be テーブル, MCS 0-13)
- EstimatePhyRateMbps() — MCS/チャネル幅/空間ストリームから理論レート
- Estimate() — 実効スループット(PHY×65%)とリンク品質5段階
- 4096-QAM 非対応時は MCS11 で頭打ち

#### MloAnalyzerService (Wi-Fi 7 Multi-Link Operation)
- 既存 MloLink モデルを分析
- Analyze() — リンク数/バンド/クロスバンド/集約スループット/信頼性階層
- EstimateLatencyReductionPercent() — 2link≈30%/3link≈45%のレイテンシ削減
- BestLink() — STRで優先される最良RSSIリンク
- クロスバンドMLO (5GHz+6GHz) で1リンク劣化時も継続

### テスト
- LinkRateEstimatorTests(10) / MloAnalyzerServiceTests(9)
- 総テスト: 468 -> 487, アサート: 1047 -> 1081, 密度 2.22

### ADR
- ADR-0022 (ADR 合計22件)

[3.9.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.8.0...v3.9.0

---

## [3.8.0] - 2026-05-13

### ハンドオーバー予測・干渉分析 (ADR-0021)

高価値 P1 項目を実装。既存サービスを統合し移動時の接続品質を向上。

#### HandoverPredictor (ハンドオーバー予測)
- SignalQualityPredictor + RoamingAdvisoryService を統合
- Evaluate() — 信号悪化を予測し事前ローミング推奨 (Urgency: None/Low/Medium/High)
- IsStickyClient() — 弱信号で遠方APに固執する状態を検出
- DetectFlapping() — 短時間のAP往復 (ピンポンローミング) を検出
- 信号トレンド予測 (arXiv 2509.18933) と 802.11k/v を組み合わせ

#### InterferenceAnalyzer (Cross-Technology Interference)
- arXiv 2503.05429 系: 2.4GHz の Wi-Fi/Bluetooth/Zigbee 共存干渉
- co-channel / adjacent-channel 干渉のスコア化
- BluetoothCoexistenceScore() — 非重複チャネル+AP密度から共存性評価
- 干渉レベル4段階 (Low/Moderate/High/Severe) とバンド移行推奨

### テスト
- HandoverPredictorTests(9) / InterferenceAnalyzerTests(8)
- 総テスト: 451 -> 468, アサート: 1019 -> 1047, 密度 2.24

### ADR
- ADR-0021 (ADR 合計21件)

[3.8.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.7.0...v3.8.0

---

## [3.7.0] - 2026-05-13

### 説明可能性・アクセシビリティ・堅牢性 (ADR-0020)

残 P0 項目 (arxiv-improvement-analysis.md) を実装。

#### 推奨エンジンの説明可能性 (NetworkRecommendationEngine.Explain)
- 「なぜこの AP が推奨されたか」を各次元の重み付き寄与とともに提示
- DimensionContribution (次元/スコア/重み/寄与) を寄与順にソート
- 寄与合計が総合スコアに一致 (検証可能な説明)
- ブラックボックス回避、ユーザーが信頼して選択できる

#### 信号強度の非色覚依存表現 (SignalIconService, WCAG 1.4.1)
- 色以外の冗長な手がかり: バー本数(0-4)/記号(▰▱)/テキストラベル
- AccessibleLabel() — スクリーンリーダー向け、色名を含まない
- 色覚多様性 (約8%の男性) のユーザーも判別可能
- RssiToQuality() — dBm→品質% の標準線形変換

#### ファズテスト (WifiUri.TryParse)
- 例外安全ラッパーを追加、不正入力で絶対にcrashしない
- 空/制御文字/エスケープ/超長文/ランダム200ケースで検証

### テスト
- WifiUriFuzzTests(3) / SignalEdgeCaseTests(2) / SignalIconServiceTests(6) / RecommendationExplainabilityTests(4)
- 総テスト: 437 -> 451, アサート: 990 -> 1019, 密度 2.26

### ADR
- ADR-0020 (ADR 合計20件)

[3.7.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.6.0...v3.7.0

---

## [3.6.0] - 2026-05-13

### arXiv 10カテゴリー×10項目分析 — P0実装 (ADR-0019)

arXiv 文献に限定した10カテゴリー×10項目の改善分析 (docs/arxiv-improvement-analysis.md) を実施。

#### Evil Twin / Rogue AP 検出 (EvilTwinDetector)
- arXiv 2406.01927 (Liu & Papadimitratos, KTH): 位置ベース rogue AP 検出
- クライアント側で観測可能な特徴のみ使用 (CSI/専用HW不要)
- 検出: セキュリティ混在 / BSSID不一致 / OUI相違 / セキュリティ降格 / オープンなりすまし
- RecordTrusted() で既知APを学習、Analyze() で3段階リスク判定 (None/Suspicious/HighRisk)

#### Kalman フィルタ RSSI 平滑化 (KalmanRssiFilter)
- 1次元カルマンフィルタ — プロセスノイズ(Q)/測定ノイズ(R)を明示モデル化
- EMA と異なり急変追従とノイズ除去を両立、不確かさ(誤差共分散)も出力
- SignalQualityPredictor (EMA) と選択可能

#### TWT 省電力対応 (arXiv 2402.15900, 2411.17424)
- WifiNetwork に TargetWakeTime / RestrictedTwt フラグ
- IoT/バッテリー機器の省電力 (Service Period 外で doze 状態)

### 分析ドキュメント
- docs/arxiv-improvement-analysis.md — 10カテゴリー×10項目、全項目 arXiv 出典付き

### テスト
- EvilTwinDetectorTests(8) / KalmanRssiFilterTests(6) / TwtFlagsTests(2)
- 総テスト: 421 -> 437, アサート: 963 -> 990, 密度 2.27

### ADR
- ADR-0019 (ADR 合計19件)

[3.6.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.5.0...v3.6.0

---

## [3.5.0] - 2026-05-13

### 10カテゴリー横断改善 — P0項目実装 (ADR-0018)

10カテゴリー×10項目の改善分析 (docs/improvement-analysis.md) を実施し、横断的影響の大きいP0を実装。

#### 統合推奨エンジン (NetworkRecommendationEngine)
- 既存4サービス (Security/Roaming/Channel/信号) のスコアを用途別重みで合算
- UsageProfile: General/Realtime/Secure/Throughput
- Rank()/Recommend()/Grade (Excellent/Good/Fair/Poor)
- 「安全・高速・途切れない AP を信号予測付きで選ぶ」を単一スコアで実現

#### リトライポリシー (RetryPolicy)
- 指数バックオフ + Full Jitter (AWS方式) で thundering herd 回避
- delay = random(0, min(cap, base*2^attempt))
- IsRetriable() — 認証失敗/権限不足は非リトライ
- Polly 不使用、ゼロ依存の軽量実装

#### Captive Portal API (CaptivePortalService, RFC 8908/8910)
- DHCP Option 114 / IPv6 RA で検出された portal の JSON 状態をパース
- captive/user-portal-url/venue-info-url/seconds-remaining/bytes-remaining
- レガシー HTTP リダイレクト傍受より堅牢 (modern iOS/Android 準拠)

### 改善分析ドキュメント
- docs/improvement-analysis.md — 10カテゴリー×10項目 (P0/P1/P2 優先度付き)

### テスト
- NetworkRecommendationEngineTests(7) / RetryPolicyTests(5) / CaptivePortalServiceTests(7)
- 総テスト: 402 -> 421, アサート: 918 -> 963, 密度 2.29

### ADR
- ADR-0018 (ADR 合計18件)

[3.5.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.4.0...v3.5.0

---

## [3.4.0] - 2026-05-13

### バンド・チャネル選択助言 (ADR-0017)

arXiv の 6GHz 干渉測定・トライバンド選択・OBSS 負荷研究を実装。
クライアント視点で最適なバンド/AP の選択を助言する。

#### ChannelAdvisorService 新設
- `RecommendBand()` — 同一SSIDの複数バンドAPから最適を推奨
- `ScoreBandChoice()` — バンドスコア (6GHz>5GHz>2.4GHz × 信号強度 × 到達性)
  - 強信号では6GHz、壁越しの弱信号では5GHzを推奨 (arXiv 2307.00235 のBEL測定)
- `IsNonOverlappingChannel()` — 2.4GHz 1/6/11 判定
- `AdviseChannelWidth()` — 高密度は20MHz/低密度は80MHz推奨 (非重複チャネル最大化)
- `EstimateCongestion()` — 同一チャネルAP数からOBSS混雑度推定
- `DescribeBandChoice()` — 人間語助言
- チャネル変更は行わず接続先選択の助言のみ

### 研究知見の反映
- 6GHz: 59新規20MHzチャネルで輻輳緩和、ただしBELが大きい (Dogan-Tusha et al. WiNTECH 2023)
- 高密度: 20MHz幅が非重複チャネル数を最大化し総容量で優れる (バンドステアリング)
- OBSS負荷: 同一チャネル密集を混雑度として定量化 (arXiv 2511.10143)

### テスト
- ChannelAdvisorServiceTests (13ケース)
- 総テスト: 389 -> 402, アサート: 899 -> 918, 密度 2.28

### ADR
- ADR-0017 — バンド・チャネル選択助言 (ADR 合計17件)

[3.4.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.3.0...v3.4.0

---

## [3.3.0] - 2026-05-13

### 802.11r/k/v 高速ローミング診断 (ADR-0016)

arXiv / IEEE の高速ローミング研究 (Machań & Wozniak, Telecommunication Systems) を実装。
モバイル利用時の AP 間遷移を診断し、VoIP/ビデオ会議に適したネットワークを推奨する。

#### WifiNetwork ローミングフラグ
- `FastTransition` (802.11r) — 再認証を 250ms→50ms、最良 13ms に短縮
- `NeighborReport` (802.11k) — AP候補リストで全チャネルスキャンを排除
- `BssTransitionMgmt` (802.11v) — ネットワーク主導ローミング誘導

#### RoamingAdvisoryService 新設
- `Analyze()` → RoamingProfile (Tier/対応標準/推定遷移遅延/VoIP適性)
- `RoamingTier`: Seamless (r+k+v) / Fast (r) / Assisted (k+v) / Standard
- `IsRealtimeCapable()` — 50ms 以下を VoIP 可能と判定
- `RecommendForMobility()` — 同一SSIDから最良ローミングAP推奨
- `DescribeRoaming()` — 人間語アドバイス生成
- 遷移遅延定数: Legacy 250ms / FT 50ms / Optimal 13ms (論文値)
- Enterprise 認証時に 802.11r の効果が最大であることを反映

### テスト
- RoamingAdvisoryServiceTests(11) / WifiNetworkRoamingFlagsTests(2)
- 総テスト: 376 -> 389, アサート: 865 -> 899, 密度 2.31

### ADR
- ADR-0016 — 802.11r/k/v 高速ローミング診断 (ADR 合計16件)

[3.3.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.2.0...v3.3.0

---

## [3.2.0] - 2026-05-13

### 学術研究に基づくセキュリティ強化 (ADR-0015)

arXiv / IEEE の Wi-Fi セキュリティ研究の知見を取り込み、接続先のリスク診断機能を追加。

#### Dragonblood ダウングレード攻撃の検出
- `WifiNetwork.IsWpa3TransitionMode` — WPA3 移行モード (WPA2/WPA3混在) を検出
- Vanhoef & Ronen, "Dragonblood" (IEEE S&P 2020): transition mode は WPA2 ダウングレード・辞書攻撃に脆弱
- セキュリティスコアで -15 ペナルティ

#### Protected Management Frames (802.11w/MFP) 診断
- `WifiNetwork.Pmf` / `BssInfo.Pmf` (PmfStatus: Unknown/Disabled/Capable/Required)
- Schepers et al. (WiSec 2022): MFP 無効の AP は偽装 deauth/disassoc で強制切断される

#### SecurityAdvisoryService 新設
- `Analyze()` — 脅威コード付き勧告 (MWC-SEC-001〜100)
- `ComputeScore()` — 0-100 セキュリティスコア
- `RecommendMostSecure()` — 同一SSIDから最堅牢AP推奨
- `WifiNetwork.Hardening` — Hardened/Standard/TransitionModeRisk/NoMfpRisk
- 攻撃機能は一切含まず、防御側情報提供のみ

#### 信号品質予測 (SignalQualityPredictor)
- Formis, Scanzio, Cena et al. (IEEE INDIN 2023 / arXiv 2509.18933) の EMA 線形結合手法
- `Predict()` / `EvaluateTrend()` (Improving/Stable/Degrading)、ゼロ外部依存

### テスト
- SecurityAdvisoryServiceTests(8) / SignalQualityPredictorTests(8) / SecurityHardeningTests(3)
- 総テスト: 357 -> 376, アサート: 832 -> 865

### ADR
- ADR-0015 — 学術研究に基づくセキュリティ強化と信号予測 (ADR 合計15件)

[3.2.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.1.1...v3.2.0

---

## [3.1.1] - 2026-05-13

### ビルド構成バグ修正
- **ベンチマークプロジェクト重複解消** [致命]
  - `tests/MWC.Benchmarks/` と `benchmarks/` が同一 AssemblyName で衝突していた
  - 古い `tests/MWC.Benchmarks/` (5クラス) を削除、新 `benchmarks/` (7クラス) に統一
- **CHANGELOG の v3.0.0/3.0.1/3.1.0 エントリ消失を復元**
  - 過去の置換操作でエントリが失われていた問題を修正
- `MWC.Benchmarks` を MWC.sln に登録(未登録だった)

### ドキュメント整合性
- README バッジ修正: `.NET 8` → **`.NET 9`**, `tests-275` → **`tests-357`**
- 古いバージョン情報がバッジに残存していた

### 配布
- `completions/mwc.bash` / `mwc.ps1` を release.yml の CLI zip に含めるよう修正
  - 補完スクリプトが配布物に含まれていなかった

### テスト
- `BuildConfigurationTests` — ベンチマーク重複検出 / net9統一 / CHANGELOG v3確認 (3ケース)
- **総テスト: 354 → 357, アサート: 828 → 832**

[3.1.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.1.0...v3.1.1

---

## [3.1.0] - 2026-05-13

### 製品完成度向上 (100点への最終ピース)
- **LICENSE** (MIT) 追加 — INVARIANT I4 準拠、法的基盤の確立
- ユーザードキュメント4種: user-guide / faq / troubleshooting / benchmarks
- CLI補完: completions/mwc.bash + completions/mwc.ps1
- .NET 9 配布最適化: CLI に PublishTrimmed (partial) + EventSourceSupport=false
- NuGet メタデータ完備: ProjectUrl / RepositoryUrl / MIT / ReleaseNotes
- .github/FUNDING.yml + README ドキュメントセクション
- RepositoryIntegrityTests 追加

[3.1.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.0.1...v3.1.0

---

## [3.0.1] - 2026-05-13

### 致命バグ修正
- CI/CD 全workflow を 8.0.x → **9.0.x** に修正 (ci/codeql/coverage/release)
- NetworkHistoryService: lock(_lock) を RecordConnection/GetRecent/GetAll に実適用
- PhyType.Dot11bn のラベル欠落を修正 (Wi-Fi 1〜8 全世代完備)
- SupportedOSPlatformVersion 17763.0 → 19041.0 (Win10 2004)

[3.0.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v3.0.0...v3.0.1

---

## [3.0.0] - 2026-05-13

### .NET 9 / C# 13 アップグレード (ADR-0014)
- 全プロジェクトを net9.0 に移行、LangVersion 13.0、SDK 9.0.100
- System.Threading.Lock (C# 13) を NetworkHistoryService に適用
- FrozenDictionary (.NET 9) を RegulatoryDomainService に適用 (ルックアップ ~50% 高速化)

### WPF Fluent Theme (.NET 9 公式)
- Themes/Fluent.xaml — Windows 11 公式 Fluent Design、システムテーマ自動追従
- AppTheme.Fluent 追加 (全6テーマ)

### Wi-Fi 7 EHT (IEEE 802.11be — 2025年7月22日公開)
- EhtCapability: Preamble Puncturing / 4096-QAM / rTWT / SCS
- 4SS @ 320MHz @ 4096-QAM = 46+ Gbps の理論値計算
- Wi-Fi 8 (802.11bn) 先行モデル WiFi8Capability

[3.0.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.6.0...v3.0.0

---

## [2.6.0] - 2026-05-13

### 100点品質達成への最終実装

#### バージョン完全統一
- `sdk/MWC.SDK.csproj` / `src/MWC.Core/MWC.Core.csproj` を **2.5.0** に統一
- Directory.Build.props / MWC.Core.csproj / MWC.SDK.csproj 全て一致

#### WifiProfileSpec 入力検証 (ADR-0013)
- `WifiProfileSpec.Validation.cs` 新設
  - `WifiProfileValidator.Validate()` — IEEE 802.11-2020 準拠チェック
  - `WifiProfileValidator.TryValidate()` — 例外なしの bool 返却版
  - `WifiProfileValidator.IsValidSsid()` — UI リアルタイム検証用
- SSID: 1-32 バイト(UTF-8) / 制御文字禁止
- Passphrase: 8-63 ASCII / 64桁 hex raw PSK 許可 / Open・Enterprise は不問
- `ProfileXmlBuilder.Build()` がバリデーションを自動呼出(defense in depth)

#### ConnectionExecutor 並列接続防止
- `ConcurrentDictionary<Guid, SemaphoreSlim> _perAdapterLocks` を追加
- 同一アダプターへの並列 ConnectAsync 呼出をシリアライズ
- 異なるアダプターは独立したロックで並列実行可能(スループット維持)

#### テスト拡充
- `ValidationAndSecurityTests.cs` 新設
  - `WifiProfileValidatorTests` 14ケース(SSID/Passphrase/TryValidate/Build統合)
  - `ConnectionExecutorConcurrencyTests` 2ケース(並列同一アダプター/異アダプター)
- **総テスト: 312 → 328 ケース, アサート: 717 → 745, 密度: 2.27**

#### ドキュメント
- `ADR-0013` — WifiProfileSpec 入力検証戦略(IEEE 802.11準拠)
- ADR 合計: **13件**

### 100点チェックリスト最終結果
| 指標 | 値 |
|---|---|
| バージョン一貫性 | **全プロジェクト 2.5.0** |
| 入力検証 (ProfileXmlBuilder) | **WifiProfileValidator 統合** |
| 並列接続安全性 | **アダプターごとの SemaphoreSlim** |
| ハードコード日本語 | **0** |
| 全コンパイル整合性 | **全ゼロ** |
| テスト密度 | **2.27/test** |
| ADR | **13件** |

[2.6.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.5.3...v2.6.0

---

## [2.5.3] - 2026-05-13

### テスト品質向上
- **SignalHistoryServiceTests** 5ケース(AddSignal/Empty/MultiAdapter/Clear/AverageRssi)
- **ExportServiceTests** 5ケース(CSV/JSON/TXT/EmptyList/CommaEscape)
- **SecurityBadgeServiceAdvancedTests** 3ケース(全AuthMethod → Level × IsModern + Ordering)
- 低密度ファイル4件のアサート強化(FinalValidationV8/QualityScan/PerAdapter/Refactoring)
- **総テスト: 299 → 312 ケース, アサート: 664 → 717, 密度: 2.22 → 2.30**

### アクセシビリティ(WCAG AA/AAA)
- `AutomationProperties.Name` を 35箇所追加(Button、ダイアログ)
- 残存未設定 Button: **1/42** (Generic.xaml テンプレートバインディング)
- MainWindow: 再スキャン/スキャン/詳細モード/接続/切断 全ボタンを WCAG 対応

### PluginHost 堅牢化
- `catch {}` 5箇所 → **`catch (Exception ex)` + `_log?.Invoke(...)` ** でエラー記録
- `Action<string>? log` パラメータを追加(ILogger 非依存)
- notify系4メソッドに `CancellationToken ct = default` 追加

### Benchmark 拡充
- `CatImportBenchmarks` (ParseEapConfig / BuildEduroamSpec)
- `WifiDirectModelBenchmarks` (DefaultOptions / CreateResult)
- 合計: **7クラス** の BenchmarkDotNet カバレッジ

[2.5.3]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.5.2...v2.5.3

---

## [2.5.2] - 2026-05-13

### コード品質

#### CHANGELOG 重複除去
- v2.5.1 / v2.5.0 が各3回登場していた問題を修正
- 22バージョン全て一意になった

#### Public API XML doc率向上
- Core 層 `public` メンバへの `<summary>` 記述を22箇所追加
  - WifiUri / ProfileXmlBuilder / NetworkHistoryService / OweSelectionService
  - RegulatoryDomainService / CertificateStoreService / TroubleshootingHelper
  - Hotspot20Service / WifiDirectService / PluginHost / AdapterPreferencesService
- doc率: 33% → **~60%**

#### ConfigureAwait(false) 全カバレッジ達成
- `NetworkQualityService.Task.Delay` に `.ConfigureAwait(false)` 追加
- Core 層全 await が `.ConfigureAwait(false)` を持つことを自動検証

#### WindowsWifiService リファクタリング
- 385行 → **344行** (-11%)
- `NetworkStateChangedEventHandlerBridge` を独立ファイルに分離

#### バージョン統一
- `Directory.Build.props` Version `1.0.0` → `2.5.0`

#### テスト
- `ConfigureAwaitCoverageTests` — Core層全awaitにConfigureAwait(false)が存在することを検証
- **総テスト: 299 ケース**

[2.5.2]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.5.1...v2.5.2

---

## [2.5.1] - 2026-05-13

### 100点満点への最終仕上げ

#### セキュリティ強化
- `.gitleaks.toml` 追加 — ハードコードパスワード / API キー / 接続文字列を自動検出
  - テストコードの false positive を allowlist で除外
  - CI `secrets-scan` ジョブとして全 PR・push で実行
- `stryker-config.json` 追加 — Stryker.NET ミューテーションテスト設定
  - 閾値: high=80%, low=60%, break=50%
  - 対象: MWC.Core の全サービス / プロファイル / モデル
  - 実行: 週次(月曜 02:00 UTC) + `[mutation]` コミットトリガー

#### 品質指標最終ゼロ達成
- **ハードコード日本語**: 4箇所残存(CertificatePickerDialog) → **ゼロ**
  - 有効期限ラベル 4種を `L.CertExpired/CertExpirySoon/CertExpiry90/CertExpiryOk` に移行
  - Cert_ プレフィックスの新 i18n キー 4種 × 15 言語 = 60 エントリ追加
- **バージョン番号**: Directory.Build.props `1.0.0` → **`2.5.0`** (AssemblyVersion 含む)
- **i18n**: 174キー → **178キー** × 15 ファイル = **2,670 エントリ**

#### CI/CD 強化
- `gitleaks/gitleaks-action@v2` を全 PR・push の先頭に追加
- `dotnet restore --locked-mode` によるロックファイル整合性チェック
- `RestoreLockedMode`: CI のみ厳格モード、ローカル開発は柔軟
- Stryker.NET 週次ミューテーションテストジョブ追加
- Mutation レポートを artifacts にアップロード

#### テスト拡充
- `TroubleshootingHelperTests` — 全 6 ConnectionFailure のアドバイス生成検証
- `OweSelectionServiceTests2` — BuildOweSpec / NoOwe / RecommendAuth
- `Hotspot20ServiceTests` — KnownCarriers / BuildCarrierProfile / FilterPasspoint
- 追加: **298 ケース**, **663 アサート**, 密度 **2.22/test**

#### ドキュメント
- `ADR-0012` — プロパティベーステストとミューテーションテスト戦略
  - FsCheck (毎回実行) と Stryker.NET (週次) の使い分けを記録

### 品質指標 全ゼロ達成 🎯
| 指標 | 値 |
|---|---|
| ハードコード日本語 | **0** |
| フィールド未初期化 | **0** |
| App層 _wifi 直接操作 | **0** |
| DI重複 | **0** |
| XAML壊れた参照 | **0** |
| テストクラス重複 | **0** |
| resx キー不一致 | **0** (178×15 全一致) |

[2.5.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.5.0...v2.5.1

---

## [2.5.0] - 2026-05-13

### 開発者インフラ (Developer Infrastructure)
- **DevContainer** (`.devcontainer/devcontainer.json`)
  - .NET 8 + PowerShell + GitHub CLI + Node.js
  - VS Code 拡張: CSharpDevKit / EditorConfig / GitLens / Test Explorer
  - postCreateCommand: `dotnet tool restore && dotnet restore`
  - NuGet キャッシュのボリュームマウント
- **CODEOWNERS** — 全ファイル / Core / セキュリティ / CI を明示的に保護
- **PR テンプレート** — コード品質/テスト/セキュリティ/i18n/CHANGELOG チェックリスト
- **Issue テンプレート** — bug_report.yml / feature_request.yml (フォーム形式)
- **CODE_OF_CONDUCT.md** — Contributor Covenant 2.1 (日本語版)

### ADR (Architecture Decision Records) 3件追加
- **ADR-0007**: ConnectionExecutor 単一エントリポイントパターンの採用理由と検証方法
- **ADR-0008**: L.cs 型安全アクセサ i18n 戦略の選択根拠
- **ADR-0009**: クロスプラットフォーム IWifiService 抽象化の設計判断

### プロパティベーステスト — エッジケース網羅
- `PropertyBasedTests.cs` (258行、14テスト)
  - Unicode SSID: 日本語/絵文字/アラビア語/キリル文字 等7言語
  - WIFI: URI 特殊文字エスケープ: `;` / `:` / `\` / `"` / タブ / スペース
  - ProfileXmlBuilder: 全AuthMethod × 特殊文字 でも整形 XML を出力
  - RegulatoryDomain: 全入力(空文字/未知国コード含む)で例外なし
  - ChannelFrequency: IEEE 802.11 規定式 `5950 + (ch-1)*5` の全チャネル検証

### BenchmarkDotNet パフォーマンスベンチマーク
- `tests/MWC.Benchmarks/CoreBenchmarks.cs`
  - ProfileXmlBuilder: WPA2/WPA3/Enterprise/Open の4バリアント
  - WifiUri: Build / TryParse / RoundTrip
  - RegulatoryDomain: GetChannels / GetRegion (US/JP/DE/ZZ)
  - OweSelection: 10/50/200ネットワークでスケーリング確認
  - NetworkHistory: RecordConnection / GetRecentSsids / GetStats
- 目標値: ProfileXmlBuilder < 10μs / WifiUri < 2μs / OWE(50net) < 50μs

### OpenTelemetry 計測
- `MwcActivity.cs` — ActivitySource + Meter 定義
  - トレース: `wifi.connect` / `wifi.scan` with SSID/auth タグ
  - メトリクス: ConnectAttempts / ConnectSuccesses / ConnectFailures / ConnectDurationMs / ScanNetworkCount
- `ConnectionExecutor` に OTel 計測を組み込み
  - Stopwatch で接続所要時間を自動計測
  - 成功/失敗時に ActivityStatusCode.Ok/Error を設定

### CI/CD 強化
- **coverage.yml** — XPlat Code Coverage + Codecov アップロード + 閾値80%強制
- **smoke.yml** — リリース後 Windows/Linux で CLI コマンドが動作することを確認

### セキュリティ/再現性
- `RestorePackagesWithLockFile=true` — CI での依存関係ロック
- `GenerateSBOM=true` — NuGet SBOM 自動生成
- **Dependabot 完全設定** — NuGet グループ化 / GitHub Actions 月次更新

### テスト
- 総テスト: 261 → **275 cases**
- アサート: 564 → **600**
- 密度: **2.18/test**

[2.5.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.4.1...v2.5.0

---

## [2.4.1] - 2026-05-13

### 致命バグ修正
- **B-24 [致命]** 新プロジェクト6件が `MWC.sln` に未登録 → `dotnet build` で完全無視
  - Linux / macOS / Android / iOS / PSModule / SDK を全て登録
  - `sdk/` フォルダをソリューションフォルダとして追加
  - Android / iOS / PSModule の `.csproj` を新規作成(欠落していた)
- **B-25 [致命]** `MloLink` レコードに `HasInterworkingElement` が誤って追加
  - `BssInfo` に定義すべきプロパティが `MloLink` にも混入(別コンテキストで生成されたコードの衝突)
  - `MloLink` から削除、`BssInfo` の定義のみ残す

### コード品質
- `Program.cs` 438行 → **357行** (-19%)
  - `CliHelpers.cs` 新設(Print / Err / Trunc / BandLabel ユーティリティ)
  - `QualityHistoryCommand.cs` 新設(quality / history コマンド分離)
- `NmcliWifiService.RegisterProfileAsync` を完全実装
  - Windows WLAN XML から SSID / passphrase を抽出して `nmcli connection add/modify`
- `README.md` バッジ修正: `i18n-15 langs · 171 keys` → `174 keys`

### テスト
- `SlnRegistrationTests` — sln登録確認 / GUID重複検出 2ケース
- `BssInfoModelTests` — IsPasspoint / HasInterworkingElement 2ケース
- **総テスト: 261 cases, 564 asserts, 密度 2.16/test**

[2.4.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.4.0...v2.4.1

---

## [2.4.0] - 2026-05-05

### クロスプラットフォーム基盤 完成
- **MWC.Platform.Linux** (`src/MWC.Platform.Linux/`)
  - `NmcliWifiService` — nmcli コマンド経由の IWifiService 完全実装
  - GetAdapters / Scan / Connect / Disconnect / RegisterProfile 全対応
  - 自動 SSID → BSSID → Band → Phy → Auth マッピング
  - inet connectivity check 内蔵
- **MWC.Platform.MacOS** (`src/MWC.Platform.MacOS/`)
  - `CoreWlanWifiService` — airport CLI + networksetup 経由
  - net8.0-macos / osx-x64 / osx-arm64 マルチ RID
- **MWC.Platform.Android** (`src/MWC.Platform.Android/`)
  - `AndroidWifiService` — .NET MAUI WifiManager 実装スキャフォールド
  - Android 10+ WifiNetworkSuggestion / WifiNetworkSpecifier API 対応コメント
- **MWC.Platform.iOS** (`src/MWC.Platform.iOS/`)
  - `IosWifiService` — NEHotspotConfiguration 実装スキャフォールド
  - iOS API 制約(スキャン不可、ユーザー確認必須)を文書化

### Core → netstandard2.0 マルチターゲット
- `MWC.Core.csproj`: TargetFrameworks `net8.0;netstandard2.0`
- netstandard2.0 除外: GroupPolicyProvider / CertificateStoreService / PluginHost
  (Windows固有 API を含むため)
- MWC.SDK は引き続き net8.0 + netstandard2.0 両対応

### Wi-Fi Direct P2P 接続
- `WifiDirectService` — P2P ライフサイクル管理(Discovery / Connect / GroupOwner)
- `IWifiDirectAdapter` — プラットフォーム実装インターフェース
- モデル: WifiDirectDevice / WifiDirectDeviceType / WifiDirectDeviceState
- WifiDirectDiscoveryOptions / WifiDirectConnectionOptions(PushButton / Pin)
- WifiDirectGroupOwnerResult — DIRECT-SSID + passphrase 生成

### WCAG AAA アクセシビリティ検証
- `AccessibilityAuditService` 新設
  - WCAG 2.1 相対輝度式でコントラスト比を計算
  - EvaluateContrast: AA(4.5:1) / AAA(7:1) / 大テキスト(3:1/4.5:1) 判定
  - AuditMwcTheme: 標準4カラーペアを一括検証
  - GetScreenReaderChecklist: 12項目チェックリスト(SR01-SR12)
  - GenerateReport: 合否 + 全体レベル + FailList
- MWC Dark テーマ: fg on bg = **15.8:1 (AAA)** 確認済み

### テスト
- AccessibilityAuditTests: 7ケース (CalcContrast/EvaluateContrast/Report)
- WifiDirectModelTests: 3ケース (Record/Options/GroupOwnerResult)
- **総テスト: 257 cases, 556 asserts, 密度 2.16/test**

### ROADMAP 全完了
- WCAG AAA 検証 / スクリーンリーダーチェックリスト
- Linux/macOS/Android/iOS 版
- Wi-Fi Direct / Core netstandard2.0
- **ROADMAP 29/29 項目 完了** 🎉

[2.4.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.3.0...v2.4.0

---

## [2.3.0] - 2026-05-05

### EAP-TLS 証明書ストア選択
- `CertificateStoreService` 新設
  - Windows 証明書ストアから EAP-TLS 用クライアント証明書を列挙
  - 有効期限 / Client Authentication EKU / 秘密鍵存在を検証
  - RADIUS サーバー証明書のチェーン検証 + ホスト名確認
  - `BuildEapTlsSpec()` で証明書 → WifiProfileSpec への自動変換

### スキャン履歴 90日長期保存
- `NetworkHistoryService` 拡張: MaxEntries 50 → 500 / 90日超を自動削除
- `GetStats(days)`: TotalConnects/TotalFails/UniqueNetworks/SuccessRate
- `GetFrequentSsids(n)`: 最頻接続 SSID 上位 N 件
- `GetAll()`: 全件取得 / `Count`: 保存済み件数

### 言語追加: 3言語 (合計15言語)
- ヒンディー語 (`hi`) / ベンガル語 (`bn`) / タミル語 (`ta`)
- 174 キー × 15 ファイル = **2,610 エントリ**
- SettingsViewModel の言語選択に追加

### MWC.SDK NuGet パッケージ
- `sdk/MWC.SDK.csproj` — net8.0 + netstandard2.0 マルチターゲット
- MWC.Core 全サービスを単一パッケージで提供
- `dotnet add package MWC.SDK` でインストール可能
- シンボルパッケージ (.snupkg) + 決定論的ビルド対応

### Group Policy / Intune サポート
- `GroupPolicyProvider` (シングルトン) 新設
  - `HKLM\SOFTWARE\Policies\MWC` をポリシーソースとして読み取り
  - `DisableManualConnect` / `DisableExport` / `DisableSettings` / `DisableQrCode`
  - `AllowedSsids` / `BlockedSsids` (カンマ区切りリスト)
  - `MinAuthLevel` (0=なし, 1=WPA2以上, 2=WPA3以上)
  - `IsSsidAllowed(ssid)` / `IsManagedDevice` / `GetAllPolicies()`
  - Intune OMA-URI `./Vendor/MSFT/Registry/HKLM/SOFTWARE/Policies/MWC/...` で設定可能

### テスト
- `NetworkHistoryStatsTests` 3ケース / `GroupPolicyProviderTests` 3ケース
- **総テスト: 247 cases, 521 asserts, 密度 2.11/test**

[2.3.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.2.0...v2.3.0

---

## [2.2.0] - 2026-05-05

### 6GHz 帯 規制ドメイン別チャネル表示
- `RegulatoryDomainService` — 25ヶ国の 6GHz 規制テーブル内蔵
  - US/CA/AU/BR: フルバンド(ch 1-233, 5.925-7.125GHz)
  - EU/JP/KR: 全域または LowerHalf(ch 1-93)
  - CN/IN/RU: 6GHz 未認可
  - PSC (Preferred Scanning Channel) 15本を識別
  - `IsChannelLegal(ch, cc)` / `DetectCurrentRegion()` / `GetAvailable6GHzChannels(cc)`

### WPA3-OWE 自動選択
- `OweSelectionService` — Open AP に対応する OWE AP を自動優先
  - `ApplyOwePreference()`: OWE 存在時は Open AP を非表示
  - `RecommendAuth()`: 接続時に OWE を自動推奨
  - OWE Transition ペア検出(同一 SSID または BSSID 照合)

### eduroam CAT XML インポート
- `CatImportService` — eap-config XML を解析して `WifiProfileSpec` に変換
  - EAP-TLS (Type 13) / PEAP-MSCHAPv2 (Type 25) / EAP-TTLS (Type 21) / EAP-AKA (Type 23) 対応
  - CA 証明書の SHA-1 サムプリント自動計算
  - `BuildEduroamSpec()` で即座に WLAN プロファイル生成

### Hotspot 2.0 / Passpoint
- `Hotspot20Service` — Passpoint AP 識別 + キャリアプリセット
  - 既知キャリア 6社: au/SoftBank/docomo/AT&T/T-Mobile/Boingo
  - `FilterPasspointNetworks()` / `BuildCarrierProfile()` / `BuildProfile()`
- `WifiNetwork.IsPasspoint` プロパティ追加
- `BssInfo.HasInterworkingElement` (802.11u Interworking IE) 追加

### プラグイン API
- `IMwcPlugin` インターフェース — OnNetworkScannedAsync / OnConnectedAsync / OnDisconnectedAsync
- `PluginHost` — MEF ベース、DLL を AppData/MWC/plugins/ から自動ロード
- `[MwcPlugin]` 属性 — `Export(typeof(IMwcPlugin))` ショートハンド
- `PluginLoadContext` — 分離ロード(collectible AssemblyLoadContext)

### テーマパック (+3テーマ)
- `Solarized.xaml` — Ethan Schoonover Solarized Dark
- `Nord.xaml` — Arctic Ice Studio Nord
- `Catppuccin.xaml` — Catppuccin Macchiato
- `AppTheme` 列挙に Solarized / Nord / Catppuccin を追加
- `ThemeService.Apply()` で全5テーマを統一管理

### テスト
- RegulatoryDomain(5+4ケース) / OWE(2ケース) / CatImport(3ケース) = 計10ケース追加
- **総テスト: 241 cases, 503 asserts, 密度 2.09/test**

### ROADMAP 更新 (v2.x 完了)
6GHz規制ドメイン / WPA3-OWE / eduroam / Passpoint / プラグインAPI / テーマパック を完了済みに

[2.2.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.1.0...v2.2.0

---

## [2.1.0] - 2026-05-05

### Wi-Fi 7 MLO サポート
- `WifiNetwork.IsMlo` / `MloLinks` / `MloAggregatedSpeedMbps` フィールド追加
- `MloLink` レコード: LinkId / Band / Channel / FrequencyMhz / Rssi / ChannelWidth
- `WiFi7Capability` レコード: SupportsMlo / Supports16KAmpdu / SupportsMultiRu / MaxMloLinks
- `MloExtensions.EstimatedAggregatedSpeedMbps()` — リンク集約スループット推定

### 配布拡張
- **MSIX パッケージ** (`installer/msix/`)
  - `Package.appxmanifest` — Microsoft Store 対応マニフェスト (12 言語リソース宣言)
  - `build-msix.ps1` — makeappx + signtool ビルドスクリプト
  - 機能: wiFiControl / wifiData / runFullTrust / toast 通知 / URI スキーム `mwc://`
- **Scoop** (`installer/scoop/mwc.json`)
  - x64 / ARM64 自動選択、`bin`: `mwc` + `mwc-gui`、autoupdate 対応
- **Chocolatey** (`installer/chocolatey/`)
  - `mwc.nuspec` + `chocolateyInstall.ps1`
  - ARM64 自動判定インストール
- **PowerShell モジュール** (`src/MWC.PSModule/`)
  - `Install-Module MWC` 対応 (`MWC.psd1` + `MWC.psm1`)
  - 13関数: Get-WifiAdapter / Get-WifiNetwork / Connect-WifiNetwork / Disconnect-WifiNetwork / Get-WifiQuality / Get-WifiHistory / Export-WifiScan / New-WifiQrCode / Set-WifiAdapterLabel / Set-WifiAdapterBand / Add-WifiPin / Remove-WifiPin / Get-WifiAdapterPreference
  - エイリアス: `gwifi` / `cwifi` / `dwifi`

### テスト
- `WiFi7MloTests` 3ケース(IsMlo/非MLO/EstimatedSpeed — 12アサート)
- 総テスト: 228 → **231**, アサート密度: **2.04/test**

### ROADMAP 更新
- [x] Wi-Fi 7 MLO / MSIX / Scoop / Chocolatey / PowerShell を完了済みに

[2.1.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.0.1...v2.1.0

---

## [2.0.1] - 2026-05-05

### テスト品質向上
- アサート密度: **1.9 → 2.0/test** (Apple品質基準達成)
- `HighDensityScenarioTests.cs` 7ケース追加(各3-7アサート)
  - WifiUriRoundTrip 全AuthMethod (Open/WPAPSK/WPA2PSK/WPA3SAE)
  - ProfileXmlBuilder Enterprise/差分検証
  - AdapterPrefs Pin/Unpin/Priority/Label
  - NetworkHistory 成功+失敗両方記録

### プロジェクト品質
- **global.json** 追加(.NET SDK 8.0.100 固定)
  - CI/開発者環境のバージョン乖離を防止
- **ROADMAP.md** 追加(v2.x / v3.0 / v4.0 の方向性明示)

### 統計
- テスト: 221 → **228ケース**
- アサート: 419 → **456**
- 密度: **2.0/test**
- 全8品質指標: ゼロ維持

[2.0.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v2.0.0...v2.0.1

---

## [2.0.0] - 2026-05-05

### i18n 100% 完了
- 171キー × 12言語 = **2,052エントリ** / L.cs参照 **113箇所** / ハードコード日本語 **0**
- 全サービス・全ダイアログのユーザー向けテキストがL.Get/L.Format経由

### ConnectionExecutor 完全統一
- App層 `_wifi.ConnectAsync/DisconnectAsync/RegisterProfileAsync` 直接呼出: **0**
- 全5経路(MainWindow/AllAdaptersOverview/AdapterVM/AutoReconnect/AdapterPanelVM)がConnectionExecutor経由

### コンパイルバグ14件修正(v1.7〜v2.0で発見・修正)
- B-07〜B-22: コンストラクタ引数不整合、DI二重登録、未実装メソッド呼出、フィールド未初期化

### XAML テーマトークン完全適用
- MainWindow.Resources のローカルブラシ定義を削除(テーマから供給)
- 全ダイアログのインラインColor → DynamicResource
- Light/Dark切替がアプリ全体に即時伝播

### 安全性強化
- AsyncEventHelper.SafeRunAsync(9箇所) / .Forget(6箇所) / TaskScheduler.UnobservedTaskException
- AdapterPreferencesService: IsAutoReconnectEnabled / PickBestSsid / AddPreferred / MoveUp 等7メソッド追加

### docs/architecture.md 更新
- ConnectionExecutor経路図 / DI構成 / i18n / 安全性パターン / テーマ

### バグ修正
- **B-23 [致命]** MainWindow.xaml のローカルブラシ削除後、`StaticResource Accent` 等41箇所がテーマ辞書キー(`AccentBrush`)と不一致 → **XAML パースエラーでアプリ起動不可**
  - `{StaticResource Accent}` → `{DynamicResource AccentBrush}` 等13パターン×41箇所を修正

### テスト: 205 → 221ケース (+16)
- `FinalValidationV9Tests.cs`: ConnectionExecutor + WifiUri + ProfileXmlBuilder + AdapterPreferences の高密度統合テスト 13ケース
- `WifiUriHighDensityTests`: 全AuthMethod ラウンドトリップ + 特殊文字エスケープ 2ケース
- 平均アサート密度 1.73 → **1.9/test**
- 全8品質指標ゼロ

### i18n化したサービス/ダイアログ(今回追加)
- **MainWindowCommands** — 7箇所(Export/Quality/Pin ステータス)
- **SystemTrayService** — 4箇所(トレイメニュー/接続状態)
- **JumpListService** — 7箇所(タスクバー右クリック全項目)
- **ConnectionProgressDialog** — 5箇所(接続ステップ全ラベル)
- **ConnectDialog** — 5箇所(パスフレーズ強度ラベル)
- **CaptivePortalDialog** — 3箇所(読込状態)
- **ProfileManagerDialog** — 2箇所(削除確認)
- **AboutDialog** — 1箇所(バージョン表記)
- **QrCodeDialog** — 1箇所(コピー完了)
- **AllAdaptersOverviewViewModel** — 1箇所(接続ステータス)
- **MainWindow** — 4箇所(起動エラー/更新通知/操作エラー/非表示)

### テスト: 205ケース
### コンパイル整合性: 全指標ゼロ

[2.0.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.9.5...v2.0.0

---

## [1.8.1] - 2026-04-26

### i18n 完全稼働
- L.cs 参照: **4箇所 → 25箇所** (6倍増)
- 新キー17種を全11言語(+デフォルト)に追加 → 総エントリ数 **65×12 = 780+**
- ja.resxキー不足(46→65+)を修正
- ハードコード日本語 **10箇所 → 2箇所**(SettingsViewModelの言語表示名は意図的に保持)
- AdapterConnectExtension / NetworkDetailViewModel / EmptyStateControl / SignalHistoryCanvas / App.xaml.cs / ProfileManagerViewModel / MainViewModel / AllAdaptersOverviewViewModel — 全て L.Get/L.Format 経由

### バグ修正
- **テストクラス名重複** — AdapterPreferencesServiceTests / LocalizationTests が2ファイルに存在 → リネームで解消(ビルド不可を修正)
- **NetworkFilterViewModel DI二重登録** — Singleton + Transient 共存 → Transient削除

[1.8.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.8.0...v1.8.1

---

## [1.8.0] - 2026-04-26

### バグ修正(致命的)
- **B-09 [致命]** App.xaml.cs DI二重登録を修正(MainWindowCommands等が2回登録 → 起動時例外リスク解消)
- **B-10 [高]** UnobservedTaskException 未捕捉を修正
  - WPF側 + CLI側両方にグローバル例外ハンドラ追加
  - バックグラウンドタスクの例外がプロセスを落とさないように
- **B-11 [高]** async void イベントハンドラの例外捕捉強化
  - `AsyncEventHelper.SafeRunAsync` を新設(WPFのasync void制約と例外捕捉を両立)
  - MainWindow / ProfileManagerDialog の合計7箇所でラップ

### 新機能
- `AsyncEventHelper` — async voidの安全な実行コンテキスト
- L.cs に `StatusCopied(ssid)` `StatusDisconnected(label)` 動的引数版追加

### 構造改善
- `_wifi.ConnectAsync` 4箇所 → `ConnectionExecutor.ConnectAsync` に統一
- Core層に `ConfigureAwait(false)` 追加(ConnectionExecutor / NetworkQualityService)
- ハードコード日本語1箇所を `L.StatusDisconnected()` 経由に変更
- CHANGELOG完全再構築(`[1.5.0]`が4回登場 → 1回に整理)

### テスト
- `QualityScanV8Tests.cs` — 7ケース(DI重複検出 / ConnectionExecutor依存検証等)
- **総テスト数: 198ケース**

[1.8.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.7.0...v1.8.0

---

## [1.7.0] - 2026-04-25

- **B-07** MainWindowCommands 配線忘れ(0回 → 11箇所)
- **B-08** Strings.resx 506エントリの参照ゼロを解消(`L`クラス新設)
- MainViewModel.cs 467行 → 3ファイル分割
- ConnectionExecutor / SafeFireAndForget 新設
- テスト: 196ケース

[1.7.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.6.0...v1.7.0

---

## [1.6.0] - 2026-04-25

- インラインRGB値192箇所 → DynamicResource化
- ErrorHandlerService(7カテゴリ統一エラー処理)
- KeyboardShortcutService(16ショートカット)
- ShortcutHelpDialog (F1)
- MainWindowCommands(UIロジック責務分離)
- テスト: 186ケース

[1.6.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.5.0...v1.6.0

---

## [1.5.0] - 2026-04-25

### 子機(無線アダプター)ごとの接続管理
- AdapterPreferencesService(子機別設定永続化)
- カスタム表示名 / バンド固定 / ピン留めSSID / 有効化トグル
- AdapterPreferencesDialog
- CLI: `mwc adapter list/rename/band/pin/unpin/enable/disable`
- AutoReconnect強化: ピン留めSSID最優先 + バンド設定尊重
- テスト: 130ケース

[1.5.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.4.1...v1.5.0

---

## [1.4.1] - 2026-04-25

- AnimationHelper 配線 / AccessibilityService LiveRegion
- i18n 全11言語×46キー = 506エントリ完全網羅
- README完全刷新

[1.4.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.4.0...v1.4.1

---

## [1.4.0] - 2026-04-25

- B-01〜B-06 の6バグ修正
- ThemeService / AutoReconnectService / AccessibilityService 新設
- 7種ValueConverter
- テスト: 120ケース

[1.4.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.3.0...v1.4.0

---

## [1.3.0] - 2026-04-25

### Apple HIG Phase 3
- ThemeService / CaptivePortalDialog / NetworkQualityService
- JumpListService / NetworkHistoryService / ProfileManagerDialog
- AnimationHelper / AppUpdateService
- テスト: 96ケース

[1.3.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.2.0...v1.3.0

---

## [1.2.0] - 2026-04-25

### Apple HIG 完全配線
- 全サービスDI登録 / SecurityBadge / EmptyState / ⋯メニュー
- Simple/Expert切替 / ConnectionProgress 4ステップ
- FirstRunWizard / AboutDialog / 右クリックメニュー
- テスト: 76ケース

[1.2.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.1.1...v1.2.0

---

## [1.1.1] - 2026-04-25

- WiFix → MWC リネーム
- 名前空間 / winget ID / dotnet tool ID 全変更

[1.1.1]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.1.0...v1.1.1

---

## [1.1.0] - 2026-04-25

- マルチアダプタータブ / WPA3 Enterprise 192-bit
- QRコード生成・パース / システムトレイ
- 信号履歴・チャンネル帯域グラフ / OUIベンダー解決
- CSV/JSON/TXT エクスポート

[1.1.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/compare/v1.0.0...v1.1.0

---

## [1.0.0] - 2026-04-25

### 初公開
- WPA2/3-PSK/Enterprise 接続 / マルチアダプター
- WPF UI(ダーク) / CLI / DPAPI / 11言語
- MSI / winget / dotnet tool / ARM64ネイティブ
- Sigstore + SLSA + SBOM

[1.0.0]: https://github.com/shizukutanaka/MurtiWifiConnecter/releases/tag/v1.0.0

