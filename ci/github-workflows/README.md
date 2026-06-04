# GitHub Actions workflows (manual install)

These two workflows back the **CI** and **CodeQL** badges in the top-level
`README.md`. They live here instead of under `.github/workflows/` because the
automation account that generated this branch lacks the GitHub `workflows`
permission and cannot push files into `.github/workflows/`.

To activate them, copy both files into `.github/workflows/` and commit:

```bash
mkdir -p .github/workflows
cp ci/github-workflows/ci.yml      .github/workflows/ci.yml
cp ci/github-workflows/codeql.yml  .github/workflows/codeql.yml
git add .github/workflows
git commit -m "Add CI and CodeQL workflows"
```

## What they do

- **ci.yml** — On `windows-latest`, restores/builds/tests `MWC.ci.slnf` in
  Release (the full solution minus `MWC.Platform.MacOS`, which is `net9.0-macos`
  and cannot build on a Windows runner). A second `ubuntu-latest` job builds the
  platform-agnostic libraries (`MWC.Core`, `MWC.Platform.Linux`) to catch
  netstandard2.0 / Linux regressions early. The test project is
  `net9.0-windows` and references `MWC.App` (WPF), so tests run only on Windows.
- **codeql.yml** — CodeQL `csharp` analysis on `windows-latest` with
  `build-mode: manual` (an explicit `dotnet build` of `MWC.ci.slnf`).

Both read the .NET SDK version from `global.json` via `actions/setup-dotnet@v4`.

## First-run notes

- `Directory.Build.props` sets `TreatWarningsAsErrors=true` with
  `AnalysisMode=AllEnabledByDefault`; the first Release build on CI may surface
  analyzer warnings as errors. Triage with `-p:TreatWarningsAsErrors=false`.
- `GenerateSBOM=true` requires the `Microsoft.Sbom.Targets` tooling; if it is
  not restorable in CI, gate `GenerateSBOM` behind a condition.
- The test project enforces 80% coverage (`<Threshold>80</Threshold>`); a drop
  below that fails `dotnet test` by design.
