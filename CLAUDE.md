# CLAUDE.md

> MWC プロジェクト憲法。AI(Claude Code/他)との協業ルール+コードベース構造。

## Why

複数の無線アダプターを持つ Windows PC で、各アダプターの SSID 一覧/接続を独立管理する CLI+GUI ツール。WPA3/Enterprise/OWE まで網羅し、商用品質を維持する。

## Map(どこに何があるか)

```
src/
  MWC.Core/              ← プラットフォーム非依存。テスト容易。
    Models/                  認証方式 enum、WifiNetwork record 等
    Abstractions/            IWifiService, ISecretProtector, IConnectivityChecker
    Profile/                 ProfileXmlBuilder ★ 心臓部
                             WifiUri (Wi-Fi QR コード)

  MWC.Platform.Windows/  ← ManagedNativeWifi 経由の Win 実装
    WindowsWifiService       スキャン/接続/プロファイル
    ConnectionWaiter         WlanNotification で実完了待機
    DpapiSecretProtector     パスワード暗号化
    HttpConnectivityChecker  msftconnecttest 疎通確認

  MWC.App/               ← WPF UI。MVVM。
    ViewModels/              CommunityToolkit.Mvvm
    Views/                   ConnectDialog, QrCodeDialog
    Resources/               .resx 多言語
    App.xaml.cs              Host/DI/Serilog/例外捕捉

  MWC.Cli/               ← System.CommandLine

tests/
  MWC.Core.Tests/          ProfileXmlBuilder ゴールデン+認証検証

installer/
  wix/Product.wxs            MSI 生成
  winget/manifest.yaml       winget 配布

.github/workflows/
  ci.yml                     build/test/coverage 80%
  codeql.yml                 SAST
  release.yml                tag→MSI/zip+Sigstore+SLSA
```

## Rules(やっていい/ダメ)

### 禁止事項
- ❌ `netsh.exe` 経由のネットワーク操作。**例外なし**(コマンドインジェクション+成功判定不能)
- ❌ パスワード平文ファイル書出。`WlanSetProfile` は文字列直渡し可
- ❌ `async void` を `try/catch` なしで書く
- ❌ ProfileXmlBuilder で文字列連結による XML 組立(必ず `XElement`)
- ❌ `WMI MSNdis_80211_*` クラス使用(Win11 24H2 で動作不可)
- ❌ `Microsoft.Toolkit.Mvvm`(旧名)。`CommunityToolkit.Mvvm` 使用
- ❌ `System.Management`(WMI)依存追加
- ❌ 量子・AI 等の「派手な機能」追加。Wi-Fi に集中

### 必須事項
- ✅ 全認証方式は ProfileXmlBuilder のゴールデンテストで検証
- ✅ パスワードは `SecureString`、使用直後 `Marshal.ZeroFreeGlobalAllocUnicode`
- ✅ 接続成功は `WlanNotification` の `connection_complete` 受信 + 疎通確認の 2 段
- ✅ UI 文字列は **必ず** `Strings.resx` 経由(ハードコード禁止)
- ✅ AutomationProperties.Name を全インタラクティブ要素に付与
- ✅ ログは Serilog のみ。`Console.WriteLine`/`Debug.WriteLine` は CLI でのみ可

### コーディング
- 命名: PascalCase 型/メソッド、camelCase ローカル、_camelCase フィールド、UPPER_SNAKE 定数
- 関数引数 ≤ 3。多い場合は record でグループ化
- Result<T,E> パターン(`ConnectionResult`)。例外はバグ用、業務失敗は Result
- ファイルスコープ namespace
- `using` ディレクティブはファイル外側

## Workflows(作業フロー)

### 新機能追加
1. **Plan**: GitHub Issue で要件 → ADR ドラフト
2. **設計**: `docs/adr/000X-*.md` 起こす
3. **テスト**: `tests/` で失敗ケース先行
4. **実装**: Core → Platform → UI/CLI の順
5. **検証**: `dotnet format` / `dotnet test` / 実機確認
6. **PR**: CHANGELOG.md `[Unreleased]` に追記

### バグ修正
1. **再現**: テストで失敗を再現
2. **修正**: 最小差分
3. **回帰**: テスト追加で再発防止
4. **CHANGELOG**: 修正内容明記

### リリース
1. `CHANGELOG.md` の `[Unreleased]` を `[1.x.x] - YYYY-MM-DD` に変更
2. `Directory.Build.props` の Version 更新
3. `git tag v1.x.x && git push --tags` → release.yml が自動実行
4. winget manifest 更新 PR(手動)

## How(技術判断ルール)

### Carmack / Martin / Pike 流の優先順位

| 状況 | 採用 |
|---|---|
| 同じ機能を 2 通りで書ける | より単純な方 |
| 抽象化 vs 直接実装 | テスト容易性が決まる方 |
| 性能 vs 可読性 | 可読性。ホットパスのみ最適化 |
| 依存追加 vs 自前実装 | ≤200 行なら自前。WlanAPI のような複雑物は依存 |

### Skill ディレクトリ
`.claude/skills/` 配下:
- `wifi-profile-xml-builder.md` ← 認証方式別の XML 雛形
- `dpapi-secret-handling.md`     ← SecureString 取扱
- `wpf-accessibility-review.md`  ← AutomationProperties 検証
- `wlan-notification-handler.md` ← 通知待機実装パターン
- `gstack-release.md`            ← リリースワークフロー

### Hooks(ガードレール)
`.claude/settings.json` で次を禁止:
- `auth/`、`billing/`、`migrations/` 配下への書込
- `appsettings.Production.*` への書込
- パスワード等を含む文字列リテラルのコミット

## 禁止プロンプト例

✗「とにかく動くように修正して」 → テスト先行で
✗「便利機能をたくさん追加」 → 1 PR 1 機能
✗「TODO は後で」 → CHANGELOG に書く
