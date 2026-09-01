# 機能過不足監査 (Feature Audit)

> **この文書の目的**: MWC の全機能を「過剰」「不足」「適正」に選別した監査結果。
> 2026-07 のソクラテス式監査(「実装されている」≠「機能している」を軸にした全数調査)の成果物。
> **読者想定**: この監査の文脈を持たない将来の開発者・AI セッション。そのため全主張に
> ファイルパス・検証コマンド・判断理由を付し、この文書単体で行動を開始できるようにしてある。
> 記載の数値は 2026-07 時点。作業前に必ず「§5 再監査手順」のコマンドで最新状態を再検証すること。
> **作業方法・環境の罠・モデル別注意は [AI-SESSION-HANDBOOK.md](AI-SESSION-HANDBOOK.md) を参照。**
> **所有者が実行すべき残作業は [COMPLETION-CHECKLIST.md](COMPLETION-CHECKLIST.md) にまとめてある**
> (CI 設置・Release・MLO リンク詳細の 3 件。各々に「なぜ AI が実行できなかったか」の実測結果付き)。

---

## §0 🔴 最重要・未解決: CI が実際には実走していない(2026-07 第3パスで発見)

> **`.github/workflows/` が存在せず、GitHub Actions の
> CI/CodeQL/リリース自動化がおそらく一度も実走していない。** CLAUDE.md はこのディレクトリ構成を
> 前提として文書化しているが、GitHub がワークフローとして認識する唯一のパス
> (`.github/workflows/`)には**何も置かれていない**
> (CI 設定は当初 `ci/github-workflows/*.yml` と `docs/ci/*.yml` の 2 箇所に別バージョンで
>  存在したが、2026-07 に `docs/ci/` へ一本化した — 下記更新参照)。過去に一度だけ正しい場所へ移設する試み
> (コミット `1c28a9c`)があったが、**その13秒後に同一セッション内で自動的にリバートされている**
> (コミット `9274953`、コミットメッセージは boilerplate のみで理由の記載なし — 内容と発生間隔から、
> エージェント実行環境の `.github/workflows/` 書込み制限ガードレールによる自動差し戻しと推測される)。
> **🔴 2026-07 第4パス — ブロックの真因が判明した(従来の記述は誤り)**:
> これまで「エージェントの `.github/workflows/` 書込みは実行環境のガードレールで
> **自動差し戻し**される」と記録していたが、これは 13 秒後の revert(`9274953`)からの
> **推論であって検証されていなかった**。実際に試したところ真因は別だった:
>
> - **ローカルは一切ブロックしない。** `.claude/settings.json` は `Write(.github/**)` を
>   permissions で**明示的に許可**しており、deny リストにも入っていない
>   (deny は `appsettings.Production*` / `*.pfx` / `*.snk` / `secrets.json` / `rm -rf /` / `netsh` のみ)。
>   ファイル作成もコミットも成功する。
> - **拒否するのは GitHub 側。** push 時にサーバが返す:
>   `refusing to allow a GitHub App to create or update workflow .github/workflows/ci.yml`
>   `without workflows permission`
>   → push に使われる **GitHub App トークンに `workflows` スコープが無い**ことが原因。
>
> 履歴の 13 秒 revert も、同じ拒否を受けて push を通すためにローカルで戻した結果と考えられる。
> **重要**: この commit を作ったまま放置すると、以降**そのブランチへの全 push が失敗する**。
> 試して拒否されたら `git reset --hard HEAD~1` で戻すこと。
>
> **必要な対応(いずれか一つ)**:
> 1. リポジトリ所有者が GitHub App / インストールに **`workflows` 権限を付与**する
> 2. 所有者自身が手元から push する:
>    `mkdir -p .github/workflows && cp docs/ci/*.yml .github/workflows/ && git add .github/workflows && git commit && git push`
>
> エージェント側の作業(ワークフロー YAML の整備・一本化・`.slnf` 修正・設置手順の文書化)は
> **すべて完了している**。残るのは上記の権限操作のみ。
>
> **2026-07 第4パス更新**: CI 設定の**二重管理は解消した**。`ci/github-workflows/`(2026-06-04 版、
> `claude/**` ブランチ対応と Windows ソリューションフィルタを欠く旧版)を削除し、
> **`docs/ci/` を正本に一本化**した。`docs/ci/README.md` に設置手順・設置後の TODO
> (README バッジ復活、テストバッジの実測値化、本 §0 のクローズ)を記載済み。
> 併せて、プロジェクト削除で `*.slnf` に壊れた参照が残ると**設置直後に `dotnet restore` が
> 失敗する**問題を実際に踏んだため(Android/iOS 削除時)、`tools/verify.sh` に
> `.slnf` 検証を追加した。設置前に `bash tools/verify.sh` を実行すること。
> **残る作業は所有者による `.github/workflows/` への配置のみ。**
>
> `docs/build-blockers-2026.md` も「CI を `.github/workflows/` へ設置して実走させるのが次の最優先」と
> 明記済みだが未達のまま。**このリポジトリで行われた変更は一度も実際の GitHub Actions CI で
> 検証されていない**(検証は `python3`/`grep` による静的チェックのみ。
> 2026-07 以降は `bash tools/verify.sh` に集約済み)。
> 検証: `ls .github/workflows/ 2>&1`(存在しないはず)/ `git log --oneline --all -- .github/`。

**中心的な発見**: このコードベースには「Core にクラスがあり単体テストが通る(=実装されている)」が
「App/CLI のどこからも呼ばれておらずユーザーが到達できない(=機能していない)」サービスが
多数存在する。ROADMAP.md はかつてこれらを完了 `[x]` と申告していた(2026-07 に訂正済み)。

---

## §1 過剰 — 製品(App/CLI)から到達不能(SDK 経由でのみ出荷)

### 1a. 完全孤立サービス(執筆時 11 個 → **現在 2 個**、いずれも理由付きの意図的な保持)

> **✅ 2026-07 第4パスで「孤立サービス」問題は実質的に解消した。**
> 配線 7 件(`OweSelectionService`/`RegulatoryDomainService`/`RetryPolicy`/`SignalIconService`
> /`CatImportService`/`Hotspot20Service`/`PrivacyAdvisoryService`)と
> 削除 4 件(`KalmanRssiFilter`/`BeaconUptimeEstimator`/`WifiDirectService`/`GroupPolicyProvider`)により
> 11 個 → **2 個**。**残る 2 個はどちらも「なぜ残すか」を明記済み**であり、
> 「実装されているが機能していない」未整理コードは残っていない:
>
> | 残存 | 保持理由 |
> |---|---|
> | `AccessibilityAuditService` | テスト(`ThemeAccessibilityAuditTests`)から使用中 = 正当な用途。製品コードからの未参照は問題ではない |
> | `CaptivePortalService` | RFC 8908。プローブ方式の `HttpConnectivityChecker` と**重複ではなく補完**(下表参照) |
>
> 再測定コマンド(この結果は下記で再現できる):
> ```bash
> for f in src/MWC.Core/Services/*.cs; do n=$(basename "$f" .cs); \
>   [ "$(grep -rl "\b$n\b" src/ | grep -v "/$n.cs" | wc -l)" -eq 0 ] && echo "$n"; done
> ```

`src/` 内で自ファイル以外からの参照が**ゼロ**の Core サービス。テストは存在する(=壊れてはいない)が、
App/CLI という製品としては動いていない。検証コマンド:

```bash
# 例: RegulatoryDomainService の参照元を探す(自ファイルを除く)
grep -rl "\bRegulatoryDomainService\b" src/ | grep -v "/RegulatoryDomainService.cs"
# → 出力が空 = 孤立(ただし直後の SDK 注記を必ず読むこと)
```

> **🔴 上記の注記は誤りだった(2026-07 第4パスで検証)**: 下の ⚠ ブロックは
> 「SDK 利用者に公開 API として出荷済みなので削除は SemVer メジャー要」と述べているが、
> **`MWC.SDK` は NuGet に一度も公開されていない**。検証:
> `https://api.nuget.org/v3-flatcontainer/mwc.sdk/index.json` と
> `https://api.nuget.org/v3/registration5-semver1/mwc.sdk/index.json` がともに **404**。
> さらに `MWC.SDK` をビルド・公開する CI もスクリプトも存在しない
> (`grep -rl "MWC.SDK" --include=*.yml --include=*.ps1 --include=*.sh .` の結果は
> ドキュメントのみ。そもそも §0 のとおり `.github/workflows/` 自体が無い)。
> `<Version>3.12.0</Version>` は宣言であって出荷実績ではない。
>
> **したがって「公開 API だから消せない」という制約は実在しない。** 消費者はゼロであり、
> 破壊する対象が存在しない。この誤った制約が下表の「推奨アクション」列を広範に
> 縛っていた(本セッションの前半でも、この注意書きを額面どおり受け取って
> `KalmanRssiFilter` の削除を見送っている)。
> **削除判断は純粋に製品としての要否だけで行ってよい。**
> 将来 SDK を実際に公開する場合は、その時点の Core をもって v1 とすればよく、
> 未公開パッケージの互換性を守る理由はない。
>
> 以下は誤りと判明した元の注記(経緯保存のため残す):

> **⚠ 重要な訂正(2026-07 第2パスで判明 — 上記のとおり前提が誤り)**: `sdk/MWC.SDK.csproj` は
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
| ~~`RegulatoryDomainService`~~ | 6GHz 帯の国別チャネル表示 | **✅ 配線済み(2026-07)** | `NetworkDetailViewModel.RegulatoryLabel`(6GHz ネットワークのみ表示、`RegionInfo.CurrentRegion` からシステムロケールで国を自動推定)。テスト: `NetworkDetailViewModelVpnEapWiringTests.cs` に追加 |
| ~~`CatImportService`~~ | eduroam CAT XML インポート | **✅ 配線済み(2026-07 第4パス)** — CLI `mwc import-cat` | XXE/DTD 対策済みの丁寧な実装。品質は高い。配線時に `BuildEduroamSpec` のマッピング誤り(匿名 ID が `Username` に入っていた)を発見・修正 — 未配線だったため露見していなかった。テスト: `tests/MWC.Core.Tests/CatImportWiringTests.cs` |
| ~~`OweSelectionService`~~ | 同一 SSID の Open/OWE ペア統合 | **✅ 配線済み(2026-07)** | `AdapterViewModel.RefreshAsync`・`AllAdaptersOverviewViewModel.AdapterPanelViewModel.RefreshAsync`・CLI `mwc scan` の3箇所に挿入。既知の限界(Open 側が実際に接続中でも無条件除外される稀なエッジケース)をサービス自身の XML doc に明記。テスト: `tests/MWC.Core.Tests/OweWiringTests.cs` |
| ~~`Hotspot20Service`~~ | Passpoint / キャリア Wi-Fi | **✅ 配線済み(2026-07 第4パス)** — CLI `mwc passpoint`。ブロッカーだった 802.11u Interworking 検出を `BeaconIeParser` に実装し、既存の IE パイプライン(`WlanBssIeProvider` → `BeaconEnrichmentService` → `BeaconIeApplier`)経由で `BssInfo.HasInterworkingElement` が埋まるようにした。テスト: `PasspointWiringTests.cs` / `InterworkingIeTests.cs` | 日本キャリア(au/SoftBank/docomo)プリセット付き。2026-H2 追補: OpenRoaming が主流化中(WBA 2025 調査で回答企業の81%が導入計画)で配線価値は上昇傾向だが、ブロッカー(802.11u Interworking IE 抽出未実装)は不変 |
| ~~`WifiDirectService`~~ | Wi-Fi Direct P2P | **✅ 削除済み(2026-07 第4パス)** | 依存する `WindowsWifiDirectAdapter` が存在せず動作不能。加えて Wi-Fi Direct は**デバイス間 P2P** であり、CLAUDE.md の Why(各アダプターの SSID 一覧/接続を独立管理)とは別の製品 capability。型定義(`IWifiDirectAdapter`/`WifiDirectDevice` 等)も同ファイルに閉じていたため巻き添えなし。**復活させる場合はプラットフォーム実装とセットで、実機検証込みで行うこと**(`git log --diff-filter=D -- src/MWC.Core/Services/WifiDirectService.cs`) |
| `CaptivePortalService` | RFC 8908 captive portal API | **保持**(2026-07 第4パスで削除を検討し見送り) | `HttpConnectivityChecker` はプローブによる**推測**、RFC 8908 は AP が返す**構造化メタデータ**(会場情報・残り時間等)で、重複ではなく補完関係。2026-07 にキャプティブポータル時の VPN 勧告を実装した(`VpnAdvisoryService.behindCaptivePortal`)ことで、ポータル情報を充実させる価値はむしろ上昇した |
| ~~`KalmanRssiFilter`~~ | RSSI 平滑化 | **✅ 削除済み(2026-07 第4パス)** | 同目的の `SignalQualityPredictor`(EMA 方式)が既に配線済みで、未配線の重複実装だった。SemVer 懸念は上記のとおり架空。**カルマンは EMA より優れた手法なので、平滑化を強化したくなったら git 履歴から復元して EMA を置き換えること**(`git log --diff-filter=D -- src/MWC.Core/Services/KalmanRssiFilter.cs`)。ただしそれは挙動が変わる変更であり、実機で検証できるセッションで行うこと |
| ~~`RetryPolicy`~~ | 接続リトライ | **✅ 配線済み(2026-07)** | `AdapterConnectExtension.ConnectWithAppleFlowAsync` に配線。一時的失敗はジッター付きバックオフで自動再試行(最大2回)、決定的失敗はユーザー承認ダイアログへ。`IsRetriable` の分類漏れ4件も同時に修正 |
| ~~`SignalIconService`~~ | 信号アイコン選択 | **✅ 配線済み(2026-07)** | `NetworkItemViewModel.Bars` が独自閾値(75/50/25/>0)の重複実装を捨てて `SignalIconService.Describe(Signal).Bars` に委譲。段階基準は Core の正式定義(80/60/40/20、WCAG 1.4.1 意図の設計)に一元化。境界値付近のバー表示が僅かに変化(例: quality 76 は旧4本→新3本)するのは意図的。`TextLabel`(英語ハードコード → i18n 規約違反になる)と `AccentHex`(テーマブラシ経由が確立方針)は使用しない。テスト: `tests/MWC.Core.Tests/SignalIconWiringTests.cs`(境界値10点+全数一致ループ) |
| ~~`BeaconUptimeEstimator`~~ | AP 稼働時間推定 | **✅ 削除済み(2026-07 第4パス)** | TSF タイムスタンプを供給する層がどこにも無く、**原理的に一度も動作しえなかった**。監査自身が以前から削除候補としていたものを、架空の SemVer 制約が解けたため実行した |
| `AccessibilityAuditService` | WCAG コントラスト計算 | **現状維持** | 2026-07 に `tests/MWC.Core.Tests/ThemeAccessibilityAuditTests.cs` から使用開始(CI でテーマ色を検証)。製品コードからは未参照だが、これは正当な使途。SDK にも同梱 |

### 1b. 準孤立(参照が形式的なもののみ)

| サービス | 唯一の"参照" | 実態 |
|---|---|---|
| ~~`GroupPolicyProvider`~~ | — | **✅ 削除済み(2026-07 第4パス)**。Intune/GP ポリシー読取だが、どのコードも呼んでいなかった = **管理者がポリシーを設定しても何も起きない**状態で、存在しない管理性を主張していた。さらにこのサービスだけのために `Microsoft.Win32.Registry` パッケージ参照が Core に入っており、削除と同時に依存も除去した(Core 内の Registry 利用は他にゼロであることを確認済み) |
| ~~`PrivacyAdvisoryService`~~ | — | **✅ 配線済み(2026-07 第4パス)** — CLI `mwc privacy`。勧告ロジックは純 Core でテスト可能、唯一プラットフォーム依存の「現在の MAC モード検出」は import-cat と同じ分解でユーザー入力(`--mac-mode`)に代替した。**残る限界**: MAC モードの**自動検出**は依然 Windows 実装が必要(入れば `--mac-mode` の既定供給元になる)。テスト: `PrivacyCliContractTests.cs` |
| `ISecretProtector` / `DpapiSecretProtector` | `App.xaml.cs`/CLI `Program.cs` の **DI 登録のみ** | `Protect`/`Unprotect` の呼び出し元ゼロ。§2b 参照 |

### 1c. プラットフォームスタブ(ROADMAP は訂正済み、ここは一覧性のための集約)

- `src/MWC.Platform.MacOS/CoreWlanWifiService.cs` — **半実装プロトタイプ**。スキャン/接続は動くが
  `RegisterProfileAsync` が `false` 固定のため、パスフレーズ必須ネットワークへは
  `ConnectionExecutor` が接続前に失敗させる。**注意**: 安易にスタブを `true` にしても直らない
  (詳細な罠の解説がファイル内コメントに記載済み。`NmcliWifiService` の Linux 実装が正しい手本)。
- ~~`src/MWC.Platform.Android/`、`src/MWC.Platform.iOS/`~~ — **2026-07 に削除済み**。
  全メソッドが空配列/false/失敗を返す完全スタブで、製品(App/CLI)からの参照はゼロ、
  `MWC.sln` の登録以外に存在理由が無かった。CLAUDE.md の Why が
  「**Windows PC** で複数の無線アダプターを管理する」である以上、
  動かない iOS/Android 実装を抱えることは「サポートしている」という誤った印象を与えるだけで、
  ビルド対象と読解対象を増やす純粋な負債だった。
  唯一の資産だった API 参照コメントは git 履歴に残っている
  (`git log --diff-filter=D -- src/MWC.Platform.Android`)。
  **復活させる場合は、動作する実装と実機検証をセットにすること。**

### 1d. 配線されているがデータ源が空(2026-H2 Web 調査で発見)

孤立サービスとは異なる新パターン: サービス自体は正しく App に配線されているが、
入力データを供給するプラットフォーム層のコードが存在しないため**常に無効な結果を返す**。

- **MLO(Wi-Fi 7 マルチリンク)表示** — `MloAnalyzerService` は
  `NetworkDetailViewModel.Load()` から正しく呼ばれ、GUI にも `MloLabel`/`HasMlo` として
  配線済み(ROADMAP は「Wi-Fi 7 MLO サポート」を `[x]` 完了と申告)。しかし
  `WifiNetwork.MloLinks` を実際に埋めるプラットフォームコードが Windows/Linux/macOS
  いずれにも存在しない(検証: `grep -rn "MloLinks\s*=" src/MWC.Platform.*` → 0件)ため、
  `MloAnalyzerService.Analyze()` は `network.MloLinks.Count == 0` で常に早期リターンし、
  GUI の MLO 行は実機で一度も表示されたことがないと推測される。
  **解決策は調査済み**(依存ライブラリ `ManagedNativeWifi` v3.0.1+ の
  `NativeWifi.GetRealtimeConnectionQuality` で実測データ取得可能。詳細と実装しなかった
  正確な理由 — 名前空間衝突・情報源間の型定義不一致・コンパイル検証不能な環境という
  3点 — は `docs/arxiv-improvement-analysis.md` §2026-H2追補 を参照)。
  **次のアクション**: dotnet/Windows 実機検証が可能なセッションで実装すること。
  **2026-07 第4パス — 一部は分解できた**: 「MLO 対応か否か」は 802.11be Multi-Link 要素
  (拡張要素、Element ID Extension 107)としてビーコンで**広告される**ため、
  `BeaconIeParser.HasMultiLink` で検出し `WifiNetwork.IsMlo` に配線した
  (実装時にパーサーが拡張要素の Ext ID を読んでいなかったことも判明し、併せて対応)。
  スキャン一覧で Wi-Fi 7 AP を見分けるにはこれで足りる。
  **残るのはリンク詳細 (`MloLinks`) のみ**:
  `MloLink` は `Rssi`(リンクごとの実測受信強度)を要求する。これはビーコンの
  Multi-Link 要素には含まれない**実測値**であり、接続中のランタイム API からしか得られない。
  802.11u Interworking が Core に切り出せたのは、あれが「広告される静的な能力情報」
  だったからで、MLO のリンク品質は本質的に実機依存である。

### 1e. メンバ単位の重複(2026-08 に発見。ファイル単位の孤立検出では原理的に見えない)

§1a〜§1d はいずれも**ファイル/サービス単位**の到達性の話だった。これはその一段下の層で、
**ファイルは使われているのに、その中の一部のメンバだけが誰からも呼ばれず、
別のファイルが同じ処理を再実装している**というパターン。

- **WMM デコードが 2 実装あった** — `WmmParser.ParseParameters` / `ParseQosInfo` は
  製品コードから**一度も呼ばれていなかった**。`BeaconIeParser.DecodeVendorSpecific` が
  AC パラメータ展開を丸ごと自前で持っており、`WmmParser.ParseAcParams` と
  1 バイト単位で同一のコードだった。`WmmParser.cs` 全体が孤立して見えなかったのは、
  同ファイルが宣言する `WmmParameters` / `WmmAcParam` 型を `BeaconIeParser` が
  使っていたため — **型は使い、ロジックは使わない**という形。

  害は「死にコードがある」ことではなく、**テストが動いていない方の実装を保証していた**こと。
  `WmmParserTests`(バイトレベルのゴールデン一式)が検証していたのは `WmmParser` 側で、
  実機で走るのは `BeaconIeParser` 側だった。片方だけを直せば両者は静かに食い違う。

  **対応(2026-08)**: 本体 1 個を受け取る入口 `WmmParser.ParseParameterBody` /
  `ParseQosInfoBody` を切り出し、`BeaconIeParser` はそこへ委譲する。
  `BeaconIeParser` の 1 パス走査(このクラスの存在理由)は保たれ、実装は 1 つになった。
  不変条件は `WmmSharedDecodeTests` が固定する(2 つの入口が同じ答えを返すこと)。

**なぜ長く見えなかったか**: `tools/verify.sh` の孤立検出が**コメント中の言及を参照と数えていた**。
`BeaconIeParser.cs` の冒頭コメントが `WmmParser` を名指ししているだけで「配線済み」と判定され、
候補にすら挙がらなかった。検出側も併せて修正した(コメント行を除外し、
拡張メソッドを収めた static クラスは public メンバ名でも参照とみなす)。

**次に同じ形を探すなら**: 「型は共有しているがロジックは各自が持っている」ペアを疑うこと。
ファイル単位の grep では永久に見えない。

- **死んだ翻訳キー** — 定義→参照の向きは誰も検査しておらず、リファクタで置き換えられた
  キーが 2026-08 の実測で 18 個 × 15 ロケール = 270 エントリ残っていた
  (`Auth_*` → SecurityBadgeService、`Label_*` 詳細ペイン → `Detail*` への移行の取り残し)。
  削除し、verify.sh に定義→参照チェックを追加。**罠**: `GetTroubleshootingAdvice` が
  `{prefix}_Title/_Reason/_Steps` を動的に組むため、素朴な grep は `Trouble_*` の
  全キー(2026-08 時点で 21 キー)を死骸と誤判定する。チェックはこの
  プレフィックス剥がしを織り込んである。

### 1f. 「案内はあるが実装が無い」— 2 つの表が別々に維持されている形

§1e が「型は共有、ロジックは重複」だったのに対し、これは
**同じ事実を 2 箇所が別々に宣言していて、片方だけが真**という形。

- **F1 ヘルプが案内するキーが押しても効かなかった** — `KeyboardShortcutService` の定義表
  (= ヘルプダイアログの表示元)には `Ctrl+Tab` / `Ctrl+Shift+Tab`(アダプター切替)があるが、
  実際にキーを処理する `MainWindow.OnKeyDown` のスイッチには case が無かった。
  アダプタータブは `TabControl` ではなく `ListBox` なので WPF の標準動作も効かない。
  逆に `Ctrl+Shift+A`(全アダプター一覧)は**動くのにヘルプに載っていなかった**。
  README が「キーボードのみで完全操作可能」「WCAG 2.1 AAA」を掲げている以上、
  これは利便性の欠落ではなく**主張が事実と違う**状態だった。

  **対応(2026-08)**: 2 つを実装し、`Ctrl+Shift+A` をヘルプに追加(resx 15 ファイル)。
  `Up`/`Down` は `ListBox` の標準操作なのでハンドラに無いのが正しく、
  チェック側で明示的に除外している(黙って無視しない)。
  同時に、呼び出し元ゼロだった `CreateBindings` を削除した — コマンド表を
  **翻訳済みの表示文字列**で引く設計で、UI 言語が変わるとキーが変わるため
  そもそも動作し得なかった。

**再発防止**: `tools/verify.sh` が 2 つの表を突き合わせ、どちらか一方にしかない
ショートカットがあれば失敗する。

- **`mwc eap-stats` / `mwc vpn-advice` が補完スクリプトに無かった** — 実装があり README にも
  載っているのに、`completions/mwc.bash` と `completions/mwc.ps1` の双方から漏れていた。
  ここは表が **3 つ**(実装・bash・PowerShell)ある。コマンドを足しても補完を忘れた時点では
  何も壊れないため、「動くのに Tab で出てこない」状態が静かに残る。
  **対応(2026-08)**: 両方に追加し、`tools/verify.sh` が `root.AddCommand(...)` から
  コマンド名を解決して両スクリプトと突き合わせる(どちらの向きの不一致でも失敗)。

**なぜファイル単位の監査で見つからなかったか**: 両ファイルとも正しく配線されており、
到達性の観点では何の問題も無い。食い違っているのは**内容**であって接続ではない。
「同じことを宣言している表が 2 つある」箇所を探すのが、この種を見つける唯一の方法。

---

## §2 不足 — 必要なのに欠けている

### 2a. GUI 配線の不足(CLI からしか使えない助言機能)

`SecurityAdvisoryService` だけが GUI(`src/MWC.App/ViewModels/NetworkDetailViewModel.cs`)に
配線されており、同系統の以下は CLI 止まり。GUI ユーザーはこれらの存在を知る手段がない:

| サービス | 現在の到達手段 | 状態 |
|---|---|---|
| `VpnAdvisoryService` | CLI `mwc vpn-advice` **+ GUI 詳細パネル** | **✅ 配線済み(2026-07)**。`NetworkDetailViewModel.VpnAdviceLabel` として表示 |
| `EapAuthStatsService` | CLI `mwc eap-stats` **+ GUI 詳細パネル** | **✅ 配線済み(2026-07)**。`NetworkDetailViewModel.EapStatsLabel`(記録がある場合のみ表示) |
| `PrivacyAdvisoryService` | CLI `mwc privacy` | **✅ 配線済み(2026-07 第4パス)**。GUI 側は未配線(下記参照) |

`VpnAdvisoryService`/`EapAuthStatsService` は `_secAdvisor` と同じパターン(static readonly
フィールド + `Load()` 内で `Analyze`/`GetAll` 呼び出し → ラベルプロパティ)で
`src/MWC.App/MainWindow.xaml` の詳細パネルに追加済み。新規 resx キー7個を全15ロケールに
追加(`LocaleKeyConsistencyTests` で完全性を検証)。テストは
`tests/MWC.Core.Tests/NetworkDetailViewModelVpnEapWiringTests.cs`。

**`PrivacyAdvisoryService` の配線状況(2026-07 第4パスで更新)**:
かつてここには「MAC ランダム化状態を検出するコードがプラットフォーム層に存在しないため
配線できない」と書いてあったが、**その前提は過大だった**。`Analyze(MacAddressMode, WifiNetwork)`
自体は純 Core のロジックで、プラットフォーム依存なのは**現在の MAC モードの検出だけ**。
`mwc import-cat` と同じ分解(**プラットフォームが供給できない値はユーザーが供給する**)を適用し、
CLI `mwc privacy --mac-mode <hardware|random-per-network|random-daily>` として配線済み。

**残る限界**: MAC モードの**自動検出**は依然として Windows 実装が必要
(「ランダムハードウェアアドレス」設定の読み取り)。実装されれば `--mac-mode` の
既定供給元になり、ユーザー入力は上書き用に残せる。**GUI 側も未配線**
(`VpnAdvisoryService`/`EapAuthStatsService` と同じ詳細パネル方式で追加可能)。

**`CatImportService` が配線できない理由(2026-07 調査で判明した、より根本的な欠落。CLI 側は
2026-07 に解消済み)**:
当初は「GUI にインポートダイアログを追加するだけ」の小さな作業と見積もっていたが、調査の結果
**GUI (`ConnectDialog`) も CLI (`mwc connect`) も、802.1X Enterprise 認証(PEAP/EAP-TTLS)の
ユーザー名・パスワード入力に一切対応していない**ことが判明した。
`ConnectDialog` は Personal(PSK/WEP)/Open/OWE のパスフレーズ入力のみに対応。さらに
`CertificatePickerDialog`(EAP-TLS 用クライアント証明書選択、
`src/MWC.App/Views/CertificatePickerDialog.xaml.cs`)自体も
どの接続フローからも呼び出されておらず孤立している(`L.cs` の文字列参照のみ)。

> **✅ CLI 側は 2026-07 に配線完了**: `mwc connect` に `--eap-type`/`--username`/`--domain`/
> `--server-name` を追加し、Enterprise 接続に対応した(`src/MWC.Cli/Program.cs` の `BuildConnect`。
> オプション数が SetHandler のジェネリック上限を超えるため InvocationContext 束縛へ変更)。
> Core 層(`WifiProfileSpec`/`ProfileXmlBuilder`/`ConnectionExecutor`)は元から完全対応済みで、
> 欠けていたのは CLI のオプション表面だけだった。契約テスト:
> `tests/MWC.Core.Tests/CliEnterpriseSpecContractTests.cs`。
> **残るは GUI 側**(`ConnectDialog` への Enterprise 入力欄追加)と `CertificatePickerDialog` の
> 接続フロー配線で、これらが揃えば `CatImportService` の配線が「小差分」になる。

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

- **疎通確認プローブ先が固定・代替なし**(2026-07 第4パスで発見。**要 Windows/dotnet セッション**)

  `HttpConnectivityChecker`(`src/MWC.Platform.Windows/`)の
  `ProbeUrl = "http://www.msftconnecttest.com/connecttest.txt"` は `const` で、
  代替 URL も上書き手段も無い。

  **判定分岐そのものは妥当**で、むしろ関連ソフトウェアより良い点がある:
  例外(DNS 失敗・TCP 拒否・タイムアウト)を「ポータル無し + 疎通無し」と正しく区別しており、
  Android の「204 以外はすべてポータル」より細かい。`AllowAutoRedirect = false` で
  302 を捕捉する点、本文完全一致を要求する点も正しい(200 + 独自 HTML を返すポータルを
  「疎通あり」と誤認しない)。

  **問題は単一プローブ依存**。msftconnecttest.com は一部の国・企業ファイアウォールで
  到達不能であり、その環境では例外経路に落ちて **実際には疎通があるのに常に
  「インターネット無し」と報告し続ける**。captive portal 検出の一般的な弱点として
  「walled garden がチェックをブロックすると端末は接続が死んでいると誤認する」
  「特定 URL への依存は第三者サーバ依存という課題を生む」ことが知られており、
  NetworkManager が接続性チェック URI を設定可能にしているのはこのため。

  なお **接続成否は左右しない**(`WindowsWifiService` は `ConnectionResult.Ok(...)` を返し、
  疎通結果は情報として渡すのみ)。影響は表示・通知の誤りに留まる。

  **推奨する対応**(実装は Windows/dotnet で検証できるセッションで行うこと):
  環境変数での上書きを追加する(既に `MWC_PASSWORD` で確立済みの流儀)。
  例: `MWC_CONNECTIVITY_URL` / `MWC_CONNECTIVITY_EXPECT`。
  ただし **URL だけ上書きされ期待本文が未指定の場合に本文検査を省いてはならない** —
  ポータルは 200 + 独自 HTML を返すため、本文を見ないと「疎通あり」と誤認する。
  その場合は Android の `generate_204` と同じく「2xx かつ本文が空」のみ疎通ありとするのが安全。

  **この環境で実装しなかった理由**: `tests/` には `MWC.Core.Tests` しか無く、
  Platform.Windows のコードは検証できない(`AI-SESSION-HANDBOOK.md` §4 の方針)。
  検証不能な変更を疎通判定という中核経路に入れる方が、限界を文書化するより有害と判断した。

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
| `trusted-aps.json` が BSSID を保存しない(再起動後は検査 2「未知 BSSID」が効かない) | 学習した BSSID を捨てているので検出が弱い、保存すべきに見える | **意図的。BSSID を永続化してはならない**。BSSID は AP の MAC であり Apple/Google の Wi-Fi 測位システムで**位置に変換できる**(任意の MAC を問い合わせると位置が返る弱点が報告され、研究では 1 年で 20 億件規模が地理特定されている)。保存すると当該ファイルは事実上「ユーザーが訪問した場所の履歴」になる。本製品は `PrivacyAdvisoryService` で MAC 追跡リスクを arXiv 引用付きで警告する立場であり、自ら位置追跡可能な識別子を平文で残すのは方針矛盾。ハッシュ化案は検査 2 が完全一致と OUI 前方一致の両方に使うため成立せず(内部表現の変更は public `GetTrustedBssids` と既存テストに波及)、「保存しなくてよいものは保存しない」を採用した。**残る限界**: 攻撃者の OUI が OUI DB に無い場合、再起動直後は降格の 1 件のみ = `Suspicious` 止まりで自動再接続は中止されない。検査 3(降格)・検査 4(ベンダー相違)は永続化されるため、両者が揃えば `HighRisk` に到達し中止される。契約は `AutoReconnectEvilTwinGuardTests` で固定済み |
| ネットワーク選択にヒステリシスが無い | RSSI は変動が大きく、瞬間値で選ぶと AP 間を往復する「スラッシング」を起こす — Cisco Optimized Roaming 等が典型的に 8 dB のヒステリシスを設ける理由 | **MWC では実害が発生しない構造**。2026-07 に全経路を追跡して確認: (1) `NetworkRecommendationEngine.Rank/Recommend` は **CLI の表示順にのみ**使われ、接続を駆動しない(`grep -rn "\.Recommend(\|\.Rank(" src/`)。表示順が僅差で入れ替わっても接続は起きない。(2) 自動再接続の実際の選択は `AdapterPreferencesService.PickBestSsid` で、**ユーザーが明示設定した `AutoConnectPriority` → `PinnedSsids` の順**に決まり信号強度を参照しない。したがってヒステリシスを足しても防ぐべき往復が存在せず、投機的な複雑化になる。**将来 Rank が自動接続を駆動するようになったら、その時点で再検討すること** |

### 既知の制限(意図的に未対応 — 安易に「修正」しないこと)

**`adapters.json` のアダプター設定は単調増加する**
(`AdapterPreferencesService._store` は `Dictionary<Guid, AdapterPreferences>` で削除機構を持たない)。
SSID ピン留めには 20 件上限があるのにアダプター項目には上限も prune も無い、という非対称は
実在する。USB ドングルを差し替え続ける利用者(本製品の主対象)で件数が増え続ける。

ただし**安易な prune はユーザー設定の破壊になる**: 「今スキャンに現れない = 恒久的に不要」
ではなく、ドングルを再接続したときに以前の設定(ピン留め・バンド固定・ラベル)が
そのまま戻るのは*正しい挙動*である。適切な設計は最終使用日時の記録 → 一定期間未使用のみ
削除、あるいは明示的な削除 UI であり、いずれも `AdapterPreferences` のスキーマ変更
(+ 既存 JSON のマイグレーション)を伴う。1 レコードは数百バイト規模で実用上の圧迫は
当面ないため、スキーマ変更を要する対応として意図的に見送っている。

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
7. Enterprise(802.1X)認証情報入力 — **CLI 側は 2026-07 完了**(`mwc connect --eap-type/
   --username/--domain/--server-name`)。**残: GUI 側**(`ConnectDialog` への Enterprise 入力欄
   追加 or 新規ダイアログ)+ `CertificatePickerDialog` の接続フロー配線。これが完了して初めて
   `CatImportService` の配線が「小差分」になる。§2b の SecureString 裁定と合わせて検討すべき
   (資格情報の安全な取り扱いという同じ論点を含むため)。

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

## §6 総括 — 長所・短所・改善点(2026-08 / ソクラテス問答パス)

> 手法: 製品が**主張していること**を 1 件ずつ取り上げ、「それはなぜ真だと言えるか」を
> 実測で問い直した。答えられない主張は虚偽であり、直すか取り下げる。
> 検証コストが人手に依存する主張は `tools/verify.sh` に移して自動化した(マスク⑤)。

### 6a. 長所 — 実測で裏の取れた主張

| 主張 | 何で確かめたか | 状態 |
|---|---|---|
| 多言語 UI(名前付き 14 ロケール + 中立ベース) | `Strings.*.resx` を実数え。全ロケールでキー集合が一致 | ✅ `verify.sh` が常時検証 |
| resx キーの参照が健全 | 文字列リテラル `L.Get/Format("…")` と `L.cs` アクセサの**両方**を resx と突合 → 欠落 0 | ✅ |
| ADR 25 件 | `docs/adr/*.md` を実数え | ✅ `verify.sh` が常時検証 |
| CLI コマンドが補完から漏れない | 実装(`root.AddCommand`)・bash・PowerShell の 3 表を突合 | ✅ `verify.sh` が常時検証 |
| F1 ヘルプのショートカットが実際に効く | ヘルプ定義表と `MainWindow.OnKeyDown` を突合 | ✅ `verify.sh` が常時検証 |
| ドキュメント中の `mwc <cmd>` が全て実在 | 全 `docs/*.md` + README を実装コマンド集合と突合 → 不一致 0 | ✅ |
| JumpList / トースト通知 / ARM64 | `JumpListService` / `NotificationService` / `MWC.App.csproj` の `RuntimeIdentifiers` を確認 | ✅ 実装あり |
| `netsh.exe` / WMI 不使用 | 依存とコードを検索 | ✅ CLAUDE.md の禁止事項を遵守 |

### 6b. 短所 — 実測で確認された弱点

| 弱点 | 実測 | 深刻度 |
|---|---|---|
| **CI が一度も実走していない** | `.github/workflows/` が存在しない。本セッションの全コミットは静的検証のみ | 🔴 最大。§0 |
| GitHub Release が無く、**署名済み配布物が 1 つも存在しない** | タグ push は 403、release 作成ツールも無し。README/SECURITY.md は Sigstore 署名・SLSA provenance・SBOM を現在形で謳っていた(2026-08 に是正、`docs/ci/release.yml` を用意) | 🔴 主張と実態の乖離としては最大 |
| MLO リンク詳細が空 | `MloLinks` を埋めるプラットフォームコードが無い(§1d) | 🟡 Windows 実機待ち |
| 現在の MAC を自動取得できない | 判定ロジックは 2026-08 に Core 化済み(`MacAddressModeInference`)。`mwc privacy --mac` で今日使える。残るのは自動供給の配線のみ | 🟢 大部分解消 |
| OUI DB が凍結 | 更新スクリプトはあるがスケジュールが無かった | 🟢 本パスで是正(`docs/ci/oui-update.yml`) |
| Dependabot が空のエコシステムを監視 | `automerge:`(スキーマに無いキー)で設定ごと無効の可能性 + 対象が github-actions のみで `.github/workflows/` は空。実依存の NuGet は未監視だった | 🟢 **本パスで実際に修正・push 済み**(`.github/workflows/` 配下ではないため権限が通った) |
| bn/hi/ta 訳がネイティブ未レビュー | 機械翻訳のまま。キー完全性のみ保証 | 🟢 §3 に既載 |
| `ExportService` の波括弧警告 | 補間文字列の入れ子による**偽陽性**と手検証済み。正規表現で C# は字句解析できない | 🟢 advisory のまま正しい |

### 6c. 改善点 — マスク 5 段階のどれで処置したか

| # | 改善点 | 段階 | 処置 |
|---|---|---|---|
| 1 | 「月次自動更新」を謳うが自動実行が無い | ①要件を疑う → ⑤自動化 | 主張を実態に合わせ、`docs/ci/oui-update.yml` を用意。設置すれば主張が真になる |
| 2 | WMM デコードが 2 実装(テストは動かない側を検証) | ③単純化 | 本体レベル入口へ委譲し 1 実装に(§1e) |
| 3 | 案内だけあって効かないショートカット | ①要件を疑う | 実装し、両表を `verify.sh` で固定(§1f) |
| 4 | 動くのに補完に出ないコマンド / 存在しないオプション | ⑤自動化 | 3 表突合を `verify.sh` に追加 |
| 5 | 死んだコード(`CreateBindings` / `UpdateNetworkMenu` / 未使用翻訳キー) | ②削除 | 削除。特に `UpdateNetworkMenu` は**呼ぶと黙って何もしない** no-op だった |
| 6 | 数値の複製がドキュメント各所で腐る | ⑤自動化 | 単一の真実の源を README に一本化し、複製を `verify.sh` が禁止 |
| 7 | 孤立検出がコメント言及を「配線済み」と誤判定 | ③単純化 | 判定を修正。これが #2 を隠していた |

**未処置(意図的)**: §2b の `SecureString` 方針は**ユーザー裁定待ち**。
利害得失が対立する設計判断であり、AI が勝手に決めない。

### 6d. 問答法から得た判定の教訓(§1e / §1f と同列)

1. **「使われている」はコメント中の言及では成立しない。** 名前を grep するだけの
   到達性判定は、ドキュメントコメントに名前が出るだけで「配線済み」と誤る。
   実際にこれが WMM の二重実装を隠していた。
2. **「未使用キー」の判定は動的構築キーで偽陽性になる。** `Trouble_*` は
   `L.cs` が `"Trouble_" + 失敗種別 + "_Title"` の形で組み立てるため、
   リテラル検索では未使用に見える。**消す前に構築箇所を探すこと。**
3. **「OS の設定だから無理」は、効果が観測できるか確かめるまで結論ではない。**
   MAC ランダム化の検出は「OS の設定値そのものなので Core に切り出せない」と
   記録されていたが、誤りだった。必要なのは設定ではなく **効果**で、それは
   MAC アドレスの Locally Administered ビットに現れる。バイト列は存在した。
   ブロッカーの理由を書くときは「読めない値」ではなく
   「**その設定が効いているとき何が変わるか**」を問うこと(§4 の判定基準に追記済み)。
4. **「可能」と「実施している」は違う。** `oui-update.ps1` は自身のコメントで
   「schedule で月1回自動実行**可能**」と書いており、README はそれを
   「月次自動更新」と読み替えていた。スクリプトの存在は自動化ではない。


---

*最終更新: 2026-08 / 監査の詳細経緯は CHANGELOG.md `[Unreleased]` と git log を参照。*
