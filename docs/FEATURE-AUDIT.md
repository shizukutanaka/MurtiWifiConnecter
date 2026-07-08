# 機能過不足監査 (Feature Audit)

> **この文書の目的**: MWC の全機能を「過剰」「不足」「適正」に選別した監査結果。
> 2026-07 のソクラテス式監査(「実装されている」≠「機能している」を軸にした全数調査)の成果物。
> **読者想定**: この監査の文脈を持たない将来の開発者・AI セッション。そのため全主張に
> ファイルパス・検証コマンド・判断理由を付し、この文書単体で行動を開始できるようにしてある。
> 記載の数値は 2026-07 時点。作業前に必ず「§5 再監査手順」のコマンドで最新状態を再検証すること。

---

## §0 🔴 最重要・未解決: CI が実際には実走していない(2026-07 第3パスで発見)

> **`.github/workflows/` が存在せず、GitHub Actions の
> CI/CodeQL/リリース自動化がおそらく一度も実走していない。** CLAUDE.md はこのディレクトリ構成を
> 前提として文書化しているが、実際には CI 設定は `ci/github-workflows/*.yml` と `docs/ci/*.yml` の
> **2箇所に別バージョンで存在**し、GitHub がワークフローとして認識する唯一のパス
> (`.github/workflows/`)には**何も置かれていない**。過去に一度だけ正しい場所へ移設する試み
> (コミット `1c28a9c`)があったが、**その13秒後に同一セッション内で自動的にリバートされている**
> (コミット `9274953`、コミットメッセージは boilerplate のみで理由の記載なし — 内容と発生間隔から、
> エージェント実行環境の `.github/workflows/` 書込み制限ガードレールによる自動差し戻しと推測される)。
> `docs/build-blockers-2026.md` も「CI を `.github/workflows/` へ設置して実走させるのが次の最優先」と
> 明記済みだが未達のまま。**この監査セッションを含め、このリポジトリで行われた変更はおそらく一度も
> 実際の GitHub Actions CI で検証されていない**(このセッションの検証は `python3`/`grep` による
> 静的チェックのみ)。この問題はエージョントによる自動修正では再度リバートされる可能性が高いため、
> **リポジトリ所有者による直接対応、または明示的な許可の下での対応が必要**。
> 検証: `ls .github/workflows/ 2>&1`(存在しないはず)/ `git log --oneline --all -- .github/` /
> `diff ci/github-workflows/ci.yml docs/ci/ci.yml`(2つの別バージョンが存在することを確認)。

**中心的な発見**: このコードベースには「Core にクラスがあり単体テストが通る(=実装されている)」が
「App/CLI のどこからも呼ばれておらずユーザーが到達できない(=機能していない)」サービスが
多数存在する。ROADMAP.md はかつてこれらを完了 `[x]` と申告していた(2026-07 に訂正済み)。

---

## §1 過剰 — 製品(App/CLI)から到達不能(SDK 経由でのみ出荷)

### 1a. 完全孤立サービス(2026-07 執筆時点11個 → `OweSelectionService`/`RegulatoryDomainService`/
`RetryPolicy` 配線済みにより現在8個)

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
| ~~`RegulatoryDomainService`~~ | 6GHz 帯の国別チャネル表示 | **✅ 配線済み(2026-07)** | `NetworkDetailViewModel.RegulatoryLabel`(6GHz ネットワークのみ表示、`RegionInfo.CurrentRegion` からシステムロケールで国を自動推定)。テスト: `NetworkDetailViewModelVpnEapWiringTests.cs` に追加。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `CatImportService` | eduroam CAT XML インポート | **ブロック中** — 下記「配線できない理由」参照 | XXE/DTD 対策済みの丁寧な実装。品質は高い。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| ~~`OweSelectionService`~~ | 同一 SSID の Open/OWE ペア統合 | **✅ 配線済み(2026-07)** | `AdapterViewModel.RefreshAsync`・`AllAdaptersOverviewViewModel.AdapterPanelViewModel.RefreshAsync`・CLI `mwc scan` の3箇所に挿入。既知の限界(Open 側が実際に接続中でも無条件除外される稀なエッジケース)をサービス自身の XML doc に明記。テスト: `tests/MWC.Core.Tests/OweWiringTests.cs` |
| `Hotspot20Service` | Passpoint / キャリア Wi-Fi | 配線(製品側)を検討。**削除は不可** | 日本キャリア(au/SoftBank/docomo)プリセット付き。**SDK 公開 API(名指し宣伝あり)— 削除は SemVer メジャー要** |
| `WifiDirectService` | Wi-Fi Direct P2P | 配線 or 削除の判断(製品側。SDK からの削除は別途 SemVer 検討) | `IWifiDirectAdapter` のプラットフォーム実装が別途必要(未存在) |
| `CaptivePortalService` | RFC 8908 captive portal API | `HttpConnectivityChecker`(実際に使われている方)との統合を検討(製品側。SDK からの削除は別途 SemVer 検討) | 機能が部分重複している |
| `KalmanRssiFilter` | RSSI 平滑化 | `SignalHistoryService` に統合 or 削除(製品側。SDK からの削除は別途 SemVer 検討) | `SignalQualityPredictor`(EMA 方式)が同目的で既に配線済み |
| ~~`RetryPolicy`~~ | 接続リトライ | **✅ 配線済み(2026-07)** | `AdapterConnectExtension.ConnectWithAppleFlowAsync` に配線。一時的失敗はジッター付きバックオフで自動再試行(最大2回)、決定的失敗はユーザー承認ダイアログへ。`IsRetriable` の分類漏れ4件も同時に修正 |
| `SignalIconService` | 信号アイコン選択 | 削除候補(製品側。SDK からの削除は別途 SemVer 検討) | 確認済み: `NetworkItemViewModel.Bars`(独自の閾値ロジック)と `MainWindow.xaml` 等の信号バー表示が別実装で存在し、本サービスは未配線のまま。閾値が微妙に異なる(75/50/25 vs 80/60/40/20)ため、配線するなら表示上の挙動変化を伴う |
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

**`CatImportService` が配線できない理由(2026-07 調査で判明した、より根本的な欠落)**:
当初は「GUI にインポートダイアログを追加するだけ」の小さな作業と見積もっていたが、調査の結果
**GUI (`ConnectDialog`) も CLI (`mwc connect`) も、802.1X Enterprise 認証(PEAP/EAP-TTLS)の
ユーザー名・パスワード入力に一切対応していない**ことが判明した
(`grep -rn "Username.*Password\|EnterpriseCred" src/MWC.App/Views/ src/MWC.App/ViewModels/` は
0件、CLI `mwc connect --auth` にも `--username` 相当のオプションが存在しない)。`ConnectDialog`
は Personal(PSK/WEP)/Open/OWE のパスフレーズ入力のみに対応。さらに `CertificatePickerDialog`
(EAP-TLS 用クライアント証明書選択、`src/MWC.App/Views/CertificatePickerDialog.xaml.cs`)自体も
どの接続フローからも呼び出されておらず孤立している(`L.cs` の文字列参照のみ)。

eduroam の PEAP/EAP-TTLS は CAT XML に実際の認証情報を含まない(各利用者の学内アカウントは
XML 配布後にユーザー自身が入力する設計が eduroam の仕様そのもの)ため、`CatImportService` を
真に機能させるには **先に Enterprise 認証情報入力 UI を新規構築する必要がある**。
XML パースだけ動かして認証情報を入力させない「インポート」機能は、登録はできても実際には
接続できない(PEAP は `ProfileXmlBuilder` の検証で Username/Password 必須のため、
そもそも登録時点で失敗する)半端な機能になるため、実装を見送った。

次に着手する場合の推奨順序: (1) Enterprise 用ユーザー名/パスワード入力パネルを
`ConnectDialog` に追加(または新規ダイアログ)→ (2) `CertificatePickerDialog` を EAP-TLS
選択時の接続フローに接続 → (3) その基盤の上で `CatImportService` の「XML を解析して
SSID/EAP種別/サーバー検証情報を事前入力し、残りをユーザーに入力させる」インポート機能を追加。
(1)(2) は `CatImportService` 単体よりずっと大きい作業(新規 UI 設計・全認証方式のゴールデン
テスト拡張・資格情報の安全な取り扱い検討)であり、§2b の SecureString 論点とも関連する。

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
2. ~~**高**: §1a のうち `RegulatoryDomainService`/`OweSelectionService` の配線~~ — 2026-07 に完了。
   前者は CLI `scan`・`AdapterViewModel`・`AdapterPanelViewModel` の3箇所、後者は
   `NetworkDetailViewModel` の6GHz 限定表示として配線。
   `CatImportService`(eduroam インポート)は当初「小差分」と見積もっていたが、調査の結果
   Enterprise 認証情報入力 UI 自体が GUI/CLI どちらにも存在しない、より根本的な欠落を発見した
   ため §2a の「配線できない理由」欄へ格上げ・降格(単純な配線作業ではなくなったため優先度
   リストの番号付けからは除外。次点は下記7)
3. **中**: §2b の SecureString 裁定をリポジトリ所有者に仰ぐ(裁定なしでは進められない)
4. **中**: §1a 残りの配線 or 削除判断(削除する場合は対応テストも削除。SDK 公開3サービス
   ―`CatImportService`/`Hotspot20Service`は残存、`OweSelectionService`/`RegulatoryDomainService`
   は既に配線済み― は削除不可、§1a 注記参照)
5. **中**: `PrivacyAdvisoryService` — MAC ランダム化状態のプラットフォーム検出を新規実装後に配線
6. **低**: §1c のプラットフォーム実装(実機がないと検証不能)
7. **低〜中(規模が大きいため要事前設計)**: Enterprise(802.1X)認証情報入力 UI の新規構築
   (GUI: `ConnectDialog` 拡張 or 新規ダイアログ、CLI: `mwc connect --username` 相当)+
   `CertificatePickerDialog` の接続フローへの接続。これが完了して初めて `CatImportService`
   の配線が「小差分」になる。§2b の SecureString 裁定と合わせて検討すべき(資格情報の
   安全な取り扱いという同じ論点を含むため)。

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

### §6 `benchmarks/`・`completions/`・`tools/` の監査(2026-07 第3パスで実施)

以前「未監査」としていた3ディレクトリを調査した結果:

| ディレクトリ | 内容 | 状態 |
|---|---|---|
| `benchmarks/` | `MwcBenchmarks.cs`(BenchmarkDotNet、7クラス) | **CI 未組込み**(`grep -rln "benchmarks" .github/` — そもそも `.github/workflows/` 自体が無いため右記§0参照。ワークフローが正しい場所にあったとしても、いずれのワークフローファイルにも benchmarks 実行ステップは存在しない)。回帰検出に使われていないため、性能劣化があっても気づけない |
| `completions/` | `mwc.bash`・`mwc.ps1`(CLI 補完スクリプト) | **配布物に未同梱**。CHANGELOG.md には「release.yml の CLI zip に含めるよう修正」という過去の記載があるが、`release.yml` 自体が存在しない(§0 参照)ため実現していない。ユーザーが実際に補完を使うには手動コピーが必要 |
| `tools/oui-update.ps1` | IEEE OUI ベンダー DB を月次更新する想定のスクリプト(スクリプト自身のコメントに「GitHub Actions の schedule で月1回自動実行可能」と明記) | **スケジュール実行が一度も設定されていない**(§0 の CI 不在と同根)。`OuiLookupService` 内蔵 DB は最終手動更新時点で凍結されたまま古くなっていく |
| `tools/update-winget-manifest.ps1` | winget manifest のバージョン・SHA256 自動更新 | 参照元ゼロ。リリース時に手動実行が必要だが、それを促す仕組み(CI ステップ・チェックリスト等)が存在しない |

これらは全て §0 の「CI 不在」問題と同根(自動化を書いたが、実行される場所に配置・接続されていない)。
§0 の解決が前提条件となるため、優先順位は §0 に従属する。

---

*最終更新: 2026-07 / 監査の詳細経緯は CHANGELOG.md `[Unreleased]` と git log を参照。*
