# MWC 改善点リサーチ Part 2 — 追加10カテゴリー × 10項目(arXiv + GitHub)

> `improvement-research-100.md`(C1–C10)で扱っていない**新規10カテゴリー**を追加で洗い出す。
> 既存3ドキュメント(arxiv-improvement-analysis / improvement-analysis-2026 / 同100)と
> 重複しない領域を選定。出典は (gh: owner/repo) / (rfc: N) / (arXiv: id)。
> これで累計 **200 改善点**。優先度 P0/P1/P2。

## 進捗トラッカー
- [x] D1 エンタープライズ認証 (802.1X/EAP/RADIUS)
- [x] D2 キャプティブポータル & CAPPORT
- [x] D3 IoT/Matter/Thread 共存・オンボーディング
- [x] D4 測位・センシング (FTM/CSI/RTT/11bf)
- [x] D5 QoS/トラフィック管理 (WMM/DSCP/L4S/QUIC)
- [x] D6 メッシュ & マルチAP (EasyMesh/802.11s)
- [x] D7 規制ドメイン & 6GHz AFC/電力
- [x] D8 信頼性・フェイルオーバー (マルチアダプター)
- [x] D9 診断・サポートツール
- [x] D10 データ分析・長期トレンド

---

## D1. エンタープライズ認証 (802.1X / EAP / RADIUS)

1. [P1] **EAP メソッド網羅表示**(PEAP/TTLS/TLS/FAST/TEAP)と推奨度。(gh: FreeRADIUS/freeradius-server)
2. [P1] **接続前 EAP 疎通自己診断**(eapol_test 相当)。(gh: 上流 wpa_supplicant `eapol_test`)
3. [P1] **サーバ証明書検証の厳格化**(CN/SAN/CA 固定)。(gh: tpm2-software/tpm2-pkcs11 EAP-TLS)
4. [P1] **EAP-TLS 1.3(RFC 9190)対応状況の表示**。
5. [P2] **TEAP(RFC 7170)+ EAP chaining 認識**。
6. [P1] **OCSP/CRL による証明書失効確認**。
7. [P1] **WPA3-Enterprise 192-bit(CNSA)判定**(既存強化)。
8. [P2] **PEAP-MSCHAPv2 の弱点注意喚起**(資格情報リレー)。
9. [P1] **RADIUS サーバ証明書の TOFU/ピン留め UX**。
10. [P2] **eduroam/802.1X プロファイルの一貫性検証**。

## D2. キャプティブポータル & CAPPORT

1. [P1] **RFC 8908 Captive Portal API 対応**(状態取得)。(rfc: 8908)
2. [P1] **RFC 8910 DHCP/RA からの Portal URI 検出**。(rfc: 8910)
3. [P1] **iOS14/Android11 互換の検出フローに整合**。
4. [P1] **既存 msftconnecttest フォールバックと API 併用**(既存 `CaptivePortalService`)。
5. [P2] **実装観点の参照**。(gh: inverse-inc/packetfence #7040)
6. [P1] **ポータルセッションの残時間/残量表示**(RFC 8908 venue-info)。
7. [P1] **in-app WebView 認証の TLS 検証強化**。
8. [P2] **セッション失効時の自動再認証**。
9. [P1] **偽ポータル(フィッシング)警告**。
10. [P2] **ポータル種別の分類・履歴**。

## D3. IoT / Matter / Thread 共存・オンボーディング

1. [P2] **Matter over Wi-Fi の QR オンボーディング認識**。(gh: project-chip/connectedhomeip)
2. [P2] **Thread Border Router の Wi-Fi 共存表示**。(gh: espressif/esp-thread-br)
3. [P1] **2.4GHz の Wi-Fi/Thread/Zigbee/BLE 共存スコア**(cross-tech と統合)。
4. [P2] **IoT 向けチャネル助言**(混雑回避)。
5. [P2] **WPA3-Personal の IoT 互換性注意**(移行モード)。
6. [P1] **TWT による IoT 省電力フレンドリ判定**(既存 `PowerSaveAdvisorService`)。
7. [P2] **Matter コミッショニング用一時 AP の検出**。
8. [P2] **ヘッドレス機器の P2P オンボーディング参照**。(gh: Ircama/hostp2pd)
9. [P1] **IoT 向け OWE(拡張開放)推奨**(既存 `OweSelectionService`)。
10. [P2] **2.4GHz 専用機器のための帯域共存助言**。

## D4. 測位・センシング (FTM/CSI/RTT/11bf)

1. [P2] **802.11mc/az FTM 測距能力の表示**。(arXiv: 2509.03901)
2. [P2] **セキュア測距(11az/bk)対応バッジ**。(arXiv: 2603.18687)
3. [P2] **非セキュア FTM の位置詐称リスク注記**。
4. [P2] **CSI 取得能力の表示**(対応 NIC)。(gh: seemoo-lab/nexmon_csi)
5. [P2] **802.11bf センシング能力の表示専用**。(arXiv: 2207.04859)
6. [P2] **センシングのプライバシー注記**(presence/motion)。
7. [P2] **RTT ベース距離推定の参考表示**。
8. [P2] **mmWave(60GHz)測位の対応有無**。(arXiv: 2303.05996)
9. [P2] **屋内測位の精度限界の説明**。
10. [P2] **外部測位ツール出力の取り込み**。

## D5. QoS / トラフィック管理 (WMM/DSCP/L4S/QUIC)

1. [P1] **WMM 有効 / AC 設定の表示**。
2. [P2] **DSCP→UP マッピング(RFC 8325)認識**。
3. [P2] **L4S/ECN 反応の簡易検出**。(rfc: 9330)
4. [P1] **QoS と bufferbloat(C4)の統合スコア**。
5. [P2] **QUIC/HTTP3 経路の遅延特性表示**。
6. [P2] **MSCS/SCS(ストリーム分類)認識**(Wi-Fi 6/7)。
7. [P1] **アプリ別レイテンシ感度の助言**(ゲーム/会議)。
8. [P2] **U-APSD 省電力と遅延のトレードオフ**。
9. [P2] **エアタイム公平性の指標**。
10. [P2] **QoS 重視の優先 SSID/帯域の自動選択**。

## D6. メッシュ & マルチAP (EasyMesh / 802.11s)

1. [P1] **EasyMesh(Multi-AP)ネットワークの認識・表示**。(gh: prplfoundation/prplMesh)
2. [P1] **クライアントステアリング(11k/v)イベントの可視化**。
3. [P2] **Multi-AP 仕様の挙動参照**。(gh: WHJWNAVY/WIFI-DOCS Multi-AP_Spec)
4. [P1] **バックホール(有線/無線)種別の表示**。
5. [P2] **802.11s メッシュの検出**(対応環境)。
6. [P1] **メッシュ内ローミングの体感**(C5 と統合)。
7. [P2] **ノード間チャネル選択の可視化**。
8. [P2] **メッシュ AP の簡易信号マップ**。
9. [P1] **同一 SSID の複数 BSSID(メッシュ)の整理表示**。
10. [P2] **EasyMesh R4/R5 機能の認識**。

## D7. 規制ドメイン & 6GHz AFC / 電力

1. [P1] **規制ドメイン別チャネル/電力の表示**(既存 `RegulatoryDomainService` 強化)。(gh: wireless-regdb)
2. [P1] **6GHz 電力モード(LPI/SP/VLP)の表示**。
3. [P2] **AFC(Standard Power)対応有無の表示**。(gh: Wireless-Innovation-Forum/6-GHz-AFC)
4. [P1] **DFS チャネル(レーダー回避)の表示**。
5. [P2] **規制 DB の更新フロー**(wireless-regdb 参照)。
6. [P1] **6GHz PSC との整合**(C3 と統合)。
7. [P2] **国移動時の規制再評価 UX**。
8. [P2] **技適/FCC/CE 等の情報表示**。
9. [P1] **規制起因の接続不可の説明**(電力/チャネル)。
10. [P2] **AFC 位置情報プライバシーの注記**。

## D8. 信頼性・フェイルオーバー (マルチアダプター冗長化)

1. [P1] **複数アダプター間の自動フェイルオーバー**。
2. [P1] **接続リトライ戦略の可視化**(既存 `RetryPolicy`)。
3. [P1] **リンク品質に基づくアダプター自動選択**。
4. [P2] **MPTCP/複数経路の認識**(参考)。(arXiv: 1907.10493)
5. [P1] **フラッピング時のバックオフ**。
6. [P2] **アダプター health 監視**(既存 `HealthCheckService`)。
7. [P1] **主/副アダプターのユーザー優先設定**(既存 `AdapterPreferencesService`)。
8. [P2] **VPN 連動フェイルオーバー**(ROADMAP 検討中項目)。
9. [P1] **切断検知の高速化**(`WlanNotification`)。
10. [P2] **障害注入による冗長化テスト**。

## D9. 診断・サポートツール (pcap / 診断バンドル)

1. [P1] **診断バンドル生成**(ログ+状態, PII マスク)(既存 HealthCheck 連携)。
2. [P2] **pcap キャプチャ連携**(Npcap/Wireshark)。(gh: wireshark/wireshark)
3. [P1] **接続失敗の根本原因ヒント**(既存 `TroubleshootingHelper` 強化)。
4. [P1] **エラー ID と既知解決策のマッピング**。
5. [P2] **RF 環境スナップショット**(スキャン+干渉)。
6. [P1] **サポート向け匿名化エクスポート**。
7. [P2] **イベントタイムライン**(接続/ローミング/切断)。
8. [P1] **再現手順の自動記録**。
9. [P2] **ドライバー/ファーム版数の収集**。
10. [P2] **既知不具合 DB との突合**。

## D10. データ分析・長期トレンド (時系列 / 可視化)

1. [P1] **接続品質の長期トレンド**(既存 90 日履歴の活用)。
2. [P2] **異常検知**(移動 Z-score / 季節性)。
3. [P1] **ネットワーク別の成功率・滞在統計**(既存 `NetworkHistoryService` 拡張)。
4. [P2] **時系列の圧縮保存**(ダウンサンプル)。
5. [P1] **週次/月次レポート生成**(CSV/JSON)。
6. [P2] **チャネル混雑のトレンド可視化**。
7. [P1] **ローミング頻度のトレンド**。
8. [P2] **バッテリー影響の推定トレンド**。
9. [P2] **プライバシー保護集約**(ローカルのみ)。
10. [P2] **主要 KPI のダッシュボード化**。

---

## まとめ(Part 1 + Part 2)
追加 10 カテゴリー × 10 = **100 項目**を arXiv + GitHub 出典付きで列挙。
`improvement-research-100.md` と合わせ **計 200 改善点**。
これ以上の網羅的列挙より、次は **(a) 重複の統合・優先度の確定**、
**(b) 上位 P0(C4 RPM / C6 MAC / C2 FragAttacks / D2 CAPPORT)の ADR 化・実装**に移るのが妥当。
