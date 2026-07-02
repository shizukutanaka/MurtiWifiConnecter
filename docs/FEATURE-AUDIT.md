# 機能過不足監査 (Feature Audit)

> **この文書の目的**: MWC の全機能を「過剰」「不足」「適正」に選別した監査結果。
> 2026-07 のソクラテス式監査(「実装されている」≠「機能している」を軸にした全数調査)の成果物。
> **読者想定**: この監査の文脈を持たない将来の開発者・AI セッション。そのため全主張に
> ファイルパス・検証コマンド・判断理由を付し、この文書単体で行動を開始できるようにしてある。
> 記載の数値は 2026-07 時点。作業前に必ず「§5 再監査手順」のコマンドで最新状態を再検証すること。

**中心的な発見**: このコードベースには「Core にクラスがあり単体テストが通る(=実装されている)」が
「App/CLI のどこからも呼ばれておらずユーザーが到達できない(=機能していない)」サービスが
多数存在する。ROADMAP.md はかつてこれらを完了 `[x]` と申告していた(2026-07 に訂正済み)。

---

## §1 過剰 — 製品(App/CLI)から到達不能(SDK 経由でのみ出荷)

### 1a. 完全孤立サービス(11個)

`src/` 内で自ファイル以外からの参照が**ゼロ**の Core サービス。テストは存在する(=壊れてはいない)が、
App/CLI という製品としては動いていない。検証コマンド:

```bash
# 例: RegulatoryDomainService の参照元を探す(自ファイルを除く)
grep -rl "\bRegulatoryDomainService\b" src/ | grep -v "/RegulatoryDomainService.cs"
# → 出力が空 = 孤立(ただし直後の SDK 注記を必ず読むこと)
```

> **⚠ 重要な訂正(2026-07 第2パスで判明)**: `sdk/MWC.SDK.csproj` は
> `<Compile Include="../src/MWC.Core/**/*.cs" />` で **Core の全ソースを NuGet パッケージ
> `MWC.SDK` (v3.11.0, `IsPackable=true`) にそのまま再パッケージして出荷している**。
> さらにパッケージの `<Description>` は下表のうち `CatImportService`・`RegulatoryDomainService`・
> `OweSelectionService`・`Hotspot20Service` を**名指しで機能として宣伝**している。
> つまりこれらは「App/CLI という製品からは未配線」だが「`MWC.SDK` ライブラリ利用者には
> 公開 API として既に出荷済み」— **単純な「削除」は公開 NuGet パッケージの破壊的変更**であり
> SemVer メジャーバンプなしには行えない。以下の「推奨アクション」列は App/CLI(製品)側の判断であり、
> SDK パッケージからも削除する場合は別途 SemVer 対応が必要な点に注意。
> 検証: `grep -A3 "Compile Include" sdk/MWC.SDK.csproj` / `grep -A15 "<Description>" sdk/MWC.SDK.csproj`

| サービス (`src/MWC.Core/Services/`) | 本来対応する機能 | 推奨アクション | 備考 |
|---|---|---|---|
| `RegulatoryDomainService` | 6GHz 帯の国別チャネル表示 | **配線**(製品側)— `NetworkDetailViewModel` か CLI `scan` に国別チャネル合法性表示を追加 | 国別テーブル・PSC 判定まで実装済み。テスト5ファイル。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `CatImportService` | eduroam CAT XML インポート | **配線**(製品側)— GUI にインポートダイアログ追加 | XXE/DTD 対策済みの丁寧な実装。品質は高い。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `OweSelectionService` | 同一 SSID の Open/OWE ペア統合 | **配線**(製品側)— スキャン結果パイプライン(`AdapterViewModel.RefreshAsync`)に挿入 | 純粋関数なので挿入は容易。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `Hotspot20Service` | Passpoint / キャリア Wi-Fi | 配線(製品側)を検討。**削除は不可** | 日本キャリア(au/SoftBank/docomo)プリセット付き。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `WifiDirectService` | Wi-Fi Direct P2P | 配線 or 削除の判断(製品側。SDK からの削除は別途 SemVer 検討) | `IWifiDirectAdapter` のプラットフォーム実装が別途必要(未存在) |
| `CaptivePortalService` | RFC 8908 captive portal API | `HttpConnectivityChecker`(実際に使われている方)との統合を検討(製品側。SDK からの削除は別途 SemVer 検討) | 機能が部分重複している |
| `KalmanRssiFilter` | RSSI 平滑化 | `SignalHistoryService` に統合 or 削除(製品側。SDK からの削除は別途 SemVer 検討) | `SignalQualityPredictor`(EMA 方式)が同目的で既に配線済み |
| `RetryPolicy` | 接続リトライ | `ConnectionExecutor` に統合 or 削除(製品側。SDK からの削除は別途 SemVer 検討) | executor は現在リトライなしで動いている |
| `SignalIconService` | 信号アイコン選択 | 削除候補(製品側。SDK からの削除は別途 SemVer 検討) | View 側に同等ロジックが直書きされている可能性を確認してから |
| `BeaconUptimeEstimator` | AP 稼働時間推定 | 削除候補(製品側。SDK からの削除は別途 SemVer 検討) | TSF タイムスタンプ入力をどの層も供給していない |
| `AccessibilityAuditService` | WCAG コントラスト計算 | **現状維持** | 2026-07 に `tests/MWC.Core.Tests/ThemeAccessibilityAuditTests.cs` から使用開始(CI でテーマ色を検証)。製品コードからは未参照だが、これは正当な使途。SDK にも同梱 |

### 1b. 準孤立(参照が形式的なもののみ)

| サービス | 唯一の"参照" | 実態 |
|---|---|---|
| `GroupPolicyProvider` | `MWC.Core.csproj` のコメント行 | Intune/GP ポリシー読取。どのコードも呼んでいない |
| `PrivacyAdvisoryService` | `VpnAdvisoryService` の XML doc 言及 | MAC ランダム化助言。どのコードも呼んでいない |
| `ISecretProtector` / `DpapiSecretProtector` | `App.xaml.cs`/CLI `Program.cs` の **DI 登録のみ** | `Protect`/`Unprotect` の呼び出し元ゼロ。§2b 参照 |

### 1c. プラットフォームスタブ(ROADMAP は訂正済み、ここは一覧性のための集約)

- `src/MWC.Platform.MacOS/CoreWlanWifiService.cs` — **半実装プロトタイプ**。スキャン/接続は動くが
  `RegisterProfileAsync` が `false` 固定のため、パスフレーズ必須ネットワークへは
  `ConnectionExecutor` が接続前に失敗させる。**注意**: 安易にスタブを `true` にしても直らない
  (詳細な罠の解説がファイル内コメントに記載済み。`NmcliWifiService` の Linux 実装が正しい手本)。
- `src/MWC.Platform.Android/AndroidWifiService.cs`、`src/MWC.Platform.iOS/IosWifiService.cs` —
  **完全スタブ**(全メソッドが空配列/false/失敗を返す)。API 参照コメントのみ有用。

---

## §2 不足 — 必要なのに欠けている

### 2a. GUI 配線の不足(CLI からしか使えない助言機能)

`SecurityAdvisoryService` だけが GUI(`src/MWC.App/ViewModels/NetworkDetailViewModel.cs`)に
配線されており、同系統の以下は CLI 止まり。GUI ユーザーはこれらの存在を知る手段がない:

| サービス | 現在の到達手段 | 状態 |
|---|---|---|
| `VpnAdvisoryService` | CLI `mwc vpn-advice` **+ GUI 詳細パネル** | **✅ 配線済み(2026-07)**。`NetworkDetailViewModel.VpnAdviceLabel` として表示 |
| `EapAuthStatsService` | CLI `mwc eap-stats` **+ GUI 詳細パネル** | **✅ 配線済み(2026-07)**。`NetworkDetailViewModel.EapStatsLabel`(記録がある場合のみ表示) |
| `PrivacyAdvisoryService` | **なし**(完全孤立) | **未着手** — 下記「配線できない理由」参照 |

`VpnAdvisoryService`/`EapAuthStatsService` は `_secAdvisor` と同じパターン(static readonly
フィールド + `Load()` 内で `Analyze`/`GetAll` 呼び出し → ラベルプロパティ)で
`src/MWC.App/MainWindow.xaml` の詳細パネルに追加済み。新規 resx キー7個を全15ロケールに
追加(`LocaleKeyConsistencyTests` で完全性を検証)。テストは
`tests/MWC.Core.Tests/NetworkDetailViewModelVpnEapWiringTests.cs`。

**`PrivacyAdvisoryService` が配線できない理由**: `Analyze(MacAddressMode mode, WifiNetwork network)`
は現在の Wi-Fi アダプターの MAC ランダム化状態(固定/ネットワーク別ランダム/日次ランダム)を
引数に要求するが、**この状態を検出するコードがプラットフォーム層に一切存在しない**
(`grep -rn "MacAddressMode" src/` は定義・テスト以外で0件)。Windows の
「ランダムハードウェアアドレス」設定はレジストリ(`HKLM\SYSTEM\...\WlanSvc\...` 相当)から
読み取る必要があり、これは既存の Core サービスを呼ぶだけでは完結しない新規プラットフォーム
実装が必要。§4 優先順位からは意図的に外してある(小差分では終わらないため)。

### 2b. CLAUDE.md ルールと実装の乖離(**ユーザー裁定待ち — 勝手に解決しないこと**)

CLAUDE.md 必須事項「パスワードは `SecureString`、使用直後 `Marshal.ZeroFreeGlobalAllocUnicode`」
に対し、実装は `WifiProfileSpec.Passphrase`(string)→ `ProfileXmlBuilder` → `ConnectDialog` の
`PasswordBox.Password` まで**全域 plain string**。`SecureString` の使用箇所はゼロ。

選択肢は (a) ルールを実態に合わせ緩和、(b) 実装を SecureString 化(10ファイル超、
Microsoft 自身が .NET Core+ で非推奨としている点に注意)、(c) 既知ギャップとして記録のみ。
**2026-07 セッションで (a) を無断実行しようとして差し戻された経緯がある**。CLAUDE.md は
プロジェクト憲法であり、その変更はリポジトリ所有者の明示的承認が必要。承認が得られるまで
現状維持(= 文書上のルールが優先)。

### 2c. 検証の不足(機能はあるが検証されていない)

| 項目 | 状態 | 補足 |
|---|---|---|
| スクリーンリーダー実機テスト (NVDA/JAWS/ナレーター) | **証跡なし** | リポジトリ内に対応するテスト・実施記録が存在しない。`AutomationProperties.Name` の付与自体は全 View で確認済み(土台は有る) |
| bn/hi/ta 翻訳のネイティブレビュー | **未実施** | 2026-07 に 274キー×3言語を機械翻訳で補完し、キー完全性は `LocaleKeyConsistencyTests` で保証。ただし訳文の品質レビューは AI 翻訳のみで、ネイティブ確認を推奨 |
| Fluent テーマの本文コントラスト | **原理的に静的監査不能** | `Fluent.xaml` の Bg/FgBrush は `SystemColors` への動的参照(OS 設定依存)。他5テーマは `ThemeAccessibilityAuditTests` で自動検証済み |
| Windows 実機での動作確認全般 | **CI 範囲外** | この開発環境は Linux で .NET SDK なし。CI は `MWC.Core.Tests` のみ。WPF/WLAN 層の実機検証は手動でしか行えない |

### 2d. 機能面の実装不足(意図的な部分実装 — 完成させる場合の注意付き)

- **VPN「自動切替」**: `VpnAdvisoryService` は助言のみで OS の VPN 状態は変更しない。
  これは意図的(誤判定時に機密トラフィックが露出する影響の大きさから)。自動切替を実装する
  場合は本サービスの判定を流用しつつ、必ずユーザー確認 UI を挟むこと。
- **802.1X「自動テスト」**: `EapAuthStatsService` は既存接続の成否を記録するのみで、
  テスト接続を自発しない。これも意図的(勝手な接続試行はユーザーの意図に反する)。

---

## §3 適正 — 過不足なしと判定済み(誤って「改善」しないこと)

以下は監査で検討のうえ**現状が正しい**と結論した項目。将来のセッションが「問題」と
誤検知しやすいものを列挙する:

| 項目 | 一見問題に見える点 | 正しい理由 |
|---|---|---|
| `NetworkHistoryService` の JSON 保存 | ROADMAP がかつて「SQLite」と記載 | 500件規模に SQLite は過剰。CLAUDE.md「≤200行なら自前実装」に整合。ROADMAP 側を訂正済み |
| Solarized テーマの本文コントラスト AA 止まり (5.61:1) | 他テーマは AAA | Ethan Schoonover の著名パレット原典を尊重。色を歪めて AAA を狙うと「Solarized を選ぶ理由」自体が消える。`ThemeAccessibilityAuditTests` に文書化済みの例外 |
| `AdapterPreferencesService.SetAutoReconnect(true)` が no-op | バグに見える | 設計意図。自動再接続は SSID の明示登録(`PinSsid`/`AddPreferred`)で有効化する方式。XML doc に明記済み |
| CLI の `Console.WriteLine` | CLAUDE.md は Serilog 必須 | CLAUDE.md が CLI のみ明示的に許可(「`Console.WriteLine`/`Debug.WriteLine` は CLI でのみ可」) |
| 技術用語の未翻訳 (PHY/BSSID/WEP/AES/MLO 等) | 翻訳漏れに見える | 全ロケール共通の意図的慣例。ja/de など完全翻訳済みロケールも同じキーをラテン文字のまま保持 |
| `EapType` enum の非ゼロ明示値 (25/13/23/21) | 不揃いに見える | IANA の EAP Method Type 公式番号。変更禁止 |
| DPAPI エントロピー `"WiFix-v1"` バイト列 | 旧名の残骸に見える | **変更絶対禁止**。既存ユーザーの保存済み暗号データが復号不能になる(`DpapiSecretProtector.cs` に警告コメントあり) |
| コア機能(複数アダプター管理)の導線 | — | `AllAdaptersOverviewView` は Ctrl+Shift+A・ツールバー・メニューから到達可能。健全 |
| App 層サービス (`src/MWC.App/Services/`) 全15個 | Core と同じ孤立問題があるのでは | 2026-07 第2パスで全数検証。真の孤立はゼロ(全て配線済み)。App 層は健全 |

---

## §4 優先順位の目安

1. ~~**高**: §2a の GUI 配線~~ — `VpnAdvisoryService`/`EapAuthStatsService` は 2026-07 に完了。
   `PrivacyAdvisoryService` は新規プラットフォーム実装が要るため別枠(下記)
2. **高**: §1a のうち `RegulatoryDomainService`/`CatImportService`/`OweSelectionService` の配線
   (実装品質が高く、配線だけで ROADMAP 項目が本当に完了する)
3. **中**: §2b の SecureString 裁定をリポジトリ所有者に仰ぐ(裁定なしでは進められない)
4. **中**: §1a 残りの配線 or 削除判断(削除する場合は対応テストも削除。SDK 公開4サービスは
   削除不可、§1a 注記参照)
5. **中**: `PrivacyAdvisoryService` — MAC ランダム化状態のプラットフォーム検出を新規実装後に配線
6. **低**: §1c のプラットフォーム実装(実機がないと検証不能)

---

## §5 再監査手順

この文書の記載は 2026-07 時点のスナップショット。以下で最新状態を再検証できる:

```bash
# (1) 孤立サービスの全数検出(src/ 内でどこからも参照されないサービス)
for f in src/MWC.Core/Services/*.cs; do
  name=$(basename "$f" .cs)
  count=$(grep -rl "\b$name\b" src/ | grep -v "/$name.cs" | wc -l)
  [ "$count" -eq 0 ] && echo "orphan: $name"
done

# (2) GUI/CLI 到達性の確認(特定サービスについて)
grep -rl "\bServiceName\b" src/MWC.App/ src/MWC.Cli/
```

> **⚠ 手法的な盲点(2026-07 第2パスで判明)**: (1) のクラス名 grep は**拡張メソッド呼び出し**を
> 見逃す。例えば `SafeFireAndForget`(`src/MWC.App/Services/SafeFireAndForget.cs`)はクラス名の
> grep ではヒットしないが、実際は `.Forget()` という拡張メソッド構文で App 内5箇所から使われて
> いる(誤って「孤立」と判定しかけた)。孤立候補が出たら、判定を確定する前に必ず
> `grep -c "(this " <file>` でそのファイルが拡張メソッドを定義していないか確認すること。
> 上記 §1a の11件は個別にこの確認を実施済みで、結論は変わらない。

自動化済みの検証(CI のテストが担当。手動再確認は不要):

| 観点 | テスト |
|---|---|
| 全15ロケールの resx キー完全一致 | `tests/MWC.Core.Tests/LocaleKeyConsistencyTests.cs` |
| テーマ6種の WCAG コントラスト | `tests/MWC.Core.Tests/ThemeAccessibilityAuditTests.cs` |
| テーマ辞書の存在・整形・16ブラシ契約 | `tests/MWC.Core.Tests/ThemeContractTests.cs` |

自動化されていない検証(人手が必要): スクリーンリーダー実機テスト、bn/hi/ta 訳文の
ネイティブレビュー、Windows 実機での WLAN 動作確認。

**本監査の対象外(未実施。将来パスの候補)**: `benchmarks/`、`completions/`、`tools/` の
各ディレクトリはまだ監査していない。

---

*最終更新: 2026-07 / 監査の詳細経緯は CHANGELOG.md `[Unreleased]` と git log を参照。*
