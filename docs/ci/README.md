# CI ワークフロー(設置待ち)

> **ここが CI 設定の正本(single source of truth)。** かつて `ci/github-workflows/` にも
> 別バージョンが存在したが、2026-07 に削除して一本化した(そちらは 2026-06-04 版で、
> こちらの 2026-06-23 版より古く、`claude/**` 等のブランチ対応と Windows ソリューション
> フィルタ経由のビルドを欠いていた)。

## 現状と、なぜここに置かれているか

**このリポジトリの GitHub Actions は一度も実走していない。** GitHub がワークフローとして
認識するのは `.github/workflows/` だけだが、そこには何も置かれていない
(詳細と経緯: `docs/FEATURE-AUDIT.md` §0)。

過去に一度、正しい場所へ移設する試み(コミット `1c28a9c`)があったが、**13 秒後に同一
セッション内で自動的にリバートされている**(`9274953`)。エージェント実行環境の
`.github/workflows/` 書込みガードレールによる自動差し戻しと推測される。
そのため**リポジトリ所有者による直接操作、または明示的な許可が必要**。

## 設置手順

```bash
mkdir -p .github/workflows
cp docs/ci/*.yml .github/workflows/
git add .github/workflows && git commit -m "ci: install workflows" && git push
```

設置後にやること:

1. `README.md` の CI / CodeQL バッジを復活させる(markup は README 内の HTML コメントに保存済み)
2. 実際に `dotnet test` が走った実測値でテストバッジを `N passing` に更新する
   (現在は静的に数えた `N methods` 表記。実数は `README.md` のバッジが唯一の出所で、
   `tools/verify.sh` が実測と突き合わせている。ここに数値を複製しないこと)
3. `docs/FEATURE-AUDIT.md` §0 を解決済みに更新する

## 中身

| ファイル | 内容 |
|---|---|
| `ci.yml` | Windows での build + test(`MWC.ci-win.slnf` 経由)、Linux でのクロスプラットフォーム部分ビルド |
| `codeql.yml` | CodeQL による SAST |
| `oui-update.yml` | IEEE OUI ベンダー DB の月次更新(差分があれば PR を作る)。README が謳う「月次自動更新」はこれを設置して初めて真になる |

## 設置前に

`bash tools/verify.sh` を実行すること。dotnet 無しで可能な静的検証(XML 整形性・
ロケールキー一致・`MWC.sln` と `*.slnf` の整合性・補完スクリプト構文・孤立サービス検出)を
まとめて走らせる。特に **`.slnf` の検証は重要** — プロジェクトを削除したときに
フィルタ側の参照を消し忘れると、CI 設置直後の `dotnet restore` が失敗する。
