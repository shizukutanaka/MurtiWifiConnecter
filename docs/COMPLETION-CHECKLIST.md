# 完成までのチェックリスト(リポジトリ所有者向け)

> **これは「AI セッションが到達できなかった残作業」を、権限を持つ人が実行できる形にした手順書。**
> 各項目に「なぜ AI が実行できなかったか(実際に試した結果)」と「あなたが何をすればよいか」を書いてある。
> 作業方法一般は [AI-SESSION-HANDBOOK.md](AI-SESSION-HANDBOOK.md)、
> 機能の過不足詳細は [FEATURE-AUDIT.md](FEATURE-AUDIT.md) を参照。
> 製品全体の長所・短所・改善点の総括は同ファイルの **§6**(2026-08 ソクラテス問答パス)にある。

作成: 2026-07 の監査・改善セッション。

---

## 全体像

残る作業は **4 件**。うち 2 件は権限操作(数分)、2 件は Windows 実機での実装。

| # | 項目 | 種別 | 所要 | 依存 |
|---|---|---|---|---|
| 1 | CI を稼働させる | 権限 | 数分 | なし。**最優先** |
| 2 | GitHub Release を作る | 権限 | 数分 | 1 が済んでいると望ましい |
| 3 | MLO のリンク詳細(RSSI のみ実機。band/channel は RNR に既出) | 実装 | 半日〜 | Windows 実機は RSSI 部分のみ |
| 4 | 現在の MAC を自動取得して `--mac` の既定にする | 実装 | 数時間 | Windows 実機(判定ロジックは Core 化済み) |

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
bash tools/verify.sh                    # ネットワーク・SDK 不要の静的検査
bash tools/typecheck-core.sh            # MWC.Core を実際にコンパイル
bash tools/typecheck-cli.sh --selftest  # MWC.Cli を型検査 (スタブ + 自己検証付き)
bash tools/typecheck-app-services.sh    # MWC.App のうち検査可能な分 (ViewModel 含む。件数を表示)
bash tools/typecheck-tests.sh --selftest # テスト (MWC.App 依存分と FsCheck を除く)
bash tools/typecheck-platform.sh        # Platform.Windows のうち循環せず検査できる分
bash tools/run-tests.sh                 # ★テストを実際に実行する (xunit 無しの近似ランナー)
bash tools/mutation-check.sh            # そのスイートに検出力があるかを変異注入で実測
```

### AI セッションで `dotnet build` / `dotnet test` を通したい場合(環境側の設定)

**`api.nuget.org` がエグレスポリシーで拒否されている**ため、restore が必ず失敗する。
プロキシの記録で確定済み(推測ではない):

```
host: api.nuget.org:443
kind: connect_rejected
detail: gateway answered 403 to CONNECT (policy denial or upstream failure)
```

同じ環境の許可リストには `registry.npmjs.org` / `pypi.org` / `index.crates.io` /
`proxy.golang.org` が**入っている**。つまり他言語のパッケージレジストリは通るのに
**NuGet だけが抜けている**。

→ **環境のエグレスポリシーに `api.nuget.org`(および `*.nuget.org`)を追加すれば、
AI セッションでも `dotnet restore` → `build` → `test` が通せる**ようになり、
README のテストバッジが示す数のテストメソッドを CI 設置前に実行できる。
これは GitHub の `workflows` 権限とは別の、独立した設定である。

**`typecheck-core.sh` は 2026-08 に追加。** `api.nuget.org` が塞がれていても
MWC.Core は SDK 同梱の参照アセンブリだけでコンパイルでき、実際に走らせたところ
**静的検査 11 種が見逃していたビルド破壊 3 件**が出た(`using System.Linq;` 欠落 /
位置引数レコードへの camelCase 名前付き引数 / obsolete API が
`TreatWarningsAsErrors` でエラー化)。**CI 設置前に必ず両方を走らせること。**

特に **restore を落とす 2 つのチェック**が重要。どちらも「CI 設置直後の
`dotnet restore` が失敗する」形で、CI が一度も走っていないため長く気づかれなかった:

1. **`.slnf` チェック** — プロジェクト削除時にソリューションフィルタの参照を
   消し忘れると restore が失敗する(2026-07 に実際に踏んだ。修正済み)。
2. **CPM チェック** — `Directory.Build.props` が
   `<PackageReference Include="Microsoft.SourceLink.GitHub" Version="8.0.0" …>` を
   全プロジェクトに注入していたが、`Directory.Packages.props` は
   `ManagePackageVersionsCentrally=true`。CPM 下でインライン Version は **NU1008 エラー**で、
   **全プロジェクトの restore が落ちる**状態だった(2026-08 に発見・修正済み。
   SDK 10 のローカル restore で実際に再現し、修正後に当該エラーが消えることを確認)。

### 設置後にやること

0. **`README.md` の「MAC ベンダー解決」に「月次自動更新」を書き戻してよい。**
   `docs/ci/oui-update.yml` を設置すると、IEEE OUI DB の月次更新 PR が実際に走るようになる。
   それまでは主張しない(`tools/verify.sh` の automation-claim チェックが強制する)。
1. `README.md` の CI / CodeQL バッジを戻す(markup は README 内の HTML コメントに保存済み)
2. テストバッジを実測値に戻す — 現在は静的に数えた `NNN methods` 表記。
   `dotnet test` の結果で `N passing` にできる
3. `FEATURE-AUDIT.md` §0 を解決済みに更新する
4. **CI が赤くなったら — どこが怪しいかは 2026-08 に絞り込み済み**:
   `MWC.Core` は `tools/typecheck-core.sh` で**実際にコンパイル済み**(`-warnaserror` 込みで green)。
   **Cli も 2026-08 に型検査済み**(`tools/typecheck-cli.sh`。ここでも実在の欠陥 3 件が出た)。
   App は **WPF 非依存の 6 ファイルのみ**検査済み(`tools/typecheck-app-services.sh`)。
   **テストは 2026-08 に型検査だけでなく実行もした** — `tools/run-tests.sh` が
   xunit 無しで反射実行し、初回で 1037 件が合格、**実在の欠陥 4 件**が出た
   (製品側 1: `CatImportService` の二重取り込み / テスト側 3: 常に偽の不変条件)。
   **残る既知の失敗 1 件**: `NetworkHistoryService_ConcurrentWrites_ThreadSafe` は
   テスト間で `LocalApplicationData` の固定パスを共有するため落ちる。
   修正には保存先を注入可能にする API 変更が要るので、判断を所有者に委ねている。

   **テストも 2026-08 に型検査済み**(`tools/typecheck-tests.sh`。ここでも実在の欠陥 5 件が出た)。
   **Platform.Windows も一部は型検査済み**(`tools/typecheck-platform.sh`)。
   一方 **App の XAML コードビハインド・ManagedNativeWifi 依存分・一部のテストは型検査されていない**。
   XAML 分は 2026-08 に実測済み(15 クラス / 72 フィールド / 20 コントロール型)。
   生成自体は可能だが、コントロールのメンバを「コードが要求した順に」足す形になり
   検査が空洞化するため見送った。**`Microsoft.WindowsDesktop.App.Ref` を入れるのが正攻法**
   (詳細は `tools/stubs/WpfMinimal.Stub.cs` のヘッダ)。
   (それぞれ System.CommandLine beta4 / WPF 参照パック / ManagedNativeWifi / xunit が要り、
   いずれもこの環境では入手できないことを確認済み)。構文エラーが無いことだけは確認した。
   **したがって CI が赤くなるとすれば Core 以外の 4 つが第一容疑者。**

   > **注意**: 「本物の `MWC.Core.dll` を参照して Cli をコンパイルし、参照欠落以外の
   > エラーだけ見る」という近道は**効かない**。System.CommandLine が無いと
   > `SetHandler(...)` のデリゲート型がエラー型になり、Roslyn はラムダ本体を
   > 束縛しないため、Core の API 名を間違えていてもエラーが出ない
   > (2026-08 に実際に試して確認)。**「エラーが出なかった」を検証済みと解釈しないこと。**
   Core で実際に出た 3 件はすべて束縛エラー(CS1929 / CS1739 / SYSLIB0057)で、
   同種のものが他プロジェクトに残っている可能性が高い。

   参考: 以前の記載「このセッションの変更はコンパイル検証されていない」は
   Core については**もう当てはまらない**。
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

- `git push --tags` → **HTTP 403**。
  **2026-08 に使い捨てタグで再検証済み**(通常の commit push は成功しているので、
  「push 全般が壊れている」ではなく**タグだけが拒否される**ことを確認した。
  リモートには何も作られておらず、ローカルのタグも削除済み)。
- GitHub MCP ツールを再列挙した結果も同じ — **release / tag を作成するツールが存在しない**
  (`get_latest_release` / `list_releases` / `get_tag` / `list_tags` など読み取り専用のみ)。

> このセッションでは記載済みブロッカーの理由を 1 件ずつ検証し直し、**4 件中 2 件は
> 記載が誤っていた**(項目 3・4)。本項目は**再検証しても記載どおり**だった。

プロキシの README が「403 はリトライ・回避せず報告する」と定めているため、迂回は試していない。

### あなたがやること

**推奨: `docs/ci/release.yml` を設置してタグを push する。**
そうするとビルド・テスト・CycloneDX SBOM・Sigstore keyless 署名・SLSA provenance・
SHA256SUMS まで一括で行われ、README / SECURITY.md が謳う配布物保護が初めて実体を持つ。
(項目 1 のコピー手順 `cp docs/ci/*.yml .github/workflows/` に含まれている)

```bash
git tag v3.12.0 && git push --tags
```

**手動で作る場合**は GitHub UI から: Releases → Draft a new release
— ただしこの場合、署名も SBOM も provenance も付かない。
その状態で README / SECURITY.md の免責(「署名済みの配布物は存在しない」)を
外してはいけない(`tools/verify.sh` が検出する)。

- タグ: `v3.12.0`
- 対象コミット: `ad36a92`(master の該当バージョン)
- 本文: `CHANGELOG.md` の `[3.12.0]` セクションをそのまま使える

または手元から `git tag v3.12.0 && git push --tags`。

> 未リリース分(`[Unreleased]`)は本セッションの改善が大量に入っているため、
> CI を通してから次バージョンとして切るのがよい。

---

## 3. MLO のリンク詳細(実機が要るのは RSSI だけ — 残りは広告されている)

### 現状

Wi-Fi 7 の **MLO 対応判定は 2026-07 に実装済み** — 802.11be Multi-Link 要素を
ビーコンから検出し `WifiNetwork.IsMlo` を設定する(`BeaconIeParser.HasMultiLink`)。
スキャン一覧で Wi-Fi 7 AP を見分けるにはこれで足りる。

**残るのはリンクごとの詳細** — `WifiNetwork.MloLinks`(各リンクの帯・チャネル・**RSSI**・帯域幅)。

**2026-08 修正**: `MloAnalyzerService` は以前 `IsMlo && MloLinks.Count > 0` の両方を要求し、
どちらか欠けると `IsMlo: false` を返していた。これは Wi-Fi 7 AP に対して**事実と異なる**答えで、
`BeaconIeApplier` が立てた `WifiNetwork.IsMlo` は唯一の消費者であるここで握り潰されていた
(= 2026-07 のビーコン検出は誰にも届いていなかった)。
2 つの問いに分割し、MLO 広告あり・リンク詳細なしの場合は
`IsMlo: true, LinkCount: 0` を返すようにした。**GUI の MLO 行は表示されるようになった**
(「Wi-Fi 7 (MLO) 対応 — リンク別詳細は取得不可」)。リンク数や集約速度は
**表示しない** — 測っていない値を測ったように見せないため。

### なぜ Core に切り出せないか(2026-08 に範囲を訂正 — 全部が実測ではない)

以前この節は「`MloLink` が要求する `Rssi` は実測値だから Core に切り出せない」と
だけ書いていた。**RSSI についてはその通りだが、リンク詳細の残りはそうではない。**

第 3 の軸(`AI-SESSION-HANDBOOK.md` §3)で問い直した結果:

| `MloLink` のフィールド | 広告されるか | 状態 |
|---|---|---|
| `LinkId` | ✅ RNR の MLD Parameters / Multi-Link の Per-STA Profile に含まれる | **未パース** |
| `Band` / `Channel` / `FrequencyMhz` | ✅ RNR の Operating Class + Channel から求まる | **既にパース済み**(`RnrNeighborAp`) |
| `ChannelWidth` | △ Operating Class から推定可 | 未実装 |
| `Rssi` | ❌ **実測値**。ビーコンには無い | ランタイム API が要る |

つまり実機が要るのは **`Rssi` だけ**で、他は Core で埋められる。
`RnrParser` は既に Operating Class・Channel・BSSID を取り出しており
(`BeaconIeSummary.RnrNeighbors`)、その情報は**現在どこからも使われていない**。

**ただし RNR の近隣 AP = MLO リンクではない。** RNR は 6GHz 探索のための
一般的な近隣 AP 広告であり、同一 AP MLD に属するかどうかは
**TBTT Information Field の MLD Parameters**(TBTT Info Length が該当長のときのみ存在)
で判定する必要がある。`RnrParser` はこのフィールドの手前で読み取りを止めている。
**RNR エントリを無条件に MLO リンクとして扱ってはならない** — 別バンドの
無関係な AP をリンクとして表示することになる。

> 本パスでこのパースを実装しなかった理由: MLD Parameters のビット配置を
> 手元の資料で確定できず、**推測でビット位置を書くのは、このセッションが
> 繰り返し是正してきた誤りそのもの**だから。仕様(802.11be D3.0 9.4.2.170.2)か
> 実測キャプチャで裏を取れる者が実装すべき。

### 実装の手がかり

**先に直すべきモデルの罠**: `MloLink.Rssi` は `int`(非 null)で既定 0。
広告情報だけでリンクを埋めると **RSSI が全部 0 のまま** になり、
`MloAnalyzerService.BestLink` は `OrderByDescending(l => l.Rssi)` で
**誰も測っていない値で「最良リンク」を選んで表示する**。
先に `int?` にして、未測定時は RSSI 依存の結論(`BestLink`・リンク間差分)を
出さないようにすること。現状 `MloLinks` は常に空なので実害は出ていないが、
埋めた瞬間に顕在化する。

- RSSI の API: `ManagedNativeWifi` v3.0.1+ の
  `NativeWifi.GetRealtimeConnectionQuality`(Win11 24H2+)
- **型名衝突に注意**: `ManagedNativeWifi.PhyType` と `MWC.Core.Models.PhyType`
- API 形状の調査結果は `docs/arxiv-improvement-analysis.md` の 2026-H2 追補にある
- 埋める先は `WifiNetwork.MloLinks`。`MloAnalyzerService` と GUI は配線済みなので、
  データが入れば表示される


---

## 4. 現在の MAC の自動取得(判定ロジックは 2026-08 に Core 化済み)

### 現状

MAC ランダム化の判定は **2026-08 に Core へ切り出した**
(`MacAddressModeInference`)。アドレスのバイト列だけで
「ランダム化されているか」が決まるため、OS 設定の照会は要らない。

CLI からは今日使える:

```powershell
ipconfig /all                          # Wi-Fi アダプターの Physical Address を見る
mwc privacy --mac AA:BB:CC:DD:EE:FF    # アドレスから判定して勧告を出す
```

`--mac-mode`(自己申告)は互換のため残してあるが、`--mac` の方が強い —
ユーザーの申告よりアドレスのビットの方が確かなため。

**残っているのは「現在の MAC を自動で取ってくる配線」だけ。**

### かつて「Core に切り出せない」と書いていた理由と、それが誤りだった訳

この節は以前こう述べていた:

> これは **OS の設定値そのもの**で、ビーコンに広告される情報ではない。
> **解析すべきバイト列が存在しない**ため…Core へ切り出すことができない

これは**問いの取り違え**だった。勧告に要るのは *設定* ではなく **効果** —
「いま使われている MAC はランダム化されたものか」— であり、
それはアドレスのバイト列に現れる。解析すべきバイト列は存在した。

- IEEE 802: オクテット 0 の bit 1 = **Locally Administered (LAA)**。
  IEEE 割当の焼き込みアドレスは必ず LAA=0。ランダム生成 MAC は実在 OUI との
  衝突を避けるため LAA=1 にする決まりで、Windows のランダム化もこれに従う。
- よって **LAA=1 → ランダム化済み / LAA=0 → 焼き込み**。設定照会は不要。
- 決まらないのは **種類**(ネットワーク別か日次か)だけで、これは
  複数観測の突合で決まる(`MacAddressModeInference.FromHistory`)。

判定基準そのものが誤っていたわけではなく、**適用を誤った**。
`AI-SESSION-HANDBOOK.md` §3 に第 3 の軸として追記済み:
**設定は読めなくても、その効果が観測値に現れるなら Core で判定できる。**

### 残作業: 現在の MAC を自動供給する

- 埋める先は `PrivacyCommand` の `--mac` 既定値
  (ユーザー指定は上書きとして残すのがよい)。
- **必要なのは Windows 固有 API ではない見込み。**
  `System.Net.NetworkInformation.NetworkInterface.GetPhysicalAddress()` は BCL であり、
  P/Invoke も WMI も要らない。WLAN アダプターの GUID と `NetworkInterface.Id` を
  突き合わせる部分だけがプラットフォーム依存になる。
  **この見込みは実機で未検証**のため、配線自体はまだ書いていない。
- `IWifiService` にはアダプターの MAC を返す口が無いので、
  `GetAdaptersAsync` が返す `WifiAdapter` に足すのが素直
  (現在の `WifiAdapter` は Id/Name/Description/State/ConnectedSsid のみ)。
- 履歴からの種類判定 (`FromHistory`) を使うなら、接続の度に
  (SSID, MAC, 時刻) を記録する必要がある。`NetworkHistoryService` が近い。


---

## 参考: このセッションで到達したこと

| | 結果 |
|---|---|
| 孤立サービス | 11 個 → **2 個**(残る 2 つはいずれも正当な用途あり) |
| 削除 | 1,393 行(動作しないモバイルスタブ、データ源の無いサービス、未配線の重複実装、Core の不要依存) |
| 新機能 | GUI の Enterprise 認証情報入力 / `mwc import-cat`(eduroam)/ `mwc passpoint` / `mwc privacy` |
| セキュリティ | RADIUS サーバ検証の強制、PEAP の V2 拡張、evil twin 防御の永続化、BSSID の位置プライバシー是正 |
| 検証基盤 | `tools/verify.sh`(dotnet 無しで走る静的チェック一式) |

**未検証**: 上記はすべて静的チェックのみ。項目 1 の CI が動いて初めて実行検証される。
