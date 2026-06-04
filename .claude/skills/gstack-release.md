# Skill: gstack-release

## MWC リリース手順

### 1. CHANGELOG.md 更新
`[Unreleased]` → `[x.y.z] - YYYY-MM-DD` に変更

### 2. バージョン更新
`Directory.Build.props`:
```xml
<Version>1.5.0</Version>
<AssemblyVersion>1.5.0.0</AssemblyVersion>
<FileVersion>1.5.0.0</FileVersion>
```

### 3. CI が通ることを確認
```powershell
dotnet restore MWC.sln
dotnet build   MWC.sln -c Release -warnaserror
dotnet test    MWC.sln              # 120ケース全パス
dotnet format  MWC.sln --verify-no-changes
```

### 4. タグ → 自動リリース
```powershell
git tag v1.5.0
git push --tags
# → release.yml が自動実行
# → x64/ARM64 MSI/zip 生成 + Sigstore 署名 + SLSA + SBOM
```

### 5. winget 更新 PR (手動)
`installer/winget/manifest.yaml` の SHA256 を更新して
`winget-pkgs` リポジトリに PR 送信

### 6. dotnet tool 更新
```powershell
dotnet pack src/MWC.Cli/MWC.Cli.csproj -c Release
dotnet nuget push *.nupkg --source https://api.nuget.org/v3/index.json
```

## 品質ゲート (全 green が必須)
- [ ] `dotnet test` 全 120 ケース パス
- [ ] CodeQL 警告ゼロ
- [ ] `dotnet format --verify-no-changes` パス
- [ ] カバレッジ ≥ 80%
- [ ] SBOM 生成済み
- [ ] Sigstore 署名済み

## ロールバック
```powershell
git revert v1.5.0  # コミット取消
git tag -d v1.5.0  # タグ削除
git push origin --delete v1.5.0
```
