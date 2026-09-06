# ADR-0005: Multi-channel Distribution (MSI + winget + dotnet tool)

- Status: Accepted
- Date: 2026-04-25

## Context

v0.x はリリースなし、`.sln` をクローンしてビルドが唯一の入手手段。一般ユーザー導入不可能。

## Decision

3 チャネルで配布:
1. **MSI**(WiX v4)— 一般ユーザー、企業 IT
2. **winget**(Microsoft Store 経由)— Windows 11 デフォルト
3. **dotnet tool**(`mwc-cli` パッケージ)— 開発者の CLI のみ需要

ARM64 ネイティブビルドを全チャネルで提供。

## Consequences

### 良い影響
- 各ユーザー層へ最適なチャネル
- winget で `winget install ShizukuTanaka.MWC` の 1 コマンド
- ARM64 ネイティブで Snapdragon X Elite 等対応
- Sigstore 署名 + SLSA provenance で信頼性担保

### 悪い影響
- ビルドマトリクス x 配布チャネルで CI 時間増
  - 緩和: GitHub Actions の matrix で並列化
- winget manifest はリリース毎に SHA256 更新の手動 PR 必要
  - 緩和: `wingetcreate update` で半自動化

## Alternatives Considered

| 候補 | 不採用理由 |
|---|---|
| Inno Setup | XML ベースの宣言的定義に劣る、保守性低 |
| MSIX のみ | コードシグニング証明書必須、個人開発者にコスト負担 |
| Chocolatey | winget がプレインストール時代に追加優先度低(別途追加可) |
| GitHub Releases zip のみ | ユーザビリティ低、PATH 設定が手動 |
| Microsoft Store | 審査リードタイム、開発者登録費用 |

将来追加候補: MSIX(コード署名証明書取得後)、Chocolatey、Scoop。

## 2026-09 追記 — MSI チャネルは版数不一致で未稼働

`installer/wix/Product.wxs` はファイル収集(harvest)に `<Files Include="...">`
ワイルドカード構文を使っているが、これは WiX **v5 以降**の機能であり、この ADR が
決定した「WiX v4」には存在しない(`github.com/wixtoolset/wix` の実ソースを
v4.0.0〜v4.0.6 の全パッチと v5.0.0 で突き合わせて確認済み。xmlns の
URI が `v4/wxs` のままなのはスキーマ世代の命名でツールセットのバージョンとは
無関係——v5.0.0 のテスト .wxs も同じ xmlns を使っている)。このため現状 MSI は
一度もビルドできておらず、`docs/ci/release.yml`(未設置の CI ドラフト)は zip の
みを配布物として扱う設計になっている。将来 MSI を出すなら、WiX を v5 系に
上げるか、この要素を v4 で書ける形に置き換えるかのどちらかが先に要る
(詳細な根拠は `Product.wxs` 自身のコメントと `release.yml` の該当コメントに
記載済み)。winget/dotnet tool の 2 チャネルはこの問題と無関係。

なお `installer/` には ADR 決定時「将来追加候補」だった MSIX/Chocolatey/Scoop の
足場(`installer/msix/`、`installer/chocolatey/`、`installer/scoop/`)も既に
作られている——この ADR 自体は未更新のままだが、実装は既に先へ進んでいる。
