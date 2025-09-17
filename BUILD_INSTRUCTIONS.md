# MurtiWifiConnecter - ビルド手順

## 必要な環境

### システム要件
- **OS**: Windows 10/11 (x64)
- **.NET**: .NET 6.0 SDK以降
- **Visual Studio**: 2022 (推奨) または Visual Studio Code

### 必須コンポーネント
1. **.NET 6.0 SDK**
   - [Microsoft公式サイト](https://dotnet.microsoft.com/download/dotnet/6.0)からダウンロード
   - または`winget install Microsoft.DotNet.SDK.6`でインストール

2. **Windows SDK** (WPFアプリケーション用)
   - Visual Studioインストール時に自動的にインストールされます

## ビルド手順

### 1. 前提条件の確認
```bash
# .NET SDKの確認
dotnet --version
# 6.0.x以降が表示されることを確認

# プロジェクトファイルの確認
dir MurtiWifiConnecter.csproj
```

### 2. 依存関係の復元
```bash
dotnet restore
```

### 3. デバッグビルド
```bash
dotnet build
```

### 4. リリースビルド
```bash
dotnet build --configuration Release
```

### 5. 実行
```bash
dotnet run
```

## 出力ファイル

### デバッグビルド
- **場所**: `bin\Debug\net6.0-windows\`
- **実行ファイル**: `MurtiWifiConnecter.exe`

### リリースビルド
- **場所**: `bin\Release\net6.0-windows\`
- **実行ファイル**: `MurtiWifiConnecter.exe`

## 配布パッケージの作成

### 単一ファイル配布
```bash
dotnet publish --configuration Release --runtime win-x64 --self-contained false --single-file
```

### フレームワーク依存配布
```bash
dotnet publish --configuration Release --runtime win-x64 --self-contained false
```

### 自己完結型配布
```bash
dotnet publish --configuration Release --runtime win-x64 --self-contained true
```

## トラブルシューティング

### よくある問題と解決方法

#### 1. "dotnet command not found"
**原因**: .NET SDKがインストールされていない、またはPATHが設定されていない

**解決方法**:
1. [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)をダウンロードしてインストール
2. コマンドプロンプトを再起動
3. `dotnet --version`で確認

#### 2. NuGetパッケージの復元エラー
**原因**: ネットワーク接続、またはNuGet設定の問題

**解決方法**:
```bash
# NuGetキャッシュのクリア
dotnet nuget locals all --clear

# パッケージの再復元
dotnet restore --force
```

#### 3. WPFリソースの読み込みエラー
**原因**: XAMLファイルのビルドアクションが正しく設定されていない

**解決方法**:
- Visual Studioでプロジェクトを開く
- XAMLファイルのプロパティで「ビルドアクション」を「Page」に設定

#### 4. 権限エラー
**原因**: 管理者権限が必要な機能を使用している

**解決方法**:
- Visual Studioまたはコマンドプロンプトを管理者として実行
- `app.manifest`で`requireAdministrator`が設定されていることを確認

## 開発環境の設定

### Visual Studio 2022
1. **ワークロード**: ".NET デスクトップ開発"をインストール
2. **拡張機能**:
   - XAML Styler (推奨)
   - ResXManager (多言語対応時)

### Visual Studio Code
1. **必須拡張機能**:
   - C# Dev Kit
   - .NET Install Tool
   - XAML (推奨)

## パフォーマンス最適化

### リリースビルド設定
```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
  <Optimize>true</Optimize>
  <DebugType>pdbonly</DebugType>
  <DefineConstants>TRACE</DefineConstants>
  <TrimUnusedDependencies>true</TrimUnusedDependencies>
</PropertyGroup>
```

### AOT (Ahead of Time) コンパイル (オプション)
```bash
dotnet publish --configuration Release --runtime win-x64 -p:PublishAot=true
```

## 品質保証

### 単体テスト実行
```bash
dotnet test
```

### コード分析
```bash
dotnet build --verbosity normal /p:RunAnalyzersDuringBuild=true
```

### パッケージ脆弱性チェック
```bash
dotnet list package --vulnerable
```

## 継続的インテグレーション

### GitHub Actionsの例
```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v3
  with:
    dotnet-version: 6.0.x

- name: Restore dependencies
  run: dotnet restore

- name: Build
  run: dotnet build --no-restore --configuration Release

- name: Test
  run: dotnet test --no-build --configuration Release
```

## サポート

### ビルド問題の報告
- **GitHub Issues**: [プロジェクトURL]/issues
- **必要な情報**:
  - OS version (`winver`)
  - .NET version (`dotnet --info`)
  - エラーメッセージの全文
  - ビルドログ (`dotnet build --verbosity diagnostic`)

### 参考資料
- [.NET 6.0 公式ドキュメント](https://docs.microsoft.com/dotnet/core/)
- [WPF アプリケーション開発ガイド](https://docs.microsoft.com/dotnet/desktop/wpf/)
- [.NET アプリケーション配布ガイド](https://docs.microsoft.com/dotnet/core/deploying/)