# 完成までのチェックリスト(リポジトリ所有者向け)

> **これは「AI セッションが到達できなかった残作業」を、権限を持つ人が実行できる形にした手順書。**
> 各項目に「なぜ AI が実行できなかったか(実際に試した結果)」と「あなたが何をすればよいか」を書いてある。
> 作業方法一般は [AI-SESSION-HANDBOOK.md](AI-SESSION-HANDBOOK.md)、
> 機能の過不足詳細は [FEATURE-AUDIT.md](FEATURE-AUDIT.md) を参照。
> 製品全体の長所・短所・改善点の総括は同ファイルの **§6**(2026-08 ソクラテス問答パス)にある。

作成: 2026-07 の監査・改善セッション。

---

## 全体像

残る作業は **5 件**。うち 2 件は権限操作(数分)、3 件は Windows 実機での実装。
**項目 5 は最も深刻** — 接続完了検知という CLAUDE.md 必須事項の中核が現状コンパイルできない。

| # | 項目 | 種別 | 所要 | 依存 |
|---|---|---|---|---|
| 1 | CI を稼働させる | 権限 | 数分 | なし。**最優先(権限側)** |
| 2 | GitHub Release を作る | 権限 | 数分 | 1 が済んでいると望ましい |
| 3 | MLO のリンク詳細(RSSI のみ実機。band/channel は RNR に既出) | 実装 | 半日〜 | Windows 実機は RSSI 部分のみ |
| 4 | 現在の MAC を自動取得して `--mac` の既定にする | 実装 | 数時間 | Windows 実機(判定ロジックは Core 化済み) |
| 5 | 🔴 `ConnectionWaiter` の接続完了検知が実 API と不一致 | 設計+実装 | 半日〜1日 | Windows 実機(検証必須)。**実装側の最優先** |

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
4. **CI が赤くなったら — どこが怪しいかは 2026-08 に絞り込み済み**
   (この項目は複数回のセッションにまたがって更新されており、以前の版は
   `NetworkHistoryService_ConcurrentWrites_ThreadSafe` を「残る既知の失敗」と書いていたが、
   それは**既に修正済み**——保存先を注入可能にする API 変更を行い、全件合格するように
   なった(具体的な件数はここに複製しない。`bash tools/run-tests.sh` の実行時出力が
   単一の真実の源)。以下が現在の状態):

   | 対象 | 検査 | 状態 |
   |---|---|---|
   | `MWC.Core` | `tools/typecheck-core.sh` | **実際にコンパイル済み**(`-warnaserror` 込みで green) |
   | `MWC.Cli` | `tools/typecheck-cli.sh --selftest` | 型検査済み(実在の欠陥 3 件が出て修正済み) |
   | `MWC.App` | `tools/typecheck-app-services.sh` | WPF 非依存の **19/46 ファイル**を型検査(件数は実行時表示、ハードコードしない) |
   | `MWC.Platform.Windows` | `tools/typecheck-platform.sh` | ManagedNativeWifi に依存しない **3/6 ファイル**を型検査 |
   | テスト(型検査) | `tools/typecheck-tests.sh --selftest` | MWC.App 依存分と FsCheck を除く **75/79 ファイル** |
   | テスト(実行) | `tools/run-tests.sh` | xunit 無しで反射実行。**現在は全件合格**(件数は実行時表示。当初の実行で実在の欠陥が複数出て修正済み) |
   | 検出力 | `tools/mutation-check.sh` | 意図的な欠陥注入 5 件を全て kill、コメントのみの対照は生存 |

   **依然として型検査も実行もされていないのは実質 2 つ**: App の XAML コードビハインド
   (27 ファイル、`InitializeComponent` partial が要る)・`MWC.Platform.Windows` の
   ManagedNativeWifi 依存分(3 ファイル)。いずれも Windows 実機か
   `Microsoft.WindowsDesktop.App.Ref`/NuGet アクセスの少なくとも一方が要る。
   XAML 分は 2026-08 に実測済み(15 クラス / 72 フィールド / 20 コントロール型)。
   生成自体は可能だが、コントロールのメンバを「コードが要求した順に」足す形になり
   検査が空洞化するため見送った。**`Microsoft.WindowsDesktop.App.Ref` を入れるのが正攻法**
   (詳細は `tools/stubs/WpfMinimal.Stub.cs` のヘッダ)。

   > **注意 (`tools/typecheck-cli.sh` を書いたときの教訓)**: 「本物の `MWC.Core.dll` を
   > 参照してコンパイルし、参照欠落以外のエラーだけ見る」という近道は**効かない**。
   > 未解決のデリゲート型(`SetHandler(...)` 等)があると Roslyn はラムダ本体を
   > 束縛しないため、Core の API 名を間違えていてもエラーが出ない
   > (2026-08 に実際に試して確認)。**「エラーが出なかった」を検証済みと解釈しないこと。**
   > `--selftest` フラグはこれを毎回確かめる。

   Core・Cli・App・Platform.Windows・テストで実際に出た欠陥はすべて**束縛エラー**
   (CS1929 / CS1739 / SYSLIB0057 / CS0246 / CS1061 / CS0029 / CS9035 等)であり、
   静的な構文チェックでは捕まらない種類だった。同種の欠陥が
   XAML コードビハインドと ManagedNativeWifi 依存分にも残っている可能性は排除できない
   ——それらは今も未検査であることに変わりないため。

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

**残っているのは「Windows から現在の MAC を実際に取ってくる」その 1 箇所だけ**
(モデルと CLI 側の配線は済んでいる — 下記追記を参照)。

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

### 2026-08 追記: モデルと CLI 側の配線は完了。残るのは Windows 側の 1 箇所だけ

`WifiAdapter` に `PhysicalAddress`(コロン区切り文字列、null 許容)を追加し、
`PrivacyCommand` は `effectiveMac = --mac ?? ad.PhysicalAddress` という優先順位で
使うよう配線済み(明示 `--mac` が最優先、次に自動供給、その次が自己申告
`--mac-mode`)。この優先順位と `MacAddressModeInference` への受け渡しは
`PrivacyCliContractTests` でテスト済み(Core だけで検証可能なため実行もされている —
`tools/run-tests.sh`)。

**残るのは 1 箇所だけ**: `WindowsWifiService.GetAdaptersAsync` が
`WifiAdapter.PhysicalAddress` を実際に埋めること。

- **必要なのは Windows 固有 API ではない見込み。**
  `System.Net.NetworkInformation.NetworkInterface.GetPhysicalAddress()` は BCL であり、
  P/Invoke も WMI も要らない。WLAN アダプターの GUID と `NetworkInterface.Id` を
  突き合わせる部分だけがプラットフォーム依存になる。
  **この見込みは実機で未検証**のため、配線自体はまだ書いていない
  (書けば動くはずという推測でコードを足すのは、本セッションが繰り返し
  戒めてきた「検証していない主張」そのものになる)。
- 埋めなければ `PhysicalAddress` は null のままで、`mwc privacy` は従来どおり
  `--mac`/`--mac-mode` をユーザーに求める — 退行はしない。
- 履歴からの種類判定 (`FromHistory`) を使うなら、接続の度に
  (SSID, MAC, 時刻) を記録する必要がある。`NetworkHistoryService` が近い。

---

## 5. 🔴 `ConnectionWaiter` の接続完了検知が実 API と一致しない

### 何が起きているか(2026-09 に GitHub 実ソースを取得して実測。推測ではない)

`api.nuget.org` がエグレス拒否のため、この環境では `MWC.Platform.Windows` を
ManagedNativeWifi に対してコンパイルしたことが一度もなかった。そこで
**NuGet を経由せず**、ManagedNativeWifi の公開ソース
(`github.com/emoacht/ManagedNativeWifi`、HEAD の
`Source/ManagedNativeWifi/ManagedNativeWifi.csproj` が `<Version>3.0.2</Version>` —
`Directory.Packages.props` のピン留めと一致することを確認済み)を直接取得し、
実際のコンパイルで突き合わせた。結果、**3 件の実在しない API 参照**が見つかった:

| ファイル | 参照している(架空の)API | 実際の API |
|---|---|---|
| `ConnectionWaiter.cs` / `NetworkStateChangedEventHandlerBridge.cs` | `NativeWifi.NetworkStateChanged`(static event) | 存在しない。`NativeWifi` は static クラスで `public static event` は 0 件(実ソース全体を grep して確認)。状態変化は代わりに **`NativeWifiPlayer`**(構築して使う `IDisposable` の instance クラス)が `NetworkRefreshed` / `ConnectionChanged` / `InterfaceChanged` / `ProfileChanged` / `RadioStateChanged` / `SignalQualityChanged` / `AvailabilityChanged` という **7 つに分かれた** instance イベントとして公開している |
| `NetworkStateChangedEventHandlerBridge.cs` | `ManagedNativeWifi.ChannelBandwidth`(型エイリアス) | 存在しない。実ソースのどこにも無い(大小文字無視で 0 件)。本体では未使用の死んだ `using` だった |
| `WindowsWifiService.cs` の `GetConnectedSsid` | `NativeWifi.EnumerateConnectedNetworks()` | 存在しない。正しくは `NativeWifi.GetCurrentConnection(Guid interfaceId)` — `(ActionResult, CurrentConnectionInfo)` を返し、`CurrentConnectionInfo.Ssid` が同じ `NetworkIdentifier` 型 |

**なぜ重大か**: `ConnectionWaiter` は CLAUDE.md が必須事項として掲げる
「接続成功は `WlanNotification` の `connection_complete` 受信 + 疎通確認の 2 段」の
**前段そのもの**。つまりこのリポジトリの最も安全性に関わる中核メカニズムが、
実際の依存パッケージに対して一度もコンパイルされたことがなかった。
モックを使うテスト(`IWifiService` を差し替える)ではこの欠陥を検出できない —
`WindowsWifiService`/`ConnectionWaiter` 自体が対象から外れているため。
実機 Windows で接続を試すか、NuGet を経由してこのファイル群を実際にビルドして
初めて表面化する種類の欠陥だった。

### 検証方法(再現手順)

```bash
git clone https://github.com/emoacht/ManagedNativeWifi.git /tmp/mnw-src
grep -n 'public static event' /tmp/mnw-src/Source/ManagedNativeWifi/NativeWifi.cs   # 0 件
grep -rni 'channelbandwidth' /tmp/mnw-src/Source/ManagedNativeWifi/*.cs             # 0 件
grep -n 'EnumerateConnectedNetworks\b' /tmp/mnw-src/Source/ManagedNativeWifi/*.cs   # 0 件
grep -n 'GetCurrentConnection' /tmp/mnw-src/Source/ManagedNativeWifi/NativeWifi.cs  # 実在する
```

### 2026-09 に対応済み

- **`WindowsWifiService.GetConnectedSsid`** — `GetCurrentConnection` を使うよう修正済み。
  実 API の形と一致することを、上記手順で取得した実ソースから転記した検証用スタブに対する
  実コンパイルで確認済み(`-warnaserror` 込みで green)。
- **`ConnectionWaiter.cs` / `NetworkStateChangedEventHandlerBridge.cs`** — class doc に
  上記の根拠を全文引用済み。**コードの書き換えはしていない** — 単純な名前の付け替えでは
  済まず、「1 つの状態変化イベント」という現在の設計を実 API の「7 種の instance イベント
  + `IDisposable` ライフサイクル」にどう対応させるかという設計判断が要るため。
  実機で検証できないままの推測実装は、この欠陥そのものより有害になりうる。

### あなたが(または Windows 実機を使えるセッションが)やること

1. `NativeWifiPlayer` を構築し、`ConnectionWaiter`/`WindowsWifiService.SubscribeEventsAsync`
   のライフサイクル(いつ構築し、いつ `Dispose` するか)に組み込む設計を決める。
2. `ConnectionWaiter` が実際に必要としている情報 —
   「指定アダプターが `connected`/`disconnecting`/認証失敗 のどれに遷移したか、
   理由コードは何か」— を、7 種の実イベントのどれ(おそらく `ConnectionChanged` が主、
   `RadioStateChanged`/`ProfileChanged` も要検討)から再構成するかを設計する。
3. 各イベントの実引数型(`ConnectionChangedEventArgs` 等、いずれも
   `Source/ManagedNativeWifi/*EventArgs.cs` に実在)を確認し、
   現在の `NetworkStateChangedEventArgs.State`/`Reason` 相当の情報が
   実際に得られるかを確かめる。得られない情報があれば、`ConnectionWaiter` の
   判定ロジック自体の見直しが要る。
4. 実機 Windows で実際に接続・切断・認証失敗を発生させ、想定どおりに
   `ConnectionOutcome` が解決されることを確認する。

---

## 参考: このセッションで到達したこと

| | 結果 |
|---|---|
| 孤立サービス | 11 個 → **2 個**(残る 2 つはいずれも正当な用途あり) |
| 削除 | 1,393 行(動作しないモバイルスタブ、データ源の無いサービス、未配線の重複実装、Core の不要依存) |
| 新機能 | GUI の Enterprise 認証情報入力 / `mwc import-cat`(eduroam)/ `mwc passpoint` / `mwc privacy` |
| セキュリティ | RADIUS サーバ検証の強制、PEAP の V2 拡張、evil twin 防御の永続化、BSSID の位置プライバシー是正 |
| 静的検証 | `tools/verify.sh`(dotnet 無しで走る静的チェック一式) |
| 型検査 | `tools/typecheck-{core,cli,app-services,platform,tests}.sh` — Core・Cli 全体、App 19/46 ファイル、Platform.Windows 3/6 ファイル、テスト 75/79 ファイルが**本物の MWC.Core.dll に対して**コンパイルされる(スタブは `--selftest` で検出力を自己検証)。この過程でコンパイルを落とす欠陥・実行時に落ちる欠陥・テストデータ自体の誤りが複数見つかり修正済み(個々の内容は `CHANGELOG.md` `[Unreleased]`、傾向は `docs/FEATURE-AUDIT.md` §6c の 22 件に集約) |
| 実行検証 | `tools/run-tests.sh` — xunit 無しで実際にテストを実行。**1250 件合格 / 0 件失敗 / 0 件 skip**。`tools/mutation-check.sh` が検出力を実測(意図的な欠陥注入 5 件すべて kill、コメントのみの対照は生存) |

**まだ未検証なのは 4 点だけ**: (1) `dotnet build`/`dotnet test` そのもの — 上記は `csc` 直叩き + 手製ランナーによる**近似**であり、`api.nuget.org` へのアクセスと CI 設置のいずれかが要る。(2) App の WPF 依存 27 ファイル(参照パック未入手)。(3) Platform.Windows(ManagedNativeWifi と Windows API が要る)。(4) MLO のリンク詳細(RSSI は実機測定値)。項目 1〜4 の解消がこれらを埋める。
