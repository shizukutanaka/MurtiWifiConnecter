# Qiita / Zenn 改善レビュー — 2026-06

日本語コミュニティ (Qiita / Zenn) で発信されている .NET 9 / WPF / 非同期 / ログ
の現代ベストプラクティスに照らして、MWC v3.11.0 のコードベースを点検した記録。

## 1. レビュー対象記事 (要旨)

| 出典 | 記事 | 主張 |
|------|------|------|
| Zenn (snak_dev) | [C# のログ、まだ logger.Log〜 で書いてるの？](https://zenn.dev/snak_dev/articles/da22b564722be9) | `[LoggerMessage]` (Source Generator) は文字列補間を **コンパイル時に解決** し、ボクシングを除去。`Log~` 拡張メソッドより常に有利。**CA1848** が推奨アナライザ。 |
| Zenn (inuinu) | [「モダン C#」に入門しよう！2025](https://zenn.dev/inuinu/articles/modern-c-sharp-2025-in-csharp14-and-dotnet10) | CA1848 は近代 C# 開発の前提。コレクション式・`stackalloc` の標準化。 |
| Zenn (nossa) | [ReadOnlySpan で文字列操作を最適化](https://zenn.dev/nossa/articles/dc4180a3b59d04) | 不要なコピーを避け、GC 圧を削減。ホットパスでは可能な限り `Span<T>` / `ReadOnlySpan<T>`。 |
| Zenn (inuinu) | [C# 最適化 / Peanut Butter](https://zenn.dev/inuinu/scraps/8f6e0dddc711f6) | 小バッファは `stackalloc`、大きいものは `ArrayPool<T>.Shared.Rent()`。 |
| Zenn (nuits_jp) | [Wpf.Extensions.Hosting](https://zenn.dev/nuits_jp/articles/2022-01-22-wpf-extensions-hosting) | WPF を Generic Host で起動し、`IHostedService` でバックグラウンド処理を統一する設計パターン。 |
| Qiita (shin21) | [C# 非同期プログラミング完全ガイド 2026](https://qiita.com/shin21/items/97c98ca10940a8d7e0da) | ライブラリ層では `ConfigureAwait(false)` 必須。`async void` は UI イベントハンドラ専用。`CancellationToken` の貫徹。 |
| Qiita (pierusan2010) | [PasswordBox の SecurePassword バインド](https://qiita.com/pierusan2010/items/5d4ceb28ee18cd4e3853) | `PasswordBox.Password` は普通の `string`(GC 上に残る)。`SecurePassword` をビヘイビア経由で ViewModel に渡し、使用直後に `Marshal.ZeroFreeGlobalAllocUnicode` で消す。 |
| Qiita (ken_hamada) | [UI Automation で Windows プログラム自動化](https://qiita.com/ken_hamada/items/501b164374667319d270) | スクリーンリーダ対応の検証は `AutomationProperties.Name` を起点に UIA で確認する。 |
| Qiita (matsumon-dev) | [.NET 汎用ホスト解説](https://qiita.com/matsumon-development/items/d66058f742a464ff0971) | .NET 6 以降、`BackgroundService.ExecuteAsync` で未処理例外が出るとホスト全体が落ちる。`HostOptions.BackgroundServiceExceptionBehavior` で制御。 |
| Qiita (sator_imaging) | [C# の高速化・最適化関連](https://qiita.com/sator_imaging/items/0413c30716c6e5df5cd3) | `FrozenDictionary` / `FrozenSet` は **読み取り専用・起動時構築** 用途で最速。 |

## 2. MWC 現状監査結果

| 項目 | 現状 | 出典の推奨 | 判定 |
|------|------|------------|------|
| **`ConfigureAwait(false)` (Core)** | 16/16 await 準拠、`ConfigureAwait(true)` ゼロ | ライブラリは必ず `(false)` | ✅ 完全準拠 |
| **`async void` 使用** | スキャン結果 0 件 (UI イベント外) | UI ハンドラ専用 | ✅ 完全準拠 |
| **`stackalloc` / `Span<T>`** | ビーコン IE パーサ全て `ReadOnlySpan<byte>` 経由、ヒープ確保 0 | ホットパスで Span | ✅ 完全準拠 |
| **`FrozenDictionary`** | `OuiLookupService` (約2700 OUI) と `RegulatoryDomainService` で採用 | 読み取り専用は Frozen | ✅ 完全準拠 |
| **`[LoggerMessage]` Source Generator** | `MwcLog` に 9 個、ホットパス (Connect / SecurityAdvisory) は対応済 | ホットパスは全て LoggerMessage | ⚠ 部分採用 |
| **ad-hoc `_log.LogXxx`** | Core 14 / App 38 | CA1848 推奨 | ⚠ 大半は cold path (I/O 例外) なので変換コスパ低 |
| **WPF PasswordBox** | `WifiPasswordBoxBehavior` 経由で `SecurePassword` を引き渡し、使用直後ゼロクリア | SecurePassword + Zero | ✅ 完全準拠 |
| **Generic Host (WPF)** | `App.xaml.cs` で `Host.CreateApplicationBuilder` を利用、`IHostedService` 採用 | nuits_jp パターン同等 | ✅ 完全準拠 |
| **BackgroundService 例外** | `AdapterFailoverService` は `try/catch` で握り、ホスト停止しない設計 | .NET 6+ 既定はホスト停止 | ✅ 意図的に Ignore 相当 |

## 3. 結論と適用した変更

CLAUDE.md 違反や明らかなバグはこの観点では見つからなかった。
**実質的な改善余地は CA1848 の可視化のみ。** ただし、

- `TreatWarningsAsErrors=true` のためアナライザ重大度を `warning` に上げるとビルドが
  即時破綻する。
- Core の 14 件は概ね I/O 例外パスで、ホットパスではない。コードを汚す価値は薄い。

そこで **`suggestion` レベル**で導入する。IDE と CI ログには現れるが、ビルドは
通る。将来 LoggerMessage への移行は段階的に進められる。

### 適用 — `.editorconfig`

```ini
# Performance hints (suggestion-only so they don't break TreatWarningsAsErrors)
dotnet_diagnostic.CA1848.severity = suggestion
```

CA2007 (`ConfigureAwait`) のコメントも、なぜ無効化しているのに Core 層が準拠している
かを追記。将来の貢献者が「ルールがないのだから外して良い」と判断しないように。

## 4. 不採用にした提案

| 提案 | 理由 |
|------|------|
| Core 全 14 件を LoggerMessage 化 | I/O 例外パスはホットパスではない。`MwcLog` の Connect/Advisory のような頻発イベントだけが本来の対象。広範な変換は依存度の低い箇所まで属性宣言が増え、可読性が下がる。 |
| `System.Threading.Channels` 採用 | 現状の規模 (アダプター ≤8 × スキャン 5-30 秒間隔) では `SemaphoreSlim` + `Task` で十分。Channels はオーバーキル。 |
| `Wpf.Extensions.Hosting` への移行 | MWC は既に `Host.CreateApplicationBuilder` 直接利用。サードパーティ依存追加の価値なし。 |
| FrozenSet 拡大 (Mesh OUI 12 件など) | 12 件規模では `HashSet` と差が出ない。マイクロ最適化。 |
| Source Generator 全展開 | 既存 Hot-path が LoggerMessage 化済、ROI 低い変更の連鎖を呼ぶ。 |

## 4b. 第2ラウンド (WPF メモリリーク・Dispatcher・破棄パターン)

別の切り口 (弱イベント・Dispatcher デッドロック・IDisposable) で再監査した。

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (dyoneda) | [弱イベントはマルチスレッドで使ってはいけない](https://qiita.com/dyoneda/items/4d188b98a8ee066df162) | `WeakEventManager.AddListener/RemoveListener` は同期コンテキストから呼ぶ必要。別スレッドからだと確実に解除できない。 |
| Qiita (mickie895) | [WPFで極力闇を見せない Dispatcher](https://qiita.com/mickie895/items/4a19f897ffe2b03eab63) | `Task.Result` で UI スレッドがデッドロックしうる。戻り値不要なら `BeginInvoke` を投げて回避。 |
| Qiita (vivinko) | [async/await で UI が固まる理由](https://qiita.com/vivinko/items/9cdff83d6bf3fb5d7c00) | `.Result`/`.Wait()` が混じり帰還先が UI のままだとデッドロック。`ConfigureAwait(false)` で制御。 |
| Zenn (nossa) | [効果的なキャンセルトークンの使用方法](https://zenn.dev/nossa/articles/df258b3ddc351f) | `CancellationTokenSource` は `IDisposable`。監視ループ完了を待ってから破棄する。 |
| Zenn (tomokusaba) | [.NET 9 の新しい LINQ CountBy](https://zenn.dev/tomokusaba/articles/83a3fdf6515435) | キー頻度カウントは `CountBy` で簡潔化。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **ブロッキング `.Result`/`.Wait()`** | App 全体で 1 箇所 (`App.xaml.cs:217` の終了処理)。直後の鎖は全 `ConfigureAwait(false)` 準拠を実コードで検証。デッドロック不可 | ✅ 安全 (コメントの主張が正しい) |
| **静的イベント購読リーク** | `ThemeService` が `SystemEvents.UserPreferenceChanged` を購読。`IDisposable` 実装済 + DI コンテナ生成シングルトンのため `Host.Dispose()` で確実に `-=` | ✅ リーク無し |
| **CancellationTokenSource 破棄順** | `AutoReconnectService.DisposeAsync` は監視ループ完了を待ってから `_cts.Dispose()` | ✅ 教科書通り |
| **`CountBy` 候補** | `ChannelAdvisorService`/`InterferenceAnalyzer` の `.Count(predicate)` は単一条件カウントで、キー別グルーピングではない → `CountBy` 対象外 | ✅ 該当なし |
| **OS スレッドでの同期 `Dispatcher.Invoke`** | `ThemeService.OnUserPreferenceChanged` が SystemEvents 専用スレッドで `Invoke`(同期) | ⚠ **要改善** → 修正 |

### 適用 — `ThemeService.cs`

`OnUserPreferenceChanged` の `Dispatcher.Invoke` → `BeginInvoke`。

```diff
-        Application.Current?.Dispatcher.Invoke(() => Apply(AppTheme.System));
+        Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
```

理由:
- このハンドラは **OS 所有の SystemEvents 専用スレッド**で発火する。同期 `Invoke`
  はそのスレッドを UI 応答まで塞ぎ、プロセス内の他 SystemEvents ハンドラを待たせる。
- アプリ終了処理中 (UI スレッドが `OnExit` 内でブロック) に本イベントが発火すると
  相互待ちでデッドロックしうる (mickie895/vivinko の指摘パターン)。
- テーマ適用は戻り値不要の fire-and-forget。`BeginInvoke` でキューに積めば
  スレッドを即返せる。`MainWindow.xaml.cs:306` の `Invoke` は戻り値を使うため
  同期のまま正しい (対比)。

### 不採用 (第2ラウンド)

| 提案 | 理由 |
|------|------|
| `MainWindow.xaml.cs:84` の `Invoke`→`BeginInvoke` | 更新チェックのバックグラウンドスレッド (OS 共有スレッドではない)。順序意味論を変える危険があり ROI 低。 |
| `WeakEventManager` 全面導入 | MWC は購読箇所が限定的で全て対の `-=`/`Dispose` 済。WeakEvent 機構の複雑性は不要。 |
| `CountBy` リファクタ | 真の頻度カウント (`NetworkHistoryService.GetFrequentSsids`) は既にロック内で辞書集計済。LINQ 化はロック粒度を乱す。 |

## 4c. 第3ラウンド (ソース生成・カルチャ安全・HttpClient 寿命)

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (kurema) | [GeneratedRegex で遊ぶ](https://qiita.com/kurema/items/068385ba2f8bbe3858e1) | `[GeneratedRegex]` (.NET 7+) はコンパイル時に正規表現コードを生成。`RegexOptions.Compiled` の初回 JIT コストが無く Native AOT 対応。SYSLIB1045 が推奨。 |
| Microsoft Learn / Zenn (microsoft) | [System.Text.Json ソース生成](https://zenn.dev/microsoft/articles/system-text-json-on-dotnet6) | `JsonSerializerContext` でリフレクションを排し、起動コスト削減・AOT 対応。 |
| Qiita (sator_imaging) | [String.Equals はもう使う必要ない説](https://qiita.com/sator_imaging/items/5b87f026c162b9188c61) | `ToUpper/ToLower` や比較は明示的に `StringComparison` を指定。カルチャ依存の罠を避ける。 |
| Zenn (arika) | [HttpClient とその設定方法](https://zenn.dev/arika/articles/20250918-httpclient-what-is-it) | `new HttpClient()` の都度生成はソケット枯渇を招く。`static readonly` + `SocketsHttpHandler`(PooledConnectionLifetime) か `IHttpClientFactory`。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **カルチャ非依存の `ToUpper/ToLower`** | bare な `ToUpper()/ToLower()` は 0 件 (全て `*Invariant`) | ✅ 完全準拠 |
| **HttpClient 寿命** | `AppUpdateService` / `HttpConnectivityChecker` は `static readonly HttpClient` + `SocketsHttpHandler`。CLI の都度生成は単発プロセスなので無害 | ✅ 推奨パターン |
| **`[GeneratedRegex]`** | `DiagnosticBundleService` が `RegexOptions.Compiled` を 4 つ (実行時 JIT) | ⚠ **要改善** → 修正 |
| **`StartsWith` カルチャ指定** | `CertificateStoreService.MatchesHostname` の `cn.StartsWith("*.")` のみ `StringComparison` 未指定 (周囲は全て Ordinal) | ⚠ **要改善** → 修正 |
| **System.Text.Json ソース生成** | 10 箇所すべてリフレクションベース。`JsonSerializerContext` 未使用 | ⏸ 保留 (下記) |

### 適用した修正

**(1) `DiagnosticBundleService` — `RegexOptions.Compiled` → `[GeneratedRegex]`**

PII マスク 4 種 (IPv4/MAC/Email/Phone) をソース生成へ。`class` を `partial` 化し
`static readonly Regex X = new(...)` を `[GeneratedRegex(...)] static partial Regex X();`
へ変換、呼び出しを `X.Replace` → `X().Replace` に修正。
- `RegexOptions.Compiled` は初回マッチ時に実行時 JIT する。診断バンドル生成は稀
  (ユーザーが「問題を報告」時のみ) なので、起動時に JIT コストを払うのは本来無駄。
- ソース生成はビルド時確定で起動コストゼロ・AOT/トリミング対応。
- 挙動は同一 (全パターン ASCII・IgnoreCase 不使用)。`DiagnosticBundleServiceTests`
  の 7 テストはすべて挙動ベースで内部フィールド非依存 → 変更後も通る。

**(2) `CertificateStoreService.MatchesHostname` — `StartsWith` に `Ordinal` 明示**

```diff
-        if (cn.StartsWith("*."))
+        if (cn.StartsWith("*.", StringComparison.Ordinal))
```

証明書ホスト名照合 (RFC 6125, セキュリティ経路) はカルチャ非依存であるべき。
同メソッド内の他の比較は全て `StringComparison.Ordinal*` を使っており、この 1 箇所
だけ既定カルチャに依存していた (CA1310 該当)。`"*."` は句読点のみで実害は無いが、
セキュリティコードでは明示的 Ordinal が正しい。

### 保留 — System.Text.Json ソース生成

10 箇所すべてリフレクションベースで、`JsonSerializerContext` 化は起動高速化と AOT
対応に有効。**ただし永続化データ (settings.json / history / adapters.json) の
シリアライズ経路を触るため、ラウンドトリップ崩れは既存ユーザーのデータ破損に直結する。**
本環境は .NET SDK 不在 (Linux・テスト Windows 専用) でビルド/テスト検証ができないため、
無検証での広域変更は CLAUDE.md の「テスト先行・最小差分」に反する。Windows CI が
通る環境で、ゴールデンテスト (既存 JSON ファイルのラウンドトリップ) を先に追加してから
着手するべき。→ §5 候補へ。

### 不採用 (第3ラウンド)

| 提案 | 理由 |
|------|------|
| CLI `QualityHistoryCommand` の `new HttpClient` を static 化 | 単発 CLI プロセスは実行後すぐ終了。ソケット枯渇は長時間稼働プロセス固有の問題で、CLI には該当しない。`using` で確実に破棄される現状が適切。 |
| `CertificateStoreService` を `OrdinalIgnoreCase` に統一 | `"*."` は大小文字を持たない句読点。`Ordinal` で十分かつ最小。 |

## 4d. 第4ラウンド (record 値等価・ガード節・破棄例外)

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (muniel) | [配列入りのレコードってどうだろう](https://qiita.com/muniel/items/fd843abc55a5626e5c45) | record の自動生成 `Equals` はコレクションプロパティ (`List`/配列) を **参照等価**で比較する。内容が同じでもインスタンスが違えば不等。`Distinct`/`HashSet`/辞書キーで使うと罠。 |
| Qiita (laughter) | [もはや new ArgumentNullException する必要はない](https://qiita.com/laughter/items/55db2b97390121373795) | `?? throw new ArgumentNullException(nameof(x))` → `ArgumentNullException.ThrowIfNull(x)`。`CallerArgumentExpression` で nameof 不要。 |
| Zenn (shimiyu) | [ObjectDisposedException を理解する](https://zenn.dev/shimiyu/articles/6e2accebf2af49) | .NET 8+ の `ObjectDisposedException.ThrowIf(disposed, this)` で破棄済みチェックを 1 行化。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **record × コレクションの値等価の罠** | `Distinct`/`GroupBy` は全て **スカラー射影**後 (`n.Auth`/`l.Band`/string)。コレクション持ち record (`WifiNetwork` 等) を丸ごと等価比較する箇所は皆無 | ✅ 罠を踏んでいない |
| **`HashSet<record>` / `Dictionary<record,>`** | 該当なし (キーは Guid/string/enum のみ) | ✅ 安全 |
| **ガード節の近代化** | Core は既に `ArgumentNullException.ThrowIfNull` を 10 箇所で採用。旧式 `?? throw` は `AdapterViewModel` の 2 箇所のみ | ⚠ **一貫性のため修正** |
| **`ObjectDisposedException.ThrowIf`** | 手動 `if (_disposed) throw` / `throw new ObjectDisposedException` は **0 件**。`_disposed` フラグは Dispose の冪等化に `return` で使用 (正しい) | ✅ 修正不要 |

### 適用した修正

**`AdapterViewModel` コンストラクタ — 旧式 `?? throw` → `ThrowIfNull`**

```diff
+        ArgumentNullException.ThrowIfNull(prefs);
+        ArgumentNullException.ThrowIfNull(executor);
         ...
-        PrefsService = prefs ?? throw new ArgumentNullException(nameof(prefs));
-        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
+        PrefsService = prefs;
+        _executor = executor;
```

コードベースの支配的イディオム (Core 全域) に揃え、ガードを代入前に集約して
fail-fast 化。検証対象 (2 引数)・例外型・パラメータ名は不変。これで旧式 0 / 近代 12。

> 補足: 既存コンストラクタは 7 引数中 2 つ (`prefs`/`executor`) のみガードする
> 非対称設計だが、残り 5 引数への拡張は「これまで通っていた null がここで例外化する」
> 挙動変更を伴いビルド/テスト検証が必要なため、本ラウンドでは**意図的に踏み込まない**。

### この回の主眼

本ラウンドは **大半が「既に近代パターンを採用済み」の確認**だった。record 値等価の罠は
回避済、`ObjectDisposedException` の手動 throw は皆無、ガード節も Core では近代化済。
唯一の不一致 (`AdapterViewModel` の旧式 throw 2 件) を解消し、コードベース全体で
ガード節イディオムを統一した。

## 5. 次の自然な深掘り候補 (将来セッション用)

0. **System.Text.Json ソース生成への移行** (§4c 保留分)
   永続化 JSON のラウンドトリップ・ゴールデンテストを先に追加し、Windows CI で検証
   できる状態を整えてから `JsonSerializerContext` を導入する。起動高速化＋AOT 対応。

1. **UI Automation テスト** (Qiita: ken_hamada / Friendly フレームワーク)
   AutomationProperties.Name の "宣言済み" と "実際に Narrator/NVDA で読まれる" の
   ギャップは UIA からのみ検証可能。WPF 統合テストとして導入価値がある。
2. **WinUI 3 / Avalonia への可搬性検討** (Zenn: shinta0806)
   MWC のモデル層は完全プラットフォーム非依存。将来 macOS / Linux GUI を Avalonia で
   提供する場合の障壁を整理しておく。
3. **Bufferbloat 計測の RPM 標準化** (IETF responsiveness)
   `NetworkQualityService.MeasureResponsivenessAsync` は実装済だが、計測結果を Apple
   "Network Quality" と互換のフォーマットで出力すれば外部ツールに連携できる。
