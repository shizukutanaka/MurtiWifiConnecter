# 完成までのチェックリスト(リポジトリ所有者向け)

> **これは「AI セッションが到達できなかった残作業」を、権限を持つ人が実行できる形にした手順書。**
> 各項目に「なぜ AI が実行できなかったか(実際に試した結果)」と「あなたが何をすればよいか」を書いてある。
> 作業方法一般は [AI-SESSION-HANDBOOK.md](AI-SESSION-HANDBOOK.md)、
> 機能の過不足詳細は [FEATURE-AUDIT.md](FEATURE-AUDIT.md) を参照。

作成: 2026-07 の監査・改善セッション。

---

## 全体像

残る作業は **4 件**。うち 2 件は権限操作(数分)、2 件は Windows 実機での実装。

| # | 項目 | 種別 | 所要 | 依存 |
|---|---|---|---|---|
| 1 | CI を稼働させる | 権限 | 数分 | なし。**最優先** |
| 2 | GitHub Release を作る | 権限 | 数分 | 1 が済んでいると望ましい |
| 3 | MLO のリンク詳細を実装 | 実装 | 半日〜 | Windows + dotnet |
| 4 | MAC モードの自動検出 | 実装 | 半日〜 | Windows + dotnet |

**1 が最優先**である理由: このリポジトリのコードは **GitHub Actions で一度も検証されたことがない**。
2026-07 セッションの全変更(約 3,900 行の追加を含む)も静的チェックのみで、
コンパイルもテスト実行も行われていない。1 が済めば、`README.md` のテストバッジが示す数の
テストメソッドが初めて実行される(数値をここに複製しないのは、腐って実測と食い違うため。
README の数値は `tools/verify.sh` が実測と突き合わせて検証している)。

---

## 1. CI を稼働させる 🔴 最優先

### なぜ AI ができなかったか(実測)

ローカルでの書き込みとコミットは**成功する**。`.claude/settings.json` は
`Write(.github/**)` を permissions で明示的に許可しており、deny にも入っていない。
拒否するのは **GitHub 側**で、push 時にこう返る:

```
refusing to allow a GitHub App to create or update workflow
.github/workflows/ci.yml without workflows permission
```

GitHub API 経由(MCP)でも同じ理由で `403 Resource not accessible by integration`。
**2 経路とも同じ原因** — push に使われる GitHub App トークンに `workflows` スコープが無い。

> ⚠️ **AI セッションがこれを試す場合の注意**: このコミットを作ると
> **そのブランチへの以降の push がすべて失敗する**。試して拒否されたら
> `git reset --hard HEAD~1` で戻すこと。

### あなたがやること(どちらか)

**A. 自分で push する(最短)**

```bash
git checkout claude/deepresearch-ultrathink-improvement-aFdkE   # または master
mkdir -p .github/workflows
cp docs/ci/*.yml .github/workflows/
git add .github/workflows
git commit -m "ci: install workflows"
git push
```

**B. GitHub App に `workflows` 権限を付与する**
→ 以降は AI セッションからも設置できるようになる。

### 設置前の確認

```bash
bash tools/verify.sh
```

特に `.slnf` チェックが重要。プロジェクトを削除したときにソリューションフィルタの
参照を消し忘れると、**CI 設置直後の `dotnet restore` が失敗する**
(2026-07 に実際に踏んだ。現在は修正済み)。

### 設置後にやること

1. `README.md` の CI / CodeQL バッジを戻す(markup は README 内の HTML コメントに保存済み)
2. テストバッジを実測値に戻す — 現在は静的に数えた `NNN methods` 表記。
   `dotnet test` の結果で `N passing` にできる
3. `FEATURE-AUDIT.md` §0 を解決済みに更新する
4. **CI が赤くなったら**: このセッションの変更はコンパイル検証されていない。
   特に WPF 側(`ConnectDialog` の Enterprise パネル)は静的検証しかできていない
   (XAML パース・`x:Name` とコードビハインドの対応・リソースキーとテーマブラシの実在は確認済み)。

   ただし**コンパイルの下限は 2026-07 に静的監査済み**で、以下は確認できている
   (= CI が赤くなるなら typo より深い意味的問題の可能性が高い):
   - 新規 CLI コマンド 3 件の `SetHandler` アリティ(オプション数 = ラムダ引数数)一致
   - 新規テストが参照する Core API メンバ 13 種すべての実在
   - record の `with` 式で使う全フィールド・全 enum 値の実在
   - 全新規ファイルの `using` 充足(型が推論される箇所は不要)
   - テストクラス名の重複なし(あれば即コンパイルエラー)
   - `BeaconIeSummary` への追加フィールドは optional 既定で既存構築を壊さない
   未確認なのは**意味論**(実行時挙動)と **WPF/プラットフォーム層の実コンパイル**のみ。

---

## 2. GitHub Release を作る

### なぜ AI ができなかったか(実測)

- `git push --tags` → 組織の egress ポリシーで **HTTP 403**
- GitHub MCP ツールを全数(約 50 個)列挙した結果、**release 作成ツールが存在しない**
  (`get_latest_release` / `list_releases` など読み取り専用のみ)

プロキシの README が「403 はリトライ・回避せず報告する」と定めているため、迂回は試していない。

### あなたがやること

GitHub UI から: Releases → Draft a new release

- タグ: `v3.12.0`
- 対象コミット: `ad36a92`(master の該当バージョン)
- 本文: `CHANGELOG.md` の `[3.12.0]` セクションをそのまま使える

または手元から `git tag v3.12.0 && git push --tags`。

> 未リリース分(`[Unreleased]`)は本セッションの改善が大量に入っているため、
> CI を通してから次バージョンとして切るのがよい。

---

## 3. MLO のリンク詳細を実装(Windows 実機)

### 現状

Wi-Fi 7 の **MLO 対応判定は 2026-07 に実装済み** — 802.11be Multi-Link 要素を
ビーコンから検出し `WifiNetwork.IsMlo` を設定する(`BeaconIeParser.HasMultiLink`)。
スキャン一覧で Wi-Fi 7 AP を見分けるにはこれで足りる。

**残るのはリンクごとの詳細** — `WifiNetwork.MloLinks`(各リンクの帯・チャネル・**RSSI**・帯域幅)。
`MloAnalyzerService` は `IsMlo && MloLinks.Count > 0` の**両方**を要求するため、
GUI の MLO 行はまだ表示されない。

### なぜ Core に切り出せないか

`MloLink` が要求する `Rssi` は**リンクごとの実測受信強度**で、ビーコンには含まれない。
接続中のランタイム API からしか得られない。
(802.11u Interworking や MLO 対応判定が Core に切り出せたのは、あれらが
「広告される静的な能力情報」だったため。判断基準は AI-SESSION-HANDBOOK §3 参照)

### 実装の手がかり

- API: `ManagedNativeWifi` v3.0.1+ の `NativeWifi.GetRealtimeConnectionQuality`(Win11 24H2+)
- **型名衝突に注意**: `ManagedNativeWifi.PhyType` と `MWC.Core.Models.PhyType`
- API 形状の調査結果は `docs/arxiv-improvement-analysis.md` の 2026-H2 追補にある
- 埋める先は `WifiNetwork.MloLinks`。`MloAnalyzerService` と GUI は配線済みなので、
  データが入れば表示される

---

## 4. MAC モードの自動検出(Windows 実機)

### 現状

`mwc privacy` は 2026-07 に配線済み。ただし MAC ランダム化状態を検出する層が無いため、
**ユーザーが `--mac-mode` で手渡す**設計になっている
(`hardware` | `random-per-network` | `random-daily`)。

省略すると `Unknown` になり、コマンドは「助言できない」旨と Windows 設定の確認手順を表示する
(`PrivacyCommand` の Unknown 分岐)。**「勧告ゼロ = 問題なし」とは表示しない** —
両者を混同すると、設定を伝えていないユーザーに「あなたのプライバシーは良好」と
誤読させるため、意図的に分けてある。

### なぜ Core に切り出せないか

これは **OS の設定値そのもの**で、ビーコンに広告される情報ではない。
解析すべきバイト列が存在しないため、802.11u Interworking や MLO 対応判定のように
Core へ切り出すことができない
(判断基準は `AI-SESSION-HANDBOOK.md` §3 の「広告される/実測される」)。

### 実装の手がかり

- Windows の「ランダムハードウェアアドレス」設定
  (設定 → ネットワークとインターネット → Wi-Fi)を読み、`MacAddressMode` にマップする
- 埋める先は `PrivacyAdvisoryService.Analyze` の第 1 引数。
  実装後は `--mac-mode` の**既定供給元**になる(ユーザー指定は上書きとして残すのがよい)
- **注意**: `IWifiService` には現在アダプターの能力・設定を返す口が無い。
  取得経路を足す必要がある(`GetAdaptersAsync` が返す `WifiAdapter` の拡張が素直)

---

## 参考: このセッションで到達したこと

| | 結果 |
|---|---|
| 孤立サービス | 11 個 → **2 個**(残る 2 つはいずれも正当な用途あり) |
| 削除 | 1,393 行(動作しないモバイルスタブ、データ源の無いサービス、未配線の重複実装、Core の不要依存) |
| 新機能 | GUI の Enterprise 認証情報入力 / `mwc import-cat`(eduroam)/ `mwc passpoint` / `mwc privacy` |
| セキュリティ | RADIUS サーバ検証の強制、PEAP の V2 拡張、evil twin 防御の永続化、BSSID の位置プライバシー是正 |
| 検証基盤 | `tools/verify.sh`(7 チェック。dotnet 無しで走る) |

**未検証**: 上記はすべて静的チェックのみ。項目 1 の CI が動いて初めて実行検証される。
