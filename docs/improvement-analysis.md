# MWC 改善点分析 — 10カテゴリー × 10項目

arXiv / GitHub / IETF RFC の調査に基づく改善点の洗い出し。各項目に優先度 (P0=即実装 / P1=次期 / P2=将来) を付す。

---

## カテゴリー1: Wi-Fi 接続コア

1. **[P0] Captive Portal API (RFC 8908/8910)** — DHCP Option 114 / RA で portal を検出。レガシー HTTP リダイレクトより堅牢
2. [P1] Passpoint R3 Venue Portal URL 対応
3. [P1] 隠し SSID (non-broadcast) の手動接続フロー
4. [P2] WPS (Wi-Fi Protected Setup) PIN/PBC 対応 (セキュリティ注意付き)
5. [P1] 接続前の到達性事前チェック (ARP/ping ゲートウェイ)
6. [P0] 接続失敗時の指数バックオフ + ジッター (現状は固定バックオフ)
7. [P2] Wi-Fi Aware (NAN) によるピア発見
8. [P1] DNS over HTTPS / DNS フォールバック検出
9. [P2] IPv6-only ネットワーク検出 (NAT64/DNS64)
10. [P1] MAC ランダム化状態の表示 (プライバシー)

## カテゴリー2: セキュリティ

1. **[P0] Captive Portal の TLS 証明書検証** — MITM 検出 (DNS hijacking 対策)
2. [済] Dragonblood (WPA3 transition) 検出 — v3.2.0
3. [済] MFP/deauth 診断 — v3.2.0
4. [P1] Evil Twin (同一 SSID 異 BSSID) 検出 — BSSID 履歴で警告
5. [P1] KARMA/MANA 攻撃検出 (既知 SSID への自動応答 AP)
6. [P0] PMF (802.11w) を接続プロファイルで Required 強制オプション
7. [P2] OWE Transition の正当性検証 (偽 OWE AP 対策)
8. [P1] 証明書ピンニング (EAP-TLS サーバー証明書)
9. [P1] 接続履歴の暗号化保存 (現状 DPAPI、Linux/macOS も同等化)
10. [P2] SAE-PK (Public Key) 対応 (Dragonblood 緩和)

## カテゴリー3: クロスプラットフォーム

1. [P1] Linux iwd (iNet wireless daemon) バックエンド対応 (NetworkManager 以外)
2. [P0] macOS CoreWLAN の権限プロンプト処理 (位置情報)
3. [P1] Android 13+ NEARBY_WIFI_DEVICES 権限対応
4. [P1] iOS NEHotspotConfiguration の有効期限管理
5. [P2] FreeBSD / OpenBSD 対応
6. [P1] Windows ARM64 ネイティブビルド検証
7. [P2] Linux wpa_supplicant 直接制御 (D-Bus)
8. [P1] プラットフォーム差異の機能マトリクス文書化
9. [P0] netstandard2.0 ターゲットの API 互換性 CI 検証
10. [P2] WSL2 環境での動作 (Windows ホスト連携)

## カテゴリー4: 信号・性能予測

1. [済] EMA 信号予測 — v3.2.0
2. [済] 802.11r/k/v ローミング診断 — v3.3.0
3. [済] バンド/チャネル選択助言 — v3.4.0
4. **[P0] 統合推奨エンジン** — Security + Roaming + Channel + 信号予測を単一スコアに合算
5. [P1] Kalman フィルタによる RSSI 平滑化 (EMA より高精度)
6. [P1] スループット実測 (iperf 様式の帯域測定)
7. [P2] 機械学習なしの時系列異常検知 (信号スパイク)
8. [P1] レイテンシ/ジッター測定 (VoIP 品質指標)
9. [P2] チャネル使用率の BSS Load IE パース
10. [P1] 接続安定性スコア (履歴ベースの信頼度)

## カテゴリー5: UI/UX・アクセシビリティ

1. [済] WCAG AA コントラスト / AutomationProperties — 既存
2. [P1] スクリーンリーダー実機テスト (NVDA/JAWS/ナレーター)
3. [P0] 信号強度の非色覚依存表現 (アイコン形状 + 色)
4. [P1] キーボードショートカットのカスタマイズ
5. [P2] ハイコントラストモード専用テーマ
6. [P1] アニメーション削減設定 (prefers-reduced-motion 相当)
7. [P1] フォントサイズスケーリング対応
8. [P2] 音声フィードバック (接続成功/失敗)
9. [P1] Empty State の充実 (ネットワークゼロ時の案内)
10. [P0] エラーメッセージのエラー ID 表示 (サポート連携)

## カテゴリー6: CLI / 自動化

1. [済] bash/PowerShell 補完 — v3.1.0
2. [P0] JSON 出力モード全コマンド統一 (--json)
3. [P1] 終了コードの標準化 (0=成功, 1=接続失敗, 2=引数エラー...)
4. [P1] パイプ連携 (scan | grep | connect)
5. [P2] watch モード (継続スキャン)
6. [P1] 設定ファイル (~/.config/mwc/config.toml)
7. [P0] 非対話モード (--non-interactive, CI 用)
8. [P2] man page 生成
9. [P1] プロファイルのインポート/エクスポート (CLI)
10. [P2] シェルスクリプト連携サンプル集

## カテゴリー7: 企業展開

1. [済] Group Policy / Intune — 既存
2. [P1] SCEP/NDES によるクライアント証明書自動登録
3. [P0] MSI のトランスフォーム (MST) でサイト別設定
4. [P1] イベントログ (Windows Event Log) 出力
5. [P2] SCCM 配布パッケージ
6. [P1] テレメトリのオプトアウト可能な集約 (組織管理者向け)
7. [P1] ログの SIEM 連携形式 (CEF/LEEF)
8. [P2] ゼロタッチ展開 (Autopilot)
9. [P0] 設定の集中管理 (レジストリ/plist ポリシー)
10. [P1] ライセンス管理 (Enterprise 機能の有効化)

## カテゴリー8: テスト品質

1. [済] property-based (FsCheck) / mutation (Stryker) — 既存
2. [P0] テストカバレッジ計測の CI ゲート (80% 閾値)
3. [P1] 統合テスト (実 WLAN API モック)
4. [P1] スナップショットテスト (XML プロファイル生成)
5. [P2] ファズテスト (WifiUri パーサー)
6. [P0] 並行性テストの拡充 (ConnectionExecutor)
7. [P1] ゴールデンファイルテスト (i18n リソース)
8. [P2] パフォーマンス回帰テスト (BenchmarkDotNet CI)
9. [P1] アクセシビリティ自動テスト (AccessibilityAudit)
10. [P1] クロスプラットフォーム CI マトリクス (Win/Linux/macOS)

## カテゴリー9: 観測可能性

1. **[P0] OpenTelemetry 構造化ログ** — ILogger + LoggerMessage source generation (高性能・型安全)
2. [P0] コンパイル時ログ生成 ([LoggerMessage] 属性) で文字列補間を排除
3. [P1] ActivitySource による分散トレーシング (接続フロー全体)
4. [P1] Meter によるメトリクス (接続成功率/レイテンシ p50/p95/p99)
5. [P1] ヘルスチェック (アダプター状態)
6. [P2] OTLP エクスポーター (Jaeger/Prometheus)
7. [P1] ログレベルの動的変更
8. [P2] 診断ダンプ生成 (サポート用)
9. [P1] PII を含まないことの自動検証 (ログ)
10. [P2] イベントカウンター (dotnet-counters 連携)

## カテゴリー10: 配布・OSS 運用

1. [済] LICENSE / FUNDING / ドキュメント — v3.1.0
2. [P0] Polly v8 によるリトライ/サーキットブレーカー (現状は自前実装)
3. [P1] SBOM (CycloneDX) の自動生成検証
4. [P1] リリースノートの自動生成 (Conventional Commits → CHANGELOG)
5. [P2] コンテナイメージ配布 (CLI)
6. [P1] Homebrew formula (macOS)
7. [P1] AUR パッケージ (Arch Linux)
8. [P2] Flatpak / Snap (Linux GUI)
9. [P0] 依存パッケージの脆弱性スキャン CI ゲート
10. [P1] 署名検証の自動テスト (Sigstore)

---

## 今サイクルの実装対象 (P0 優先)

横断的に影響が大きい P0 を選定:

1. **統合推奨エンジン** (C4-4) — 既存4サービスを束ねる中核機能
2. **OpenTelemetry 構造化ログ / LoggerMessage** (C9-1,2) — .NET 公式ベストプラクティス
3. **指数バックオフ + ジッター** (C1-6) — 接続信頼性
4. **Captive Portal API モデル** (C1-1, C2-1) — RFC 8908/8910

これらを v3.5.0 として実装する。
