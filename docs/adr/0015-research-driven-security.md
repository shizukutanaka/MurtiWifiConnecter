# ADR-0015: 学術研究に基づくセキュリティ強化と信号予測

**Date**: 2026-05-13
**Status**: Accepted

## Context

MWC は Wi-Fi 管理ツールとして、接続先のセキュリティリスクをユーザーに正しく伝える責任がある。arXiv / IEEE の学術研究から、現代の Wi-Fi に実在する以下の脅威と技術が明らかになった。

### セキュリティ脅威

1. **Dragonblood** (Vanhoef & Ronen, IEEE S&P 2020)
   - WPA3 transition mode (WPA2/WPA3 混在) は WPA2 へのダウングレード攻撃に脆弱
   - 攻撃者は同一 SSID の WPA2 専用ローグ AP を立て、辞書攻撃に必要な情報を取得できる

2. **Wi-Fi Deauthentication** (Schepers et al., WiSec 2022)
   - MFP (802.11w / Protected Management Frames) 無効の AP は偽装 deauth/disassoc フレームで強制切断される
   - WPA3 と Wi-Fi 6 では MFP 利用が前提

### 信号品質予測

3. **EMA 線形結合** (Formis, Scanzio, Cena et al., IEEE INDIN 2023 / arXiv 2509.18933)
   - 複数時定数の指数移動平均を線形結合することで、重い DL モデルに匹敵する精度の RSSI 予測が可能
   - チャネル非依存モデルでも競合性能 → メーカー横断で汎用利用できる
   - 計算コストはほぼゼロ (stdlib のみで実装可能)

## Decision

### セキュリティ診断 (`SecurityAdvisoryService`)

- `WifiNetwork` に `PmfStatus` (Unknown/Disabled/Capable/Required) と `IsWpa3TransitionMode` を追加
- `SecurityHardening` プロパティで堅牢性を4段階分類 (Hardened/Standard/TransitionModeRisk/NoMfpRisk)
- `SecurityAdvisoryService.Analyze()` が脅威コード付きの勧告を生成 (MWC-SEC-001〜100)
- `ComputeScore()` で 0-100 のセキュリティスコアを算出 (transition mode は -15 ペナルティ)
- **重要: 攻撃は一切実装しない。防御側の情報提供のみ。**

### 信号品質予測 (`SignalQualityPredictor`)

- 短期/中期/長期の3つの EMA を線形結合
- `Predict()` で次の RSSI を予測、`EvaluateTrend()` で改善/安定/悪化を判定
- ゼロ外部依存、stdlib のみ

## Consequences

- ユーザーは接続前に Dragonblood / deauth リスクを認識できる
- 同一 SSID の複数 AP から最も堅牢なものを自動推奨できる (`RecommendMostSecure`)
- 信号品質の予測により、接続が不安定になる前に別ネットワークへの切替を促せる
- 攻撃機能を含まないため、デュアルユース懸念がない
- プラットフォーム層 (WindowsWifiService 等) で実際の PMF / transition mode フラグを
  WLAN API から取得する実装が今後必要 (現状はモデルとサービスのみ)
