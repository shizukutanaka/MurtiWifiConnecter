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
- [ ] C4 品質計測 & bufferbloat/responsiveness  ← 次イテレーション(出典収集済み)
- [ ] C5 ローミング & モビリティ (11r/k/v, MLO)
- [ ] C6 プライバシー (MAC ランダム化, probe)
- [ ] C7 プロビジョニング & プロファイル (DPP/eduroam/QR/Passpoint)
- [ ] C8 クロスプラットフォーム実装 (Linux/macOS/Android/iOS)
- [ ] C9 UX・可視化・アクセシビリティ
- [ ] C10 配布・サプライチェーン・CI/CD

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

---

## 次イテレーション用メモ(出典収集済み)
- **C4 品質/bufferbloat**: (gh: network-quality/goresponsiveness) IETF responsiveness(RPM)クライアント、
  (gh: network-quality/draft-ietf-ippm-responsiveness) 仕様、Flent、crusader、Apple `networkQuality`、
  (arXiv: 2511.19213) AQM の速度計測影響、L4S(RFC 9330系)。`NetworkQualityService` 拡張に直結。
- 以降 C5–C10 を順次、arXiv + GitHub から出典収集して埋める。
