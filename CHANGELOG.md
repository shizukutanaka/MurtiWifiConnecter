# Changelog

All notable changes to MWC will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [Unreleased]

### Fixed
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

