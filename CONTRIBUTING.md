# Contributing to MWC

## 開発セットアップ

```powershell
git clone https://github.com/shizukutanaka/MurtiWifiConnecter.git
cd MurtiWifiConnecter
dotnet restore MWC.sln
dotnet build MWC.sln
dotnet test MWC.sln
```

必須:
- .NET 8 SDK
- Windows 10 1809+ または 11
- Visual Studio 2022 17.8+ または VS Code + C# Dev Kit

## コミット規約

[Conventional Commits](https://www.conventionalcommits.org/):
- `feat:` 新機能
- `fix:` バグ修正
- `refactor:` 内部改善
- `docs:` ドキュメント
- `test:` テスト追加/修正
- `chore:` ビルド/CI/依存

例: `feat(profile): add WPA3-Enterprise 192-bit support`

## PR チェックリスト

- [ ] テスト追加(回帰防止)
- [ ] `dotnet format` 通過
- [ ] `dotnet test` 全パス
- [ ] CHANGELOG.md `[Unreleased]` に追記
- [ ] 公開 API 変更時は ADR 起票
- [ ] UI 文字列追加時は Strings.resx + 主要言語(ja/en)更新

## 翻訳貢献

[`docs/i18n-guide.md`](docs/i18n-guide.md)

## コード品質ライン

- カバレッジ ≥ 80%(CI でチェック)
- CodeQL 警告ゼロ
- TreatWarningsAsErrors(全プロジェクト)
- `Nullable enable`

## 行動規範

[Contributor Covenant 2.1](https://www.contributor-covenant.org/version/2/1/code_of_conduct/)
