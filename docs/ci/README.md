# CI ワークフロー(残り 3 本は設置待ち、ci.yml は設置済み)

> **ci.yml の正本は `.github/workflows/ci.yml`。** 2026-11 に設置され(PR #6)、
> 以後は実働側で保守されている(PR #7 / #8 で脆弱性検査を OSV 照会方式へ、
> カバレッジ取得を coverlet.collector 方式へ更新)。ここ docs/ci/ に ci.yml の
> 複製は置かない — 設置時点の複製が残っていた間に実働側だけが進化し、
> 旧方式(dotnet list package --vulnerable、coverlet.msbuild 用プロパティ)が
> ここに化石として残る乖離が実際に起きたため。
>
> ここに残る 3 本(`codeql.yml` / `oui-update.yml` / `release.yml`)については、
> **設置されるまでここが正本(single source of truth)**。かつて
> `ci/github-workflows/` にも別バージョンが存在したが、2026-07 に削除して
> 一本化した(そちらは 2026-06-04 版で、こちらの 2026-06-23 版より古かった)。

## 経緯

GitHub がワークフローとして認識するのは `.github/workflows/` だけ。
長らくそこには何も置かれておらず、GitHub Actions は一度も実走していなかった
(詳細と経緯: `docs/FEATURE-AUDIT.md` §0)。2026-11 に所有者側の操作で
`ci.yml` が `.github/workflows/` へ設置され(PR #6)、以後 CI は実走している。

過去に一度、正しい場所へ移設する試み(コミット `1c28a9c`)があったが、**13 秒後に同一
セッション内で自動的にリバートされている**(`9274953`)。真因は GitHub App トークンの
`workflows` スコープ不足だったことが後に判明している(`docs/FEATURE-AUDIT.md` §0)。

## 残り 3 本の設置手順

```bash
cp docs/ci/codeql.yml docs/ci/oui-update.yml docs/ci/release.yml .github/workflows/
git add .github/workflows && git commit -m "ci: install remaining workflows" && git push
```

**`docs/ci/*.yml` を一括コピーしないこと。** 設置済みの ci.yml まで実働側より
古い複製で上書きする事故を防ぐため、ファイル単位でコピーする(そもそも ci.yml は
もうここに無い)。

設置後にやること:

1. `README.md` の CodeQL バッジを復活させる(markup は README 内の HTML コメントに保存済み)
2. `oui-update.yml` 設置で README の「月次自動更新」の主張が真になる。`release.yml`
   設置と最初のリリースで、署名付き配布物についての記述が真になる

## 中身

| ファイル | 内容 |
|---|---|
| (`.github/workflows/ci.yml`) | 設置済み。Windows での build + test(`MWC.ci-win.slnf` 経由)、Linux でのクロスプラットフォーム部分ビルド、OSV による脆弱性検査、カバレッジ計測。正本は実働側 |
| `codeql.yml` | CodeQL による SAST |
| `oui-update.yml` | IEEE OUI ベンダー DB の月次更新(差分があれば PR を作る)。README が謳う「月次自動更新」はこれを設置して初めて真になる |
| `release.yml` | タグ push で zip をビルドし、CycloneDX SBOM・Sigstore keyless 署名・SLSA provenance・SHA256SUMS を付けて Release を作る。README / SECURITY.md が謳う配布物保護はこれを設置して初めて真になる |

## 設置前に

`bash tools/verify.sh` を実行すること。dotnet 無しで可能な静的検証(XML 整形性・
ロケールキー一致・`MWC.sln` と `*.slnf` の整合性・補完スクリプト構文・孤立サービス検出)を
まとめて走らせる。特に **`.slnf` の検証は重要** — プロジェクトを削除したときに
フィルタ側の参照を消し忘れると、CI の `dotnet restore` が失敗する。
