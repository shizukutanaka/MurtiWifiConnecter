# MWC 競合ソフト & arXiv ギャップ分析 (2026)

> 本書は **既存の `docs/arxiv-improvement-analysis.md`(10カテゴリー×10項目)と `ROADMAP.md`
> に未掲載の改善点だけ**を、同種ソフトと 2024–2026 の arXiv 文献を参照して洗い出した差分(delta)。
> CLAUDE.md の方針(Wi-Fi に集中・量子/AI 等の派手機能は不採用)に適合するものに限定する。
> 各項目はコードベースを grep し「未実装」を確認済み。

調査日: 2026-06。優先度 P0=即実装 / P1=次期 / P2=将来。

---

## 1. 競合ソフト 機能差分(本書で扱うギャップに絞った比較)

| 機能 | Win標準 | WifiInfoView | inSSIDer | NetSpot | Acrylic Wi-Fi | **MWC 現状** |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| MAC ランダム化の表示/管理 | ✅(設定) | ❌ | ❌ | ❌ | ❌ | **❌ ギャップ** |
| 従量制(metered)接続の考慮 | ✅ | ❌ | ❌ | ❌ | ❌ | **❌ ギャップ** |
| WPS 有効 AP の検出・警告 | ❌ | △ | ❌ | ❌ | ✅(WPS/PIN表示) | **❌ ギャップ** |
| 負荷時遅延(bufferbloat/RPM) | ❌ | ❌ | ❌ | △ | ❌ | **❌ ギャップ** |
| Wi-Fi 8 (802.11bn) 能力バッジ | ❌ | ❌ | ❌ | ❌ | ❌ | **△ PHY名のみ** |
| 802.11bf センシング能力表示 | ❌ | ❌ | ❌ | ❌ | ❌ | **❌ ギャップ** |
| L4S/ECN・WMM(QoS)表示 | ❌ | ❌ | ❌ | ❌ | ❌ | **❌ ギャップ** |
| 信号履歴グラフ | ❌ | ❌ | ✅ | ✅ | ✅ | ✅ 実装済 |
| Evil Twin / Rogue AP 検出 | ❌ | ❌ | ❌ | ❌ | △ | ✅ 実装済 |

出典: NetSpot/Comparitech/itPRC 各 WiFi アナライザ比較 (2025–2026)、Acrylic Wi-Fi 製品ページ。

---

## 2. 新規改善点(既存分析・ROADMAP に未掲載)

### G1. [P0] MAC ランダム化 & プローブ要求プライバシー管理  〔プライバシー〕
- **背景**: ランダム MAC でも Probe Request 内の Information Element 指紋で 99% 近い精度で
  端末を再識別・追跡できることが近年示された。固定 MAC やランダム化無効は追跡リスク。
- **提案**: Windows のネットワーク別「ランダムなハードウェアアドレス」設定状態を**表示**し、
  固定 MAC のまま公共 SSID に接続している場合に注意を促す。プローブ追跡リスクの説明 UI。
- **出典**: arXiv [2412.10548](https://arxiv.org/abs/2412.10548)(Probe Request Fingerprinting)、
  [2408.01578](https://arxiv.org/html/2408.01578v1)(MAC De-Randomization)、
  [2206.10927](https://arxiv.org/abs/2206.10927)、[1703.02874](https://arxiv.org/abs/1703.02874)。
- **実装フック**: 新 `PrivacyAdvisoryService`(Core)。Windows 側は WlanAPI/レジストリで
  per-profile の MAC ランダム化フラグを参照。`SecurityBadgeService` にプライバシーバッジ追加。
- **適合性**: ✅ Wi-Fi 専用・表示主体。netsh/WMI 不使用方針に抵触しない。

### G2. [P0] 負荷時遅延(bufferbloat / responsiveness)グレード  〔品質計測〕
- **背景**: RTT/ロスだけでは体感品質を捉えきれない。IETF responsiveness(RPM)や Apple RPM、
  L4S/ECN は「負荷時遅延(working latency)」を重視。AQM が速度計測結果に与える影響も報告。
- **提案**: 既存 `NetworkQualityService`(RTT+ロス)を拡張し、**並列 TCP で輻輳を作りつつ
  RTT を測る** working-latency 計測を追加、RPM 値と A–F の bufferbloat グレードを表示。
- **出典**: IETF [draft-ietf-ippm-responsiveness](https://datatracker.ietf.org/doc/html/draft-ietf-ippm-responsiveness-03)、
  arXiv [2511.19213](https://arxiv.org/html/2511.19213)(AQM の速度計測影響)、L4S/ECN(RFC 9330 系)。
- **実装フック**: `NetworkQualityService.MeasureAsync` に `MeasureResponsivenessAsync` を追加。
  `QualityGrade` に bufferbloat 次元を併記。CLI `mwc quality --bufferbloat`。
- **適合性**: ✅ 既存品質計測の自然な深化。≤200 行で自前実装可。

### G3. [P1] WPS 有効 AP の検出と弱点警告  〔セキュリティ〕
- **背景**: WPS(特に外部レジストラ PIN)は Pixie-Dust 等で総当たり可能な既知の弱点。
  Acrylic 等の競合は WPS/PIN を表示するが、MWC は WPS の存在自体を扱っていない。
- **提案**: ビーコン/プローブ応答の WPS IE を解析し、WPS(PIN 方式)有効 AP に
  セキュリティ助言を表示。WPA3 でも WPS 併用時は注意。
- **出典**: 競合(Acrylic Wi-Fi)機能、WPS PIN の構造的脆弱性(WSC 仕様 + Pixie-Dust 既報)。
- **実装フック**: `SecurityAdvisoryService` に WPS チェック追加(IE パース可能な範囲で)。
- **適合性**: ✅ セキュリティ表示。能動的攻撃は行わない(検出・助言のみ)。

### G4. [P1] Wi-Fi 8 (802.11bn / UHR) 能力バッジ  〔次世代対応〕
- **背景**: 既存分析は NPCA(2504.15774)と AP 省電力(2411.17424)のみ。Wi-Fi 8 の核心である
  **Seamless Mobility Domain(SMD)によるシームレスローミング**と**マルチ AP 協調
  (C-SR/C-BF/C-TDMA/C-RTWT)** は未カバー。
- **提案**: AP/アダプターが広告する UHR/MLD 能力から、SMD 対応・マルチ AP 協調対応の
  **バッジ表示**(現状は PHY 名のみ)。将来のシームレスローミング診断の土台。
- **出典**: arXiv [2303.10442](https://arxiv.org/abs/2303.10442)(Wi-Fi 8 Primer / UHR)、
  [2501.03680](https://arxiv.org/abs/2501.03680)(Multi-AP Coordinated Spatial Reuse)。
- **実装フック**: `MloAnalyzerService` を拡張、`SecurityBadgeService`/`WifiNetwork` に能力フラグ。
- **適合性**: ✅ 表示主体。実機普及前でも能力検出は前方互換に有用。

### G5. [P2] 802.11bf Wi-Fi センシング能力の表示(表示専用)  〔次世代対応/プライバシー〕
- **背景**: 802.11bf は WLAN 信号で在室・動き・ジェスチャを検出する標準。サーベイ複数あり。
  「自分の AP/端末がセンシング能力を広告しているか」はプライバシー観点でも有用。
- **提案**: センシング対応の有無を**表示するのみ**(計測機能は持たない=派手機能化を回避)。
  センシングが presence/motion を検出しうる旨のプライバシー注記。
- **出典**: arXiv [2207.04859](https://ar5iv.labs.arxiv.org/html/2207.04859)(11bf Overview)、
  [2403.19825](https://arxiv.org/pdf/2403.19825)(Sensing Performance)、
  [2503.04637](https://arxiv.org/html/2503.04637v1)(11bf/11ax 共存)。
- **実装フック**: `WifiNetwork` に sensing-capable フラグ、バッジ表示のみ。
- **適合性**: ✅(表示専用に限定すれば)。CLAUDE.md の「Wi-Fi に集中」に合致。

### G6. [P1] 従量制(metered)接続の考慮  〔UX/省電力〕
- **背景**: Windows は接続を従量制としてマークできる。MWC の `AutoReconnectService` /
  `AppUpdateService` は metered を考慮せず、従量制回線で更新確認や再接続を行いうる。
- **提案**: 接続の metered フラグを参照し、従量制時はアップデート確認・重いバックグラウンド
  処理・自動再接続戦略を抑制。バッジ表示も。
- **出典**: Windows ネットワーク従量制 API(プラットフォーム機能)。省電力(既存 C1)とも整合。
- **実装フック**: 既存 `AutoReconnectService` / `AppUpdateService` に metered ガード。
- **適合性**: ✅ 既存サービスの素直な強化。

### G7. [P2] L4S / ECN・WMM(QoS)アウェアネス  〔品質計測〕
- **背景**: L4S(ECN ベースの低遅延)対応や WMM アクセスカテゴリは体感遅延に直結。G2 と相補。
- **提案**: 接続の WMM 有効/AC 設定、経路の ECN/L4S 反応の簡易検出を表示。
- **出典**: L4S(RFC 9330/9331/9332)、IETF responsiveness。
- **実装フック**: G2 の responsiveness 計測に ECN マーキング観測を付加。
- **適合性**: ✅ 表示・計測主体。

---

## 2bis. 追加改善点 — 第2ラウンド(IoT オンボーディング・既知脆弱性・MLO・測位)

### G8. [P1] DPP / Wi-Fi Easy Connect オンボーディング(+セキュリティ注記)  〔オンボーディング〕
- **背景**: MWC は `WIFI:` URI の QR を持つが、これは旧 WPS の流儀。**DPP(Device Provisioning
  Protocol)= Wi-Fi Easy Connect** が WPS の標準後継(QR/NFC/BLE でブートストラップ)。
  ただし 2025 年の解析で DPP 3.0 は WPS より攻撃面が広がりうる設計上の問題が報告された。
- **提案**: DPP ブートストラップ QR の生成/読み取り対応を検討しつつ、UI で
  「DPP は WPS 後継だが構成次第で攻撃面が増える」旨の注意を併記(usability/security トレードオフ)。
- **出典**: Springer IJIS 2025「Security analysis of the Wi-Fi Easy Connect」
  ([DOI 10.1007/s10207-025-00988-3](https://link.springer.com/article/10.1007/s10207-025-00988-3))、
  DEF CON 33「Breaking Wi-Fi Easy Connect: A Security Analysis of DPP」。
- **実装フック**: `WifiUri`(Profile)に DPP URI スキーム、`QrCodeDialog`、`SecurityAdvisoryService`。
- **適合性**: ✅ Wi-Fi 専用。能動攻撃なし。実装は段階的(まず注意喚起、次に生成対応)。

### G9. [P1] FragAttacks 助言(集約/フラグメンテーション脆弱性)  〔セキュリティ〕
- **背景**: `SecurityAdvisoryService` は Dragonblood / MFP-deauth / SAE-PK を扱うが、
  **FragAttacks(CVE-2020-24586/24587/24588:フラグメントキャッシュ/混在鍵/集約フラグ)**は未対応。
  ほぼ全 Wi-Fi 機器が影響を受けた設計+実装欠陥で、パッチ状況の確認が重要。
- **提案**: 既存助言体系に FragAttacks の項目を追加(ドライバー/ファーム更新の促し、
  MFP 併用の推奨)。検出は限定的でも、教育的助言として価値が高い。
- **出典**: Vanhoef「Fragment and Forge」(USENIX Security 2021,
  [papers.mathyvanhoef.com](https://papers.mathyvanhoef.com/usenix2021.pdf))、
  CVE-2020-24586/24587/24588、[fragattacks.com](https://www.fragattacks.com/)。
- **実装フック**: `SecurityAdvisoryService` に項目追加(既存パターンの踏襲)。
- **適合性**: ✅ 既存セキュリティ助言の自然な拡張。

### G10. [P1] MLO アノマリー助言(リンク飢餓・失敗時の堅牢性)  〔Wi-Fi 7〕
- **背景**: 既存 `MloAnalyzerService` はリンク集約/レイテンシ削減推定を持つが、
  **MLO 特有のアノマリー**(条件次第で単一リンクより遅延が悪化・リンク間飢餓)や
  MLO 接続失敗時のスタック堅牢性(例: mac80211 の MLO use-after-free, CVE-2026-46125)は未考慮。
- **提案**: `MloAnalysis` に「MLO がかえって不利になりうる条件」の助言を追加し、
  リンク非対称が大きい場合は単一リンク運用を提案。失敗時挙動の注意も。
- **出典**: arXiv [2210.07695](https://arxiv.org/abs/2210.07695)
  (Understanding MLO in Wi-Fi 7: Performance, Anomalies, Solutions)、CVE-2026-46125。
- **実装フック**: `MloAnalyzerService.Analyze` の判定にアノマリー条件を追加。
- **適合性**: ✅ 既存 MLO 分析の深化。

### G11. [P2] 802.11az/bk セキュア FTM 測位能力の表示(表示専用)  〔次世代対応〕
- **背景**: 旧 802.11mc の FTM(Fine Timing Measurement)測距は非セキュアで、
  **802.11az/bk(2023 確定)が測距にセキュリティ強化**を導入。ただし commodity 機器の
  セキュア測距対応はまだ限定的。MWC は FT(802.11r)時間は扱うが FTM 測位能力は未表示。
- **提案**: AP/アダプターが FTM(11mc)/セキュア測距(11az/bk)能力を広告しているかを
  **表示するのみ**。非セキュア FTM は位置詐称リスクがある旨の注記。
- **出典**: arXiv [2603.18687](https://arxiv.org/abs/2603.18687)(Secure Wi-Fi Ranging 11az/bk)、
  [2509.03901](https://arxiv.org/abs/2509.03901)(FTM サーベイ 180 本)、
  [2511.17935](https://arxiv.org/html/2511.17935v1)(11mc vs 11az 性能)。
- **実装フック**: `WifiNetwork` に FTM/secure-ranging フラグ、バッジ表示のみ。
- **適合性**: ✅ 表示専用に限定すれば CLAUDE.md 方針に合致。

### 範囲外メモ(調査したが CLAUDE.md 方針により非採用)
- **連合学習 / オンデバイス LLM ローミング**(arXiv [2405.11504](https://arxiv.org/html/2405.11504v1)
  「AI/ML-native 802.11」、[2505.04174](https://arxiv.org/html/2505.04174)「On-Device LLM for
  Wi-Fi Roaming」)は有望だが、CLAUDE.md「❌ 量子・AI 等の派手な機能」に抵触。
  既存の軽量統計予測(`HandoverPredictor` / `SignalQualityPredictor`)の範囲に留めるのが妥当。

---

## 3. 今サイクル推奨(P0)

1. **G1 MAC ランダム化 & プローブ追跡プライバシー助言** — 競合に無く、研究的裏付けも強い差別化。
2. **G2 bufferbloat / responsiveness グレード** — 既存品質計測の自然な深化、体感品質を可視化。

いずれも Core 中心・≤200 行で自前実装可能。次点で G3(WPS 警告)/ G6(metered)。

**低コストの即効改善**: G9(FragAttacks 助言)は `SecurityAdvisoryService` に項目を
1 つ足すだけで、既存パターンを踏襲して数十行で実装可能。教育的価値が高くおすすめ。

---

## 4. 出典(Sources)

- Wi-Fi 8 / 802.11bn: [arXiv 2303.10442](https://arxiv.org/abs/2303.10442), [arXiv 2501.03680](https://arxiv.org/abs/2501.03680)
- 802.11bf センシング: [arXiv 2207.04859](https://ar5iv.labs.arxiv.org/html/2207.04859), [arXiv 2403.19825](https://arxiv.org/pdf/2403.19825), [arXiv 2503.04637](https://arxiv.org/html/2503.04637v1)
- MAC/プローブ プライバシー: [arXiv 2412.10548](https://arxiv.org/abs/2412.10548), [arXiv 2408.01578](https://arxiv.org/html/2408.01578v1), [arXiv 2206.10927](https://arxiv.org/abs/2206.10927), [arXiv 1703.02874](https://arxiv.org/abs/1703.02874)
- 負荷時遅延 / L4S: [IETF responsiveness draft](https://datatracker.ietf.org/doc/html/draft-ietf-ippm-responsiveness-03), [arXiv 2511.19213](https://arxiv.org/html/2511.19213), [bufferbloat.net](https://www.bufferbloat.net/projects/bloat/wiki/Tests_for_Bufferbloat/)
- 競合比較: [Comparitech WiFi analyzers 2025](https://www.comparitech.com/net-admin/wifi-analyzers/), [NetSpot WiFi testing tools](https://www.netspotapp.com/wifi-analyzer/wifi-testing-tools.html), [itPRC best WiFi analyzers](https://www.itprc.com/best-wifi-analyzers-for-windows-networks/)
