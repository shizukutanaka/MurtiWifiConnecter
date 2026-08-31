# MWC AI セッション作業指示書(Opus / Sonnet 向け)

> **読者**: このリポジトリで作業する将来の Claude セッション(モデル問わず)。
> **読む順序**: `CLAUDE.md`(プロジェクト憲法)→ 本書(作業方法・環境の罠)→
> `docs/FEATURE-AUDIT.md`(機能の過不足の詳細と検証コマンド)。
> **権限が無くて着手できない項目**は `docs/COMPLETION-CHECKLIST.md` に所有者向けの手順として
> 分離してある。そこに載っている 3 件を AI セッションが再挑戦しても同じ壁に当たる。
> 本書は 2026-07 の長期監査・改善セッションで実際に踏んだ罠と確立した手法の記録である。
> 推測ではなく、全項目が実体験に基づく。

## §1 プロダクトの長所(壊さないこと)

| 長所 | 具体 |
|---|---|
| Core 層の設計品質 | XXE/DTD 対策済み `CatImportService`、IANA 公式番号の `EapType`(25/13/23/21 — 変更禁止)、WCAG 1.4.1 設計の `SignalIconService`、`Result<T,E>` パターン(`ConnectionResult`)、学術引用付き `SecurityAdvisoryService` |
| i18n 体制 | 名前付き 14 ロケール + 中立ベースの計 15 resx ファイル × 516 キー(README バッジの「14 langs」はこの名前付きロケール数で正しい)。`LocaleKeyConsistencyTests` がキー欠落を自動検出。UI 文字列は必ず resx 経由(CLAUDE.md 必須) |
| テーマ契約 | 16 ブラシ契約。`ThemeContractTests` は views の新規 `DynamicResource ...Brush` 参照を走査して全テーマ辞書への定義を強制。`ThemeAccessibilityAuditTests` が WCAG コントラスト比を実測検証 |
| 監査文化 | 「実装されている ≠ 機能している」を軸にした FEATURE-AUDIT 方法論。呼び出し元ゼロの Core サービスを grep で検出し、配線 or 削除判断を文書化する |
| Enterprise CLI(2026-07 完成) | `mwc connect` が 802.1X 完全対応: `--eap-type`/`--username`/`--domain`/`--server-name`/`--trusted-root-ca` + `MWC_PASSWORD` 環境変数。GUI 拡張の**参照実装**として `Program.cs` の `BuildConnect` を読むこと |

## §2 短所と改善バックログ(優先順・着手条件付き)

各項目に「なぜ未着手か」と「着手条件」を明記する。条件を満たさないまま着手しないこと。

| # | 項目 | 状態と着手条件 |
|---|---|---|
| 1 | **CI 不在** — `.github/workflows/` が存在せず CI/CodeQL は一度も実走していない | ワークフロー YAML は `docs/ci/` に**一本化済み**(旧 `ci/github-workflows/` は削除)。**ブロックの真因は GitHub 側**: push 時に `refusing to allow a GitHub App to create or update workflow ... without workflows permission` で拒否される(2026-07 に実測)。ローカルは `.claude/settings.json` が `Write(.github/**)` を許可しており作成もコミットも通るため、**試すと push 不能なコミットが残る** → `git reset --hard HEAD~1` で戻すこと。必要なのは GitHub App への `workflows` 権限付与か所有者による push。詳細は FEATURE-AUDIT §0 |
| 2 | **GUI Enterprise 入力**(`ConnectDialog` 拡張) | **Windows + dotnet でのコンパイル検証が必須**(WPF の `x:Name` 生成・イベントシグネチャ・リソース参照は python では検証不能)。CLI 側は完成済み。消費側は `MainWindowCommands.cs` と `AllAdaptersOverviewView.xaml.cs` の2箇所(`new ConnectDialog(ssid, auth)` → `dlg.Passphrase`) |
| 3 | **CatImportService 配線**(eduroam インポート) | 上記2の完了後に「小差分」化する。単体では PEAP の username/password 必須検証で登録失敗する半端な機能になるため見送り中 |
| 4 | **MLO 実測データ源** — `WifiNetwork.MloLinks` を供給する層が無く MLO 表示が死んでいる | 解決策は `ManagedNativeWifi.GetRealtimeConnectionQuality`(Win11 24H2+、v3.0.1)。API 形状は `docs/arxiv-improvement-analysis.md` 2026-H2 追補に記録済み。**`PhyType` の名前衝突に注意**(`ManagedNativeWifi.PhyType` vs `MWC.Core.Models.PhyType`)。要 dotnet/Windows 検証 |
| 5 | **PrivacyAdvisoryService** | `Analyze(MacAddressMode, ...)` が要求する MAC ランダム化状態を検出するプラットフォーム実装が前提(現状 `grep MacAddressMode src/` は定義・テストのみ) |
| 6 | **SecureString 方針** | CLAUDE.md は必須と規定するが実装は全域 plain string。**ユーザー裁定待ち。勝手に解決しないこと**(過去に無断緩和を試み差し戻された)。FEATURE-AUDIT §2b |
| 7 | **GitHub Release** | タグ push は組織 egress ポリシーで **HTTP 403**。プロキシ README(`/root/.ccr/README.md`)が「リトライ・回避せず報告」と明記。**リトライ禁止**。所有者の手動操作か権限付与が必要 |
| 8 | **未検証の数値**(README の `tests-NNNN` バッジ等) | 実 `dotnet test` 実行後にのみ更新する。grep 概算で別の未検証値に差し替えない |

## §3 環境の罠と作業ルール(本セッションで実際に踏んだもの)

- **dotnet SDK なし** → 検証は python(XML 整形性・resx キー一致・波括弧対応)+ CI 委任。§5 のチートシート参照
- **dotnet を入れてビルドする試みは 2026-07 に実施済み — 到達不能と確定した。** 繰り返さないこと:
  - SDK 自体は入る: `apt-get update && apt-get install -y dotnet-sdk-10.0` は成功する
    (公式の `dot.net/v1/dotnet-install.sh` はプロキシが 403。apt には 8.0 と 10.0 のみで 9.0 は無い)。
  - しかし **`api.nuget.org` が組織の egress ポリシーで明示的に拒否**される
    (`curl -sS "$HTTPS_PROXY/__agentproxy/status"` の `recentRelayFailures` に
     `gateway answered 403 to CONNECT` として記録される)。パッケージ復元ができないため
    ビルドもテストも不可能。プロキシ README が「リトライ・回避せず報告」と定めているので迂回しない。
  - なお `global.json` は SDK 9.0.100 / `rollForward: latestFeature` を要求するため
    SDK 10 では解決されない。**検証のために global.json を書き換えたら必ず元に戻すこと**
    (restore が生成する `packages.lock.json` も不完全なので消す)。
  - **副産物として判明した CI 設計上の事実**: `tests/MWC.Core.Tests` は `net9.0-windows` を
    ターゲットとし `MWC.App`(WPF)を参照している。つまり**テストは Linux では動かない**。
    `docs/ci/ci.yml` が Windows ジョブでのみテストを走らせ、Ubuntu ジョブを Core ビルドに
    限定しているのは正しい設計。Linux でテストを動かそうとしないこと。
- **resx 編集の落とし穴**: `git add src/MWC.App/Resources/Strings.*.resx` の glob は**ベースの `Strings.resx`(ロケール接尾辞なし)にマッチしない**(shell glob の `*` は空文字に不一致)。実際に取りこぼして Stop hook に指摘された。→ **`git add -u` を使う**
- **孤立検出 grep の盲点**: クラス名 grep は**拡張メソッド呼び出しを見逃す**。`SafeFireAndForget` を `.Forget()` 構文で使われているのに孤立と誤判定しかけた。→ 孤立候補には `grep -c "(this " <file>` で拡張メソッド定義を確認
- **自動拒否される操作**: `git push --force*` / レビューなしの master マージ / タグ push(組織 egress ポリシーで 403)。→ designated branch へ**通常 push**、PR 作成はユーザー要求時のみ、force-push しない
- **`.github/workflows/` は「書けるが push できない」**(2026-07 実測)。ローカルの settings.json は許可しているのでファイル作成もコミットも成功するが、GitHub が push を拒否する(App トークンに `workflows` スコープが無い)。**やってしまうとそのブランチへの全 push が失敗する**ので `git reset --hard HEAD~1` で戻す。§2 の 1 番参照
- **WPF はテスト不能**: `Window` 派生クラスはテストプロジェクトからインスタンス化できない。ロジックは ViewModel(`ObservableObject`)層に置き、そこをテストする(例: `NetworkDetailViewModelVpnEapWiringTests`)
- **変更禁止リスト**(FEATURE-AUDIT §3 要約): DPAPI エントロピー `"WiFix-v1"`(既存ユーザーの暗号データが復号不能になる)、`EapType` 数値、Solarized の AA 止まり(著名パレット尊重)、`SetAutoReconnect` の no-op(設計意図)、CLI の `Console.WriteLine`(CLAUDE.md が CLI のみ許可)
- **コミット規律**: `CHANGELOG.md` `[Unreleased]` に追記、Phase ごとにコミット、コミットフッター付与、designated branch へプッシュ

### 「プラットフォーム層が要る」を鵜呑みにしない — 分解できるかを必ず問う

2026-07 に `Hotspot20Service` のブロッカー(802.11u Interworking IE 抽出)を調べたところ、
**大部分は Core に切り出せた**。リポジトリには既に完全な IE 解析基盤
(`BeaconIeParser` → `BeaconIeApplier` → `IBeaconIeProvider`)があり、
欠けていたのは要素 1 つだけだった。解析は Core でテスト可能、
実機が要るのは生バイトの供給だけ、という形に分割できた。

判断の分かれ目は「**その値はビーコンで広告されるか、それとも実測値か**」:

| 項目 | 分解可否 | 理由 |
|---|---|---|
| 802.11u Interworking | **✅ 分解できた** | ビーコンで広告される静的な能力情報。有無の判定は純粋なバイト解析 |
| MLO (`MloLinks`) | ❌ 分解不可 | `MloLink` が **RSSI(リンクごとの実測受信強度)** を要求する。ビーコン IE には無く、接続中のランタイム API(`ManagedNativeWifi.GetRealtimeConnectionQuality`)からしか得られない |
| MAC ランダム化検出 | ❌ 分解不可 | OS の設定値そのものの読み取り。解析すべきバイト列が存在しない |

新しい「要プラットフォーム」項目に当たったら、まずこの問いを立てること。
広告される静的情報なら Core に解析を書けて、実機セッションの作業量と危険を大きく減らせる。

## §4 モデル別の注意(Opus / Sonnet)

- **共通の最優先原則**: 「**検証していない主張をしない**」。数値・完了報告・「テストが通る」等は、実際に確認した手段の範囲でのみ述べる。§3 のルールはモデルに依らず適用
- **大規模変更**(10 ファイル超・アーキテクチャ変更・全認証方式に触れる変更)は着手前に計画提示 → ユーザー承認
- **調査の徹底**: 薄い grep 1回で結論を確定させず、§3 の反証チェック(拡張メソッド・resx glob 等)を通す。孤立サービスは「呼び出し元ゼロ」を確定する前に SDK 再パッケージ(`sdk/MWC.SDK.csproj` が Core 全体を出荷)の有無も見る
- **安全に進められる増分の選び方**: この環境では「Core 層のロジック + CLI + テスト(python/golden で検証可能)」が最も安全。「WPF + 実機 API + プラットフォーム層」は検証不能なので、実機セッションに残す

## §5 検証チートシート(dotnet 不在時)

> **まず `bash tools/verify.sh` を実行すること。** 下記のチェックを 1 コマンドにまとめてある
> (XML 整形性・ロケールキー一致・`MWC.sln` 整合性・波括弧・補完スクリプト構文・孤立サービス検出)。
> 変更前後で走らせれば、壊したかどうかの下限は判定できる。
> 波括弧チェックだけは**警告扱い**である — 正規表現で C# を字句解析することは原理的にできず、
> 補間文字列の入れ子で必ず誤検知するため(理由はスクリプト内に記載)。
> これは CI の代替ではない。dotnet がある環境では `dotnet build` / `dotnet test` を必ず併用すること。

個別に実行したい場合:

```bash
# XML 整形性(全 resx / 全 XAML)
python3 -c "import xml.etree.ElementTree as ET,glob; [ET.parse(p) for p in glob.glob('src/MWC.App/**/*.xaml',recursive=True)+glob.glob('src/MWC.App/Resources/*.resx')]; print('XML OK')"

# 15 ロケールのキー完全一致
python3 -c "import xml.etree.ElementTree as ET,glob; base=set(e.get('name') for e in ET.parse('src/MWC.App/Resources/Strings.resx').findall('.//data')); [print(p,'MISSING',base-set(e.get('name') for e in ET.parse(p).findall('.//data'))) for p in glob.glob('src/MWC.App/Resources/Strings.*.resx')]; print('locale check done')"

# C# / bash の波括弧対応・構文
python3 -c "c=open('FILE').read(); print('OK' if c.count('{')==c.count('}') else 'MISMATCH')"
bash -n completions/mwc.bash

# 孤立サービス全数検出(SDK 再パッケージ・拡張メソッドは別途確認)
for f in src/MWC.Core/Services/*.cs; do n=$(basename "$f" .cs); [ "$(grep -rl "\b$n\b" src/ | grep -v "/$n.cs" | wc -l)" -eq 0 ] && echo "orphan?: $n"; done
```

CI が有効化された後に走る自動テスト(手動再確認は不要になる): `LocaleKeyConsistencyTests`
(resx キー一致)、`ThemeAccessibilityAuditTests`(WCAG コントラスト)、`ThemeContractTests`
(テーマ辞書 16 ブラシ契約)、`ProfileXmlBuilderTests`(全認証方式のゴールデン)。

---

*作成: 2026-07 の監査・改善セッション。機能詳細と検証コマンドは `docs/FEATURE-AUDIT.md`、
改善の時系列は `CHANGELOG.md` と git log を参照。*
