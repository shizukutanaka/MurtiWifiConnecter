# MWC 機能仕様書 (Specification)

> MWC(Multi WiFi Connector)の機能仕様。README / ROADMAP / CLAUDE.md と実装から導出。
> 各要件に ID(FR-xx)を付与し、実装状況とテストの所在を明記する。
> 「不足」発見のための基準書。最終更新時の未充足項目は §12 にまとめる。

## 1. 目的とスコープ
- 複数の無線アダプターを 1 画面で独立管理する Windows 用 Wi-Fi ツール。
- Open / OWE / WEP / WPA / WPA2 / WPA3(SAE/Enterprise/192-bit)接続。
- スキャン分析・品質計測・QR・CLI・多言語・アクセシビリティ。
- **非スコープ**: 量子暗号、ブロックチェーン、派手な AI 機能(CLAUDE.md)。

## 2. 接続コア (IWifiService)
| ID | 要件 | 実装 | テスト |
|---|---|---|---|
| FR-01 | 無線アダプター列挙 | `IWifiService.GetAdaptersAsync` / WindowsWifiService | FakeWifiService 統合 |
| FR-02 | 指定アダプターでスキャン | `ScanAsync` | 同上 |
| FR-03 | プロファイル登録(冪等) | `RegisterProfileAsync` | — |
| FR-04 | 接続=通知完了+疎通確認の2段 | `ConnectAsync` + ConnectionWaiter + HttpConnectivityChecker | — |
| FR-05 | 切断 / プロファイル削除 / 一覧 | `DisconnectAsync` / `DeleteProfileAsync` / `ListProfilesAsync` | — |
| FR-06 | 状態変化通知の購読 | `SubscribeEventsAsync` (IAsyncEnumerable) | — |
| FR-07 | `netsh`/WMI を使わず WlanAPI 直叩き | ManagedNativeWifi 経由 | ADR-0001 |

## 3. プロファイル生成 (ProfileXmlBuilder ★心臓部)
| ID | 要件 | 実装 | テスト |
|---|---|---|---|
| FR-10 | 文字列連結禁止・`XElement` で組立 | ProfileXmlBuilder | ProfileXmlBuilderTests |
| FR-11 | SSID/パスフレーズの検証(IEEE 802.11) | WifiProfileValidator + spec.Validate | ValidationAndSecurityTests |
| FR-12 | インジェクション自動エスケープ | XElement | `Injection_AttemptIsEscaped` |
| FR-13 | **全認証方式をゴールデンテストで検証**(CLAUDE.md 必須) | §3.1 マトリクス | ProfileXmlBuilderTests |

### 3.1 認証/EAP サポートマトリクス
| 方式 | 実装 | ゴールデンテスト |
|---|:---:|:---:|
| Open / OWE / WEP | ✅ | ✅ |
| WPA-PSK / WPA2-PSK / WPA3-SAE / WPA3-Transition | ✅ | ✅ |
| WPA2/WPA3 Enterprise / WPA3-Enterprise-192(GCMP256) | ✅ | ✅ |
| EAP: PEAP-MSCHAPv2 (25) | ✅ | ✅ |
| EAP: EAP-TLS (13) | ✅ | ✅ |
| EAP: **EAP-TTLS (21)** | ✅(本改修で実装) | ✅ `Enterprise_TTLS_BuildsEapTtlsConfig` |
| EAP: EAP-AKA (23, SIM) | ⛔ 非サポート(§12) | ✅ 拒否を検証 |

## 4. スキャン分析
| ID | 要件 | 実装 |
|---|---|---|
| FR-20 | マルチアダプター並列スキャン + タブ UI | MainViewModel / AllAdaptersOverview |
| FR-21 | 信号履歴グラフ(60分 RSSI) | SignalHistoryService / SignalHistoryCanvas |
| FR-22 | チャンネル帯域グラフ(2.4/5/6GHz) | ChannelBandCanvas / ChannelAdvisorService |
| FR-23 | MAC ベンダー解決(IEEE OUI) | OuiLookupService |
| FR-24 | 干渉/ローミング/MLO/省電力の各助言 | Interference/Roaming/Mlo/PowerSave サービス群 |

## 5. ネットワーク品質
| ID | 要件 | 実装 | テスト |
|---|---|---|---|
| FR-30 | RTT/パケットロス/グレード計測 | NetworkQualityService.MeasureAsync | ServicesTests |
| FR-31 | **負荷時遅延(bufferbloat/RPM)** | `MeasureResponsivenessAsync` / `ComputeRpm` / `GradeBufferbloat`、CLI `mwc quality --bufferbloat` | ResponsivenessTests |

## 6. セキュリティ助言 (SecurityAdvisoryService)
| ID | 要件 | コード |
|---|---|---|
| FR-39 | 助言を CLI で表示 | `mwc scan --advise`(Warning/Critical) |
| FR-40 | WPA3 移行モード(Dragonblood)警告 | MWC-SEC-001 |
| FR-41 | MFP 無効(deauth)警告 | MWC-SEC-002 |
| FR-42 | WEP/WPA-TKIP/Open 警告 | MWC-SEC-003/004/005 |
| FR-43 | **FragAttacks 助言** | MWC-SEC-006 |
| FR-44 | **WPS 有効 AP 警告** | MWC-SEC-007 |
| FR-45 | 堅牢ネットワークの肯定的フィードバック | MWC-SEC-100 |
| FR-46 | 総合スコアに WPS ペナルティを反映 | `ComputeScore`(-10) |

## 7. プライバシー (PrivacyAdvisoryService)
| ID | 要件 | コード |
|---|---|---|
| FR-50 | 固定 MAC × 公共ネットワークの追跡警告 | MWC-PRIV-001 |
| FR-51 | ランダム化の推奨/日次ローテーション提案/良好評価 | MWC-PRIV-002/003/100 |
| FR-52 | IE 指紋による再識別の限界の周知 | MWC-PRIV-004 |

## 8. 出力 / CLI
| ID | 要件 | 実装 |
|---|---|---|
| FR-60 | QR(`WIFI:` URI)生成・パース | WifiUri / QrCodeDialog |
| FR-61 | CSV/JSON/TXT エクスポート | ExportService |
| FR-62 | CLI: list/scan/connect/disconnect/qr/export/quality/history/profile/adapter/multi | MWC.Cli/* |
| FR-63 | システムトレイ常駐 / JumpList | SystemTrayService / JumpListService |

## 9. UX / アクセシビリティ / i18n
| ID | 要件 | 実装 |
|---|---|---|
| FR-70 | Light/Dark/System テーマ | ThemeService |
| FR-71 | UI 文字列は必ず Strings.resx 経由 | Resources/L.cs |
| FR-72 | 14 言語(516 キー一致) | Strings.*.resx |
| FR-73 | WCAG / AutomationProperties / Live Region | AccessibilityService |

## 10. クロスプラットフォーム / 配布
| ID | 要件 | 実装 |
|---|---|---|
| FR-80 | Windows 実装(製品本体)。Linux/macOS は部分実装 | MWC.Platform.{Windows,Linux,MacOS} |
| FR-81 | MWC.Core / MWC.SDK ライブラリ(net9.0) | §11 |
| FR-82 | winget/scoop/choco/msix/MSI 配布 | installer/* |
| FR-83 | Sigstore 署名 + SLSA + SBOM | Directory.Build.props / ci |

## 11. ビルド / 品質ゲート
- net9.0 単一ターゲット(ns2.0 は net6+ API 多用のため撤廃。`docs/build-blockers-2026.md`)。
- `TreatWarningsAsErrors=true` / アナライザ全有効 / カバレッジ 80%。
- CI/CodeQL は `ci/github-workflows/`(要 `.github/workflows/` 設置)。

## 12. 既知の未充足 / 将来 (Gap)
| 項目 | 状態 | 方針 |
|---|---|---|
| **EAP-AKA (SIM 認証)** | 宣言のみ・非サポート | SIM ハードウェア前提・Windows 実機での XML 検証が必要。`EapType` に残すが Build/Validate で明示的に拒否。需要があれば実装。 |
| CI 実走検証 | 未 | 取込みソースに複数のビルド阻害があったため(build-blockers-2026)、`.github/workflows/` 設置で net9.0 ビルド+テストの緑化が最優先。 |
| **生 IE スキャン (Country/TPC/BSS Load/RNR/MDID/WMM)** | Core 完備・Windows 実機検証待ち | ManagedNativeWifi 3.0.2 の `BssNetworkInfo` は生 IE バイトを公開しない(Ssid/Bssid/Phy/Rssi/Freq/Band/Channel のみ)。`IBeaconIeProvider` で取得経路を分離し、Core 側は `BeaconIeParser`/`BeaconEnrichmentService` で完備・テスト済み。Windows 実装 `WlanBssIeProvider` は `WlanGetNetworkBssList` P/Invoke だが**実機検証が必須**のため既定 DI 未登録(`WindowsWifiService` は `NullBeaconIeProvider` にフォールバックし基本スキャンは劣化なし)。 |
| docs/improvement-* の P1/P2 | 計画 | DPP/CAPPORT/MLO アノマリー/FTM 等(improvement-analysis-2026, improvement-research-100/part2)。 |
