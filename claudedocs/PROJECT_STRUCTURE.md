# プロジェクト構造ガイド

## ディレクトリ構成

```
MurtiWifiConnecter/
├── Core/                    # 基盤インフラストラクチャ
│   ├── CommonTypes.cs       # 共通型定義 (Result<T>, NetworkInfo等)
│   ├── Services.cs          # 統合サービスアクセス
│   ├── Interfaces.cs        # インターフェース定義
│   ├── WifiOperations.cs    # WiFi操作実装
│   ├── NetworkScanning.cs   # ネットワークスキャン
│   ├── ProcessExecutor.cs   # プロセス実行エンジン
│   └── Logging.cs          # 基本ログシステム
│
├── Personal/               # 個人・家庭向け機能
│   ├── PersonalWifiSystem.cs      # トップレベル統合管理
│   ├── PersonalWifiCoordinator.cs # 機能調整・イベント連携
│   ├── PersonalWifiAssistant.cs   # 軽量監視 (2分間隔)
│   ├── PersonalDashboard.cs       # リアルタイム監視
│   ├── PersonalTrayManager.cs     # システムトレイ統合
│   ├── PersonalSettingsManager.cs # 設定管理
│   ├── PersonalLoggingSystem.cs   # 個人向けログ
│   ├── PersonalWifiTests.cs       # 個人向けテスト
│   ├── HomeWifiManager.cs         # 家庭ネットワーク管理
│   ├── BatteryAwareManager.cs     # バッテリー最適化
│   ├── SimpleWifiDoctor.cs        # ワンクリック診断・修復
│   ├── FamilyNetworkProfiles.cs   # 家族プロファイル・制限
│   ├── AutoStartupManager.cs      # 自動起動管理
│   └── LightweightPerformance.cs  # 軽量パフォーマンス
│
├── UI/                     # ユーザーインターフェース
│   ├── MainWindow.xaml.cs  # メインウィンドウ
│   ├── App.xaml.cs        # アプリケーション
│   └── QuickSetupWizard.cs # 3ステップセットアップ
│
├── claudedocs/            # 開発・分析ドキュメント
│   ├── CODE_CONSOLIDATION_SUMMARY.md
│   ├── performance-analysis-report.md
│   └── PROJECT_STRUCTURE.md (このファイル)
│
├── Properties/            # プロジェクト設定
├── Program.cs            # エントリーポイント
├── SimpleCLI.cs         # CLI インターフェース
├── README.md            # プロジェクト概要
└── CHANGELOG.md         # 変更履歴
```

## アーキテクチャ設計

### Core層 (基盤)
**責務**: 低レベルなシステム操作、型定義、共通機能
**特徴**: 
- 他の層に依存しない独立性
- Rob Pike的な小さなインターフェース
- 高いテスト可能性

### Personal層 (ビジネスロジック)
**責務**: 個人・家庭利用に特化した機能
**特徴**:
- Core層の機能を組み合わせて価値を提供
- 家族安全機能、バッテリー最適化
- 2分間隔の軽量監視

### UI層 (プレゼンテーション)
**責務**: ユーザーとのやり取り
**特徴**:
- Personal層のサービスを利用
- 3ステップセットアップ
- システムトレイ統合

## 依存関係ルール

```
UI層 → Personal層 → Core層
  ↓        ↓         ↓
  X   →    X    →   OS
```

- **上位層は下位層に依存可能**
- **下位層は上位層に依存禁止**
- **同階層内は相互依存を最小化**

## ファイル分類基準

### Core配置基準
- OSレベルの操作 (netsh, WMI等)
- 型定義・共通ユーティリティ
- フレームワーク・インフラストラクチャ
- 他の機能から独立している

### Personal配置基準
- 個人・家庭利用の価値を直接提供
- 家族管理、バッテリー最適化
- Core機能を組み合わせたサービス
- ドメインロジックを含む

### UI配置基準
- ユーザーとの直接的なやり取り
- XAML/WPF関連
- イベントハンドリング
- 表示・入力処理

## 設計原則の適用

### John Carmack原則
- **Core**: 直接的で効率的な実装
- **Personal**: 実用的な機能に集中
- **UI**: レスポンシブなユーザー体験

### Robert C. Martin原則
- **単一責任**: 各ディレクトリが明確な責務
- **依存関係逆転**: 抽象化への依存
- **開放閉鎖**: 拡張可能な設計

### Rob Pike原則
- **小さなインターフェース**: Core層のシンプルなAPI
- **組み合わせ可能**: Personal層での機能組み合わせ
- **複雑さの隠蔽**: UI層での使いやすさ