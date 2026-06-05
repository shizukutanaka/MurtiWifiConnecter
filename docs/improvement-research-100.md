# MWC 改善点リサーチ — 10カテゴリー × 10項目(arXiv + GitHub 出典)

> 目的: プロダクトを 10 カテゴリーに分け、各 10 項目を **arXiv 論文**と **GitHub OSS** から
> 出典付きで洗い出す。既存 `arxiv-improvement-analysis.md` / `improvement-analysis-2026.md`
> と重複する基礎項目は出典強化に留め、新規性・実装フックを重視する。
> 本書は `/loop` で**反復的に拡充**する(1イテレーションで数カテゴリーずつ)。

凡例: 優先度 P0/P1/P2。出典は (gh: owner/repo) または (arXiv: id)。

## 進捗トラッカー
- [x] C1 接続コア & WLAN API 抽象化
- [x] C2 セキュリティ & 脆弱性検出
- [x] C3 スキャン & スペクトラム分析
- [x] C4 品質計測 & bufferbloat/responsiveness
- [x] C5 ローミング & モビリティ (11r/k/v, MLO)
- [x] C6 プライバシー (MAC ランダム化, probe)
- [x] C7 プロビジョニング & プロファイル (DPP/eduroam/QR/Passpoint)
- [x] C8 クロスプラットフォーム実装 (Linux/macOS/Android/iOS)
- [x] C9 UX・可視化・アクセシビリティ
- [x] C10 配布・サプライチェーン・CI/CD

---

## C1. 接続コア & WLAN API 抽象化

1. [P1] **能力照会(capabilities)API を `IWifiService` に追加** — 各 OS の対応バンド/PHY/MLO 差異を吸収。Linux は `iw`(nl80211)で詳細取得可。(gh: NetworkManager/NetworkManager)
2. [P2] **Intel iwd を Linux バックエンド候補化** — wpa_supplicant より軽量・D-Bus ネイティブ。`NmcliWifiService` の代替/補完。(gh: 上流 iwd)
3. [P1] **wpa_supplicant control interface のイベント駆動パターン参照** — 接続フローの状態通知を堅牢化。(gh: 上流 w1.fi/wpa_supplicant)
4. [P1] **ManagedNativeWifi の最新 API 追従** — 6GHz / Wi-Fi 7 列挙・MLO プロパティの網羅確認。(gh: emoacht/ManagedNativeWifi ← MWC が依存)
5. [P2] **受信専用フレーム/IE パーサ** — `vanhoefm/libwifi` を教育的参考に IE 解析(WPS/BSS Load/RNR 等)。(gh: vanhoefm/libwifi)
6. [P1] **Wi-Fi Direct/P2P の WPS enrolment 強化** — `WifiDirectService` を hostp2pd 設計参考に。(gh: Ircama/hostp2pd)
7. [P1] **DPP(Easy Connect)制御の足場** — README-DPP を参考に段階導入。(gh: 上流 hostapd README-DPP)
8. [P2] **nl80211 vendor command 抽象化** — ベンダー固有拡張能力の取り扱い方針。
9. [P1] **接続状態機械の形式化** — connecting→assoc→4way→connected をテスト可能に(C7 形式検証と連携)。
10. [P2] **RNR(Reduced Neighbor Report)パース** — 6GHz AP を 2.4/5GHz ビーコンから発見し高速化。

## C2. セキュリティ & 脆弱性検出

1. [P1] **evil twin / karma 検出の多シグナル集約** — sentrygun のセンサ集約設計を `EvilTwinDetector` に反映。(gh: s0lst1c3/sentrygun)
2. [P1] **PMKID 攻撃・deauth 検出指標を助言化** — RogueAP-Detector の検出観点を取り込む。(gh: anotherik/RogueAP-Detector)
3. [P1] **FragAttacks 助言**(CVE-2020-24586/24587/24588)— パッチ/ファーム更新の促し。(gh: vanhoefm/fragattacks)
4. [P2] **デバイス分類ベースの異常検知枠組み** — Kismet のアラート設計を参考。(gh: kismetwireless/kismet)
5. [P1] **攻撃手口を被害者側検出シグナルへ反転** — 偽 captive portal + deauth の兆候検出。(gh: wifiphisher/wifiphisher)
6. [P1] **PMF(802.11w)未対応 AP の deauth 耐性助言**(既存強化)。(arXiv: 既存 WiSec 2022 出典)
7. [P1] **WPS 有効 AP 検出 + PIN 方式警告**(Pixie-Dust)。(2026 doc G3 と統合)
8. [P2] **KRACK(4-way 再送)注意喚起の項目化**。
9. [P1] **既存助言(Dragonblood / SAE-PK)の出典・文言最新化**。
10. [P2] **脅威カタログとの突合** — wifi-arsenal を参照し助言カバレッジの抜けを点検。(gh: 0x90/wifi-arsenal)

## C3. スキャン & スペクトラム分析

1. [P1] **2.4/5/6GHz + BLE 統合可視化** — sparrow-wifi の統合ビューを `ChannelBandCanvas` 拡張の参考に。(gh: ghostop14/sparrow-wifi)
2. [P1] **チャネル星評価 UX** — VREM WiFiAnalyzer のチャネル推奨提示を参考。(gh: VREMSoftwareDevelopment/WiFiAnalyzer)
3. [P2] **チャネル重なり度メトリクス** — WACA の channel spectrum 計算を参考に重複スコア。(gh: sergiobarra/WACA_WiFiAnalyzer)
4. [P1] **BSS Load IE パース** — チャネル使用率/局数で混雑可視化。
5. [P1] **6GHz PSC 優先スキャン** — Preferred Scanning Channels でスキャン高速化。
6. [P1] **パッシブ/アクティブ切替 + 省電力**(既存省電力 C1 と整合)。
7. [P2] **Cross-Tech 干渉スコア**(BLE/Zigbee, 2.4GHz)。(arXiv: 2503.05429)
8. [P2] **スペクトラム占有の時系列ヒストグラム** — 断続干渉検出。
9. [P1] **隠し SSID の検出表示** — プローブ応答/アソシエーションから名称復元。(gh: ghostop14/sparrow-wifi 参照)
10. [P2] **外部ツール出力の取り込み(CSV)** — SDR 連携は範囲外、結果のインポートで妥協。

## C4. 品質計測 & bufferbloat / responsiveness

1. [P0] **working-latency(RPM)計測を `NetworkQualityService` に追加** — 並列 TCP 負荷下の RTT。(gh: network-quality/goresponsiveness)
2. [P0] **IETF responsiveness 準拠の指標 + A–F グレード**。(gh: network-quality/draft-ietf-ippm-responsiveness)
3. [P1] **再現可能な計測プロファイル(CLI)** — Flent の思想。(gh: tohojo/flent)
4. [P1] **RTT/loss/marking 同時サンプリング** — crusader 参照。(gh: Zoxc/crusader)
5. [P1] **Apple `networkQuality` 互換の RPM 出力** で相互運用。
6. [P2] **L4S/ECN マーキング観測**。(RFC 9330 系)
7. [P2] **AQM が計測へ与える影響の注記**。(arXiv: 2511.19213)
8. [P1] **アップ/ダウン別 working latency 分離表示**。
9. [P1] **計測の自己テスト(ループバック)** で回帰検出。
10. [P2] **計測サーバの地理近接選択 + フォールバック**。

## C5. ローミング & モビリティ (11r/k/v, MLO)

1. [P1] **FT(802.11r)over-the-air / over-the-DS の判別表示**。(gh: milangroshev/hostpad-802.11r)
2. [P1] **Mobility Domain(MD-ID)表示**。(gh: walidmadkour/OpenWRT-UCI-helper-802.11r)
3. [P1] **既知の落とし穴を助言**(reassociation deadline 既定値問題など)。(gh: openwrt/openwrt issues)
4. [P1] **802.11k Neighbor Report のパース・可視化**。
5. [P1] **802.11v BSS Transition Management 受信時の挙動表示**。
6. [P1] **スティッキークライアント検出**(遠方 AP 固執)。
7. [P1] **ローミングフラッピング検出**(連続再接続)。
8. [P1] **MLO アノマリー助言**(単一リンクより悪化する条件)。(arXiv: 2210.07695)
9. [P2] **PMK キャッシング状態の表示**。
10. [P2] **ローミング閾値のユーザー設定**。

## C6. プライバシー (MAC ランダム化・probe)

1. [P0] **per-network ランダム MAC 設定状態の表示**。(arXiv: 2206.10927)
2. [P0] **固定 MAC で公共 SSID 接続時の追跡リスク警告**。(arXiv: 2412.10548)
3. [P1] **プローブ要求指紋追跡の解説 UI**。(arXiv: 2408.01578)
4. [P1] **ランダム化の限界(IE 指紋)の周知**。(arXiv: 1703.02874)
5. [P1] **metered 接続時のバックグラウンド抑制**(プライバシー+省電力)。
6. [P2] **SSID 履歴のローカル暗号化保存**(既存 DPAPI と整合)。
7. [P2] **接続履歴の最小化・自動失効**(既存 90 日)。
8. [P1] **PII 非含有の自動検証**(既存 HealthCheck と連携)。
9. [P2] **BSSID→位置の収集を行わない設計の明示**。
10. [P2] **エクスポート時の PII マスキング**。

## C7. プロビジョニング & プロファイル (DPP/eduroam/QR/Passpoint)

1. [P1] **DPP/Easy Connect 段階対応**(QR ブートストラップ)。(gh: 上流 hostapd README-DPP)
2. [P1] **DPP のセキュリティ注記**(2025 解析)。(improvement-analysis-2026 G8)
3. [済/強化] **eduroam CAT XML インポートの検証強化**(既存 `CatImportService`)。
4. [済/強化] **Passpoint / Hotspot 2.0 プロファイルの可視化**(既存 `Hotspot20Service`)。
5. [P1] **プロファイル XML のスキーマ検証・スナップショットテスト**。
6. [P1] **`WIFI:` URI ⇔ DPP URI 相互変換 UX**。
7. [P2] **EAP-TLS 証明書チェーン検証の UI**(既存 `CertificateStoreService`)。
8. [P2] **他ツールからのプロファイル移行インポート**。
9. [P1] **プロファイル削除・棚卸し UX**(既存 `ProfileManager`)。
10. [P2] **グループポリシー配布の検証**(既存 `GroupPolicyProvider`)。

## C8. クロスプラットフォーム実装 (Linux/macOS/Android/iOS)

1. [P1] **macOS CoreWLAN の能力照会拡充**。(gh: chbrown/macos-wifi)
2. [P1] **CoreWLAN Wireless Manager の機能網羅を参照**。(gh: andyvand/CoreWLANWirelessManager)
3. [P1] **Linux nmcli/iw 併用での詳細取得**。(gh: keithrbennett/wifiwand)
4. [P2] **クロスプラットフォーム scan の正規化**。(gh: BaseMax/wifi-scanner)
5. [P2] **CoreLocation/netsh/nmcli の差異吸収**。(gh: scivision/scan-wifi-python)
6. [P1] **nmcli GUI の UX を参考にした Linux 版**。(gh: sweelinq/WifiManager)
7. [P1] **Android WifiManager Suggestion API 対応確認**(既存 `AndroidWifiService`)。
8. [P1] **iOS NEHotspotConfiguration の制約の明文化**(既存 `IosWifiService`)。
9. [P2] **共通 Core(netstandard2.0)の契約テスト**。
10. [P2] **プラットフォーム能力マトリクスの文書化**。

## C9. UX・可視化・アクセシビリティ

1. [P1] **信号/チャネルグラフ描画の再利用を LiveCharts2 で評価**。(gh: Live-Charts/LiveCharts2)
2. [P2] **OxyPlot を代替候補に評価**。(gh: oxyplot/oxyplot)
3. [P1] **グラフへ AutomationPeer 付与**でスクリーンリーダー対応。
4. [P0] **非色覚依存の信号表現**(形状で冗長符号化)。(既存 a11y 方針)
5. [P1] **reduced-motion 設定対応**。
6. [P1] **フォントスケーリング / ハイコントラスト**。
7. [P1] **Empty State の充実**。(gh: Carlos487/awesome-wpf 参照)
8. [P1] **エラー ID 表示**(サポート性向上)。
9. [P2] **キーボードのみ操作の網羅テスト**。
10. [P2] **音声・テキスト併記の推奨グレード**。

## C10. 配布・サプライチェーン・CI/CD

1. [済/強化] **Sigstore keyless 署名(cosign)**。(gh: sigstore/cosign)
2. [P1] **SBOM(CycloneDX/SPDX)生成と添付**。(gh: anchore/syft)
3. [P1] **SLSA provenance(L3)生成**。(gh: slsa-framework/slsa-github-generator)
4. [P0] **CI/CodeQL を `.github/workflows` へ設置**(本ブランチで `ci/github-workflows/` に用意済み)。
5. [P1] **再現可能ビルド(Deterministic + SourceLink, 既存)の検証**。
6. [P1] **`packages.lock.json` コミットで locked restore 復活**。
7. [P1] **winget/scoop/choco/msix マニフェストの自動更新**(既存 tools/)。
8. [P2] **GitHub Artifact Attestations の検証手順を文書化**。
9. [P1] **依存脆弱性スキャン(dependabot / `dotnet list package --vulnerable`)**。
10. [P2] **配布バイナリの SmartScreen 評価準備**。

---

## まとめ
全 10 カテゴリー × 10 = **100 改善点**を arXiv + GitHub 出典付きで列挙した。
即効性の高い P0/P1 横断テーマ: **品質の体感化**(C4 RPM)、**プライバシー**(C6 MAC)、
**セキュリティ助言の拡充**(C2 FragAttacks/WPS)、**CI 設置**(C10-4)。
詳細な実装計画は `improvement-analysis-2026.md`(G1–G11)と本書を統合して起こす。
