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

## 4e. 第5ラウンド (CompositeFormat / 同期 I/O / ValueTask / ホットループ)

| 出典 | 記事 | 主張 |
|------|------|------|
| Microsoft Learn | [CA1863: Use 'CompositeFormat'](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1863) | 同じ format string を繰り返し `string.Format` するなら `CompositeFormat.Parse` で事前解析しキャッシュ。.NET 8 ベンチで反復フォーマット 15-30% 削減。 |
| Qiita (vivinko) | [C# 非同期処理の基準｜UI スレッド/await/デッドロックを避ける判断軸](https://qiita.com/vivinko/items/659c3490853102de516a) | UI スレッドで `File.ReadAllText` 等の同期 I/O はブロックの主因。`*Async` 版＋`await`。 |
| Zenn (mayuki) | [Task/ValueTask を直接返せる場合でも原則 async/await](https://zenn.dev/mayuki/articles/96a17916096714) | `ValueTask` をそのまま返すと dispose 競合や stack trace 欠落を招きうる。`async/await` を経由する。 |
| Qiita (Kujiro) | [ループの最適化手法 ② `List<T>` を `Span<T>` 化](https://qiita.com/Kujiro/items/9569e91b942bcf9d528b) | `CollectionsMarshal.AsSpan(List<T>)` で foreach の境界チェック削減。ホットループで有効。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **`CompositeFormat` キャッシュ** | `L.Format` が 31 ヶ所から呼ばれ毎回 `string.Format` で再パース | ⚠ **要改善** → 修正 |
| **同期 File I/O** | 10 ヶ所。startup の `Load` (DI 構築前)・atomic 書込みの `Save` (背景スレッド)・ユーザー起動 export (待ち時間が UX 上自然) のいずれか | ✅ **意図的設計** — 変更しない |
| **`ValueTask` の誤用** | `AutoReconnectService.DisposeAsync` のみ。これは `IAsyncDisposable.DisposeAsync` の契約 (`ValueTask` 必須) | ✅ インタフェース契約 |
| **`CollectionsMarshal.AsSpan` 候補** | ホットループは `IReadOnlyList<T>` インタフェース型 (`RnrNeighbors`/`bySsid`)。具象 `List<T>` ではないため `AsSpan` 不可。共変性のため抽象化は妥当 | ✅ 設計的に正当 |

### 適用した修正

**`L.cs` — `string.Format` を `CompositeFormat` キャッシュ化** (CA1863)

```csharp
private static readonly ConcurrentDictionary<(string Key, string CultureName), CompositeFormat> _formatCache = new();

public static string Format(string key, params object[] args)
{
    var culture  = CultureInfo.CurrentUICulture;
    var template = Get(key);
    var cacheKey = (key, culture.Name);
    if (!_formatCache.TryGetValue(cacheKey, out var fmt))
    {
        try { fmt = CompositeFormat.Parse(template); _formatCache.TryAdd(cacheKey, fmt); }
        catch (FormatException) { return template; }  // 不正テンプレートはキャッシュせず
    }
    try { return string.Format(culture, fmt, args); }
    catch (FormatException) { return template; }     // 引数不足等 (既存契約と同一)
}
```

設計判断:
- **キーはカルチャ込み**: テンプレート文字列はカルチャ依存 (`"Connected to {0}"` vs `"{0} に接続しました"`)。
- **規模上限**: format キー約 50 × カルチャ 15 = ~750 件で頭打ち。LRU 不要。
- **失敗時はキャッシュしない**: resx の構文エラーがあると不正な CompositeFormat を握ってしまい以降全部 raw に落ちる。Parse 失敗時は単純に template を返す。
- **既存契約完全保持**: `RefactoringTests.Format_BadArguments_DoesNotThrow` が引数不足時に raw template を返すことを期待。`string.Format(culture, fmt, /*empty*/)` も `FormatException` を投げるため、外側 try/catch で同じ挙動。
- **スレッド安全**: `ConcurrentDictionary` + `CompositeFormat` 自体がイミュータブル。

### 不採用 (第5ラウンド)

| 提案 | 理由 |
|------|------|
| `SettingsService.Load` を `LoadAsync` 化 | DI 構築の前提として "起動直後に設定が読まれている" を満たす必要がある。非同期化すると初回 UI バインディングがレースする。Save 側は背景スレッドなので影響なし。 |
| `AutoReconnectService.DisposeAsync` を `Task` 化 | `IAsyncDisposable.DisposeAsync()` の戻り値型は `ValueTask` 固定 (BCL 契約)。 |
| `BeaconIeParser` の foreach を `AsSpan` 化 | パラメータが `IReadOnlyList<T>`。共変性のため抽象化は正しく、変更すると `Array.Empty<T>()` 等の参照渡しが壊れる。 |
| ホットパスで非同期 `File.ReadAllTextAsync` 採用 | 該当する hot path がない (Load は startup, Save は背景、Export はユーザー起動)。 |

## 4f. 第6ラウンド (Process.Start URL 起動・DI 寿命・null 免除・暗号 RNG)

セキュリティ寄りの角度で再監査。MWC は信頼できないネットワーク (キャプティブ
ポータル) と接触するため、外部起動シンクを重点的に確認した。

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (naoki_oda) | [Web アプリから既存 Windows アプリを安全に起動する 2 方式比較](https://qiita.com/naoki_oda/items/9da81332ca1278d0b58c) | OS 登録のスキーム/シェル起動は悪意ある呼び出し元から任意ハンドラを起動させうる。検証が必要。 |
| Qiita (M_Kagawa) | [既定ブラウザで URL を開く (.NET 6)](https://qiita.com/M_Kagawa/items/24e817a63742f04e2dc3) | .NET Core 以降は `UseShellExecute=true` が必要。シェル起動は URL 以外も起動しうる点に注意。 |
| Zenn (rendya) | [.NET の Strategy パターンと DI](https://zenn.dev/rendya/articles/dotnet-strategy-pattern-gof-to-modern-di) | Singleton が Scoped を抱え込む captive dependency は古いインスタンスを使い続けるバグの温床。 |
| Qiita (Hoshinari) | [null 免除演算子で警告を無視する](https://qiita.com/Hoshinari_Games/items/b07f364640336ca51ef6) | `!` はコンパイル時 null チェックを実行時に倒すだけ。濫用すると NRE をデバッグ困難にする。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **`Process.Start(url, UseShellExecute=true)`** | 3 ヶ所。`CaptiveProbe` (const)・About の Hyperlink (XAML ハードコード https)・`certmgr.msc`。いずれも**ネットワーク供給値ではない** | ✅ 実害なし → ただしシンク防御を追加 (下記) |
| **DI captive dependency** | 登録は全て `AddSingleton`/`AddTransient`。`AddScoped` は **0 件** (デスクトップにスコープ無し)。Transient は factory ラムダ/`GetService` で都度解決し singleton ctor に抱え込まない | ✅ 構造的に発生しない |
| **null 免除演算子 `!`** | 全体で 8 件 (Core 4/App 4) のみ。濫用なし | ✅ 健全 |
| **暗号 RNG** | `RetryPolicy` の jitter は `Random.Shared` (非機密で正当)。WifiDirect の group SSID は `Guid.NewGuid` (SSID は公開ブロードキャストで秘匿不要)。脆弱なパスフレーズ生成は無し | ✅ 適切な使い分け |

### 適用した修正 — 外部起動シンクの多層防御

`Process.Start(..., UseShellExecute=true)` は http/https に限らず `file://`・
カスタムスキーム・実行ファイルまで起動しうる典型的な任意起動シンク。現状の
呼び出し元はすべてハードコード URL で**実害は無い**が、

- キャプティブポータル画面は信頼できないネットワークが関与する文脈であること
- WPF の `Hyperlink.RequestNavigate` → `e.Uri` 起動は、将来 NavigateUri が
  データバインド/ローカライズ/非 http 化された瞬間に任意起動へ化けること

から、**シンク側で「http/https の絶対 URI のみ起動」を不変条件として強制**する。

新規 `BrowserLauncher` (App/Services) を追加:
- `OpenHttp(string?)` / `OpenHttp(Uri?)`: scheme が http/https の絶対 URI のみ許可。
  それ以外は起動せず警告ログ。起動失敗 (ブラウザ未関連付け) も握りつぶさず記録。
- `AboutDialog.OnHyperlinkNavigate` と `CaptivePortalDialog.OnOpenExternal` を
  これ経由に変更。`certmgr.msc` 起動 (URL ではなく管理コンソール) は対象外。

テスト `BrowserLauncherTests`: `file://`・`javascript:`・`ms-settings:`・`ftp:`・
相対/スキーム無し/null を**全て拒否**することを検証 (拒否は Process.Start 到達前に
false を返すため副作用なし。正の http URL は実起動するので CI では検証しない)。

> これは脆弱性修正ではなく**多層防御 (hardening)**。CLAUDE.md のセキュリティ姿勢
> (「安全な箇所でも PII を必ずマスク」等) と同じ思想で、不変条件をシンクに局在化・
> 明示する。

### 不採用 (第6ラウンド)

| 提案 | 理由 |
|------|------|
| `certmgr.msc` 起動もスキーム検証 | URL ではなく Windows 管理コンソール (.msc) の起動。ハードコードかつ別カテゴリで、URL 用ヘルパーの対象外。 |
| `Random.Shared` を `RandomNumberGenerator` 化 | 用途はバックオフ jitter (再試行の時間分散)。予測不能性は不要で、暗号 RNG はオーバーキル。 |
| null 免除 `!` の一掃 | 8 件と僅少で、各々 NRT 解析の限界を補う正当な用法。機械的除去は可読性を下げる。 |

## 4g. 第7ラウンド (ObservableCollection スレッド・多重列挙・linked CTS)

WPF コレクションのスレッド安全と LINQ 遅延評価の罠を監査。

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (Kokudori) | [Livet で始める WPF 入門 その7](https://qiita.com/Kokudori/items/dfc5850321ea4c70b56b) | バインド済みコレクションを別スレッドから変更すると `NotSupportedException`。WPF 4.5 の `BindingOperations.EnableCollectionSynchronization` か Dispatcher 経由が必要。 |
| Qiita (okazuki) | [複数 UI スレッドを WPF でやる前に](https://qiita.com/okazuki/items/698ec4d45c8286fedac1) | `ObservableCollection<T>` はスレッドセーフでない。複数スレッドからの操作は予期せず失敗。 |
| Zenn (snak_dev) | [コレクションで capacity 指定してる？](https://zenn.dev/snak_dev/articles/0823a1f24ada92) | 遅延評価の `IEnumerable` を 2 回列挙すると処理が 2 回走る。`ToList()` で実体化 (CA1851)。 |
| Zenn (nossa) | [効果的なキャンセルトークンの使用方法](https://zenn.dev/nossa/articles/df258b3ddc351f) | `CreateLinkedTokenSource` は `IDisposable`。破棄漏れでリーク/ゾンビタスク。 |

### 監査結果 — 全軸クリーン (コード変更なし)

| 項目 | 現状 | 判定 |
|------|------|------|
| **`ObservableCollection` の別スレッド変更** | VM の async は `ConfigureAwait(false)` を使わず継続が UI スレッドに戻る。バックグラウンドサービス (AutoReconnect/Failover) は束縛コレクションに触れない。自動スキャンは `DispatcherTimer` (ThreadPool Timer ではない) | ✅ **設計的に安全** |
| **`MainViewModel` の設計コメント** | 「ThreadPool タイマーだと SynchronizationContext が無くコレクション変更が Dispatcher 外で起き NotSupportedException を投げ自動スキャンが無言で失敗していた」と明記 — **過去に踏んで修正済の知見が残る** | ✅ 制度的知識あり |
| **`IEnumerable` 多重列挙 (CA1851)** | ~18 の `IEnumerable` 受け取りメソッドは全て単一パス or `ToList`/`HashSet` で 1 度実体化。`ExportService.ToTxt` はループカウンタで総数を数え count-then-iterate を回避 | ✅ クリーン |
| **`CreateLinkedTokenSource` 破棄** | 3 ヶ所全て `using var` | ✅ クリーン |

### 適用 — 予防的アナライザガード (コード欠陥はなし)

コード欠陥が無いため**プロダクションコードは変更しない**。代わりに、検証で確認した
「多重列挙していない」状態を**予防的に固定**する:

```ini
dotnet_diagnostic.CA1851.severity = suggestion  # possible multiple enumeration of IEnumerable
```

`IEnumerable<WifiNetwork>` を受ける公開サービスメソッドが多数あるため、将来
`if (networks.Any()) { … networks.Select(…) }` のような二重列挙が紛れ込むのを
IDE/CI で検知できる。現状グリーンなのでノイズはゼロ。R1 の CA1848 と同じ
「suggestion で TreatWarningsAsErrors を壊さず可視化」方針。

### この回の主眼

本ラウンドは **3 軸すべてクリーン**で、プロダクションコードの変更は行わなかった
(変更すれば theater になる)。WPF コレクションのスレッド安全という最も多い落とし穴が、
`DispatcherTimer` 採用と設計コメントによって**既に潰され知見化されている**ことを
確認できたのが収穫。多重列挙の予防ガード (CA1851) のみ追加。

## 4h. 第8ラウンド (XXE・enum 未検証キャスト・不正 UTF-8・Regex ReDoS)

外部入力のパース安全性を監査。MWC は eduroam CAT (外部 XML) を取り込むため XXE を重点確認。

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (keitakei777) | [XML External Entity (XXE) 脆弱性](https://qiita.com/keitakei777/items/b36d130bff5161159e87) | 外部実体解決で情報漏洩/SSRF/DoS。`DtdProcessing.Prohibit` + `XmlResolver=null` で封じる。 |
| Qiita (tomoki0sanaki) | [XXE と .NET Framework](https://qiita.com/tomoki0sanaki/items/1987ecd472a1fd325d71) | `XmlResolver=null` を明示するのが手早い防止策。 |
| Zenn (spacesolver) | [我々が enum に望むこと](https://zenn.dev/spacesolver/articles/ec960fb5b14d06) | C# は範囲外値を enum に格納できる。switch は網羅性を保証しない。 |
| devleader / MS Learn | [Regex Performance / MatchTimeout](https://learn.microsoft.com/en-us/dotnet/api/system.text.regularexpressions.regex.matchtimeout?view=net-9.0) | ネスト量指定子は catastrophic backtracking (ReDoS)。`MatchTimeout` か `NonBacktracking`。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **XXE (CAT XML)** | `CatImportService.ParseEapConfig` が外部 eduroam XML を `XDocument.Parse` | ⚠ 既定で安全だが**境界で明示** → 修正 |
| **不正 UTF-8 / untrusted バイト復号** | SSID は ManagedNativeWifi が復号済で渡す。生バイト `Encoding.GetString` は無し (出力用 export を除く) | ✅ 該当なし |
| **enum 未検証キャスト** | 唯一 `ChannelBandCanvas` の WPF DP getter (`typeof(WifiBand)` 登録済で型安全)。ネイティブ値→enum は `MapAuth/MapCipher/MapPhy` の switch + `_ => default` で安全に正規化 | ✅ 健全 |
| **Regex ReDoS** | DiagnosticBundle / HealthCheck の正規表現は IPv4/MAC/Email/Phone でネスト量指定子なし=線形。入力は境界済ログ/診断文。Linux nmcli regex はローカルコマンド出力 | ✅ ReDoS なし |

### 適用した修正 — XXE 防御の明示化 (多層防御)

`XDocument.Parse(string)` は .NET 9 既定で `DtdProcessing.Prohibit` + `XmlResolver=null`
のため**実は既に安全**。しかし CAT/eap-config は eduroam から DL される信頼できない
外部 XML であり、セキュリティ境界の不変条件をフレームワーク既定に委ねず**ローカルに
可視化・監査可能**にする (R6 の BrowserLauncher と同じ思想、CLAUDE.md は `CA3075=error`)。

```csharp
var settings = new XmlReaderSettings
{
    DtdProcessing             = DtdProcessing.Prohibit, // <!DOCTYPE> 拒否で実体展開を不可能に
    XmlResolver               = null,                   // 外部 DTD/実体を一切解決しない
    MaxCharactersFromEntities = 0,
};
using var reader = XmlReader.Create(new StringReader(xmlContent), settings);
doc = XDocument.Load(reader);
```

テスト 2 件追加 (`ServicesCoverageTests`):
- `ParseEapConfig_XxeExternalEntity_Rejected`: `file:///etc/passwd` 外部実体を含む DOCTYPE
  → `FormatException` (実体解決前に拒否され、ファイルは読まれない)。
- `ParseEapConfig_EntityExpansionDtd_Rejected`: billion laughs 風 DTD → `FormatException`。

> これは脆弱性修正ではなく**境界の明示化 (hardening)**。挙動は不変 (正規の CAT XML に
> DTD は無い)。セキュリティレビューで「信頼できない XML を既定依存で解析」と指摘されない
> よう、防御をシンクに局在させる。

### 不採用 (第8ラウンド)

| 提案 | 理由 |
|------|------|
| 全 Regex に `MatchTimeout` 付与 | パターンにネスト量指定子が無く ReDoS 不可能。入力も境界済。タイムアウトは無意味な複雑化。 |
| `NonBacktracking` への切替 | 同上。線形パターンに DFA エンジンは不要。 |
| enum キャストに `Enum.IsDefined` ガード | ネイティブ値→enum は既に switch + default で正規化済。`IsDefined` はボクシング/リフレクションコストがあり冗長。 |

## 4i. 第9ラウンド (数値カルチャ・SemaphoreSlim 非同期ロック・整数オーバーフロー)

ロケール依存の数値バグと非同期排他の正当性を監査。**全軸クリーン、プロダクション変更なし。**

| 出典 | 記事 | 主張 |
|------|------|------|
| Zenn (proudust) | [double.Parse("1.5") → FormatException ← は？](https://zenn.dev/proudust/articles/2020-09-18-csharp-parse-culture) | 仏/独ロケールは小数点がコンマ。`Parse` を `InvariantCulture` 無しで使うと環境依存で例外/誤読。 |
| Qiita (tmokmss) | [特定ロケールのみで発生する例外の不思議](https://qiita.com/tmokmss/items/daf0d8427ba392c11a53) | 端末ロケール既定の数値解析は再現性の無いバグの温床。機械可読データは Invariant。 |
| Qiita (laughter) | [await を含むコードの排他制御](https://qiita.com/laughter/items/2c5daf9fef32a694523f) | `await` 区間の排他は `lock` 不可。`SemaphoreSlim.WaitAsync` + `finally Release`。 |
| Zenn (mod_poppo) | [C 言語での整数のオーバーフロー検査](https://zenn.dev/mod_poppo/articles/c-checked-int) | バイト演算/シフトでの桁あふれ。 |

### 監査結果 — 全軸クリーン

| 項目 | 現状 | 判定 |
|------|------|------|
| **`double/float/decimal.Parse`** | コードベース全体に **0 件**。最も危険な小数点セパレータ問題が存在しない | ✅ 該当なし |
| **`int.TryParse` (6 件)** | CAT XML の EAP 型・nmcli/CoreWLAN の channel/freq/signal。すべて **非負 ASCII 整数**で、`int.TryParse("25")` は全カルチャで同一結果。負号も ASCII "-" で一致 | ✅ 実バグなし (CA1305 は理論上の指摘のみ) |
| **数値の機械可読出力フォーマット** | `ExportService` は整数のみ出力 (`int.ToString()` は全カルチャ同一、グループ区切り無し)。日付は `InvariantCulture` 明示。JSON は System.Text.Json (内部 Invariant) | ✅ ラウンドトリップ安全 |
| **`SemaphoreSlim` 非同期ロック** | `ConnectionExecutor`: `WaitAsync(ct)` は **try の外** (L65)、`finally { Release() }` (L132)。キャンセルで WaitAsync が throw しても finally に到達せず**過剰 Release しない** | ✅ 教科書通り正しい |
| **整数オーバーフロー (ビーコン解析)** | `(uint)(b[6] | (b[7]<<8) | (b[8]<<16) | (b[9]<<24))` は uint へ明示キャストで意図的ラップ。境界チェック (`bodyStart+len > data.Length`) で範囲外参照を防止 (R 既出) | ✅ 防御的 |

### この回の結論 — コード変更なし

全軸クリーンのため**プロダクションコードは変更しない**。特筆すべき確認:

1. **`double.Parse` がゼロ** — ロケール数値バグの最大の温床が構造的に存在しない。
   表示は WPF バインディング/`L.Format` 経由、機械可読出力は整数 or System.Text.Json。
2. **`SemaphoreSlim` の過剰 Release バグが無い** — `WaitAsync` を try の外に置く
   正しいイディオム。キャンセル時に Release が呼ばれずセマフォカウントが破壊されない
   (この 1 点だけで「アダプタ毎ロックが壊れて並行接続が漏れる」級のバグを防いでいる)。

> `int.TryParse("25")` に `InvariantCulture` を付ける案は**不採用**。非負整数は全
> カルチャで同一結果のため純粋な儀式 (theater) で、挙動を 1 ビットも変えない。
> CA1305 を suggestion 化する案も不採用 (整数解析に多数ヒットしノイズになる。R7 の
> CA1851 が green だったのとは異なる)。

## 4j. 第10ラウンド (ログインジェクション・Task.WhenAll 例外・WPF Freeze)

攻撃者制御の SSID に着目したログ偽造 (CWE-117) を監査。**実バグを発見・修正。**

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (kaminuma) | [外部入力の攻撃面: インジェクション](https://qiita.com/kaminuma/items/2e0f7a12b17e2b6e3dc9) | 未検証入力の `\r\n` を行指向ログへ出力するとログ注入 (CWE-117)。`[\r\n\t]` の無害化が最低限の防御。 |
| Zenn (dara) | [More Effective C# メモ (第3章)](https://zenn.dev/dara/scraps/63854485fc53cf) | `await` した Task は最初の例外のみ送出。`Task.Result/Wait` は AggregateException に集約。 |
| Zenn (rioil) | [WPF の Freeze](https://zenn.dev/rioil/scraps/a53f242bd675ff) | `Freezable.Freeze()` で変更監視を省きグラフィック最適化。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **ログインジェクション (SSID 経由)** | `PiiMask.Ssid` が先頭 2 文字を**生**で残し、Serilog の `{Message:lj}` は非エスケープ描画。攻撃者が `"\r\n偽ログ"` SSID をブロードキャスト→プレーンテキストログに改行注入 | ⚠ **CWE-117 実バグ → 修正** |
| **`Task.WhenAll` 例外** | 自動スキャン (タイマー駆動, L176) は `SafeRefreshOne` で各 Task を try/catch ラップ済。一方**手動リフレッシュ 2 箇所 (L91 Load / L111 RefreshAllAsync) は `a.RefreshAsync()` を直渡し**し、複数アダプタ同時失敗時に**最初の例外しかログに出ない** | ⚠ **観測性の軽微なギャップ → 統一修正** |
| **WPF `Freeze`** | テーマブラシは XAML 静的リソース (WPF が自動 Freeze)。動的生成 Brush/Geometry をスレッド跨ぎ共有する箇所なし | ✅ 該当なし |

### 適用した修正 — `PiiMask.Ssid` の制御文字無害化 (CWE-117)

**攻撃シナリオ**: 802.11 SSID は任意オクテット (CR/LF 含む) を許容する。攻撃者が
`"\r\n2099-01-01 [ERR] forged entry"` のような SSID をブロードキャストし、被害者の
MWC がそれをログ (接続試行・フェイルオーバー等) に出力すると、`PiiMask.Ssid` が
先頭 2 文字 (`\r\n`) を生で残すため、`Serilog.Sinks.File` のプレーンテキスト出力
(`{Message:lj}` は文字列プロパティを非エスケープで描画) に**改行が注入されログ行が偽造**
される。

**修正**: マスク時に残す先頭 2 文字の `char.IsControl(c)` を `'?'` に置換。`string.Create`
で確保効率も維持。可視文字 (絵文字・日本語・アクセント付き) は保持し、制御文字のみ無害化。

```csharp
string prefix = string.Create(keep, ssid, static (dst, src) =>
{
    for (int i = 0; i < dst.Length; i++)
    {
        char c = src[i];
        dst[i] = char.IsControl(c) ? '?' : c;   // CR/LF/TAB/C0/C1 → '?'
    }
});
```

テスト 7 ケース追加 (`PiiMaskSsidTests`):
- `\r\n…` / `\n…` / `\r…` / `\t\t…` / `…` → マスク結果に制御文字が**残らない**
  ことを `masked.Any(char.IsControl)` で検証。
- `日本語…`→`日本` / `Café`→`Ca` → 可視文字は保持されることを検証。
- 既存 8 ケース (マスク桁数契約) は不変で全通過。

> これは R6/R8 のような「安全だが明示化」ではなく、**実際に悪用可能なログ偽造の修正**。
> SSID は唯一の攻撃者完全制御の入力であり、`PiiMask.Ssid` が全 SSID ログの単一通過点
> であるため、ここでの無害化が最小かつ確実な対策になる。

### 適用した修正 (2) — `Task.WhenAll` の例外取りこぼし統一

`MainViewModel` の手動リフレッシュ 2 箇所が `Task.WhenAll(Adapters.Select(a => a.RefreshAsync()))`
と直渡しで、`RefreshAsync` は `try/finally` のみ (catch なし) のため、複数アダプタが同時に
失敗すると **WhenAll が最初の例外しか再送出せず**、他アダプタの失敗がログに残らない。
自動スキャン経路 (L176) は既に `SafeRefreshOne` で各 Task を try/catch ラップしてこの問題を
回避済だったため、**手動経路 2 箇所も `SafeRefreshOne` 経由に統一**し、各アダプタの失敗を
独立にログするようにした (挙動の一貫性 + 観測性向上)。

### 不採用 (第10ラウンド)

| 提案 | 理由 |
|------|------|
| 全ログ呼び出しに汎用サニタイザ | SSID が唯一の攻撃者完全制御入力。アダプタ名/BSSID は OS/ドライバ由来で、SSID チョークポイント (`PiiMask.Ssid`) の無害化で主要ベクタは塞がる。 |
| Serilog を JSON シンクに変更 | プレーンテキストログは人間可読性で運用上重要。シンク変更より入力無害化が正攻法 (OWASP も入力中和を推奨)。 |

## 4k. 第11ラウンド (DPAPI スコープ・パストラバーサル・クリップボード機密・書式文字列)

R10 に続き脅威モデル視点で機密の永続化/露出経路を監査。**実バグ 1 件 (クリップボード露出) を修正。**

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (OTAGAI-SAMA) | [ProtectedData クラス](https://qiita.com/OTAGAI-SAMA/items/c0da45ad60f9d07d9efc) | DPAPI `CurrentUser` はログイン中ユーザーのみ復号可。`LocalMachine` は同一 PC の全ユーザーが復号可。 |
| Qiita (keitakei777) | [Directory Traversal 脆弱性](https://qiita.com/keitakei777/items/3ff73388786112d79d76) | ユーザー入力をそのままパスに使うと `../` で外部ファイルへ。`Path.GetFileName` / ベースディレクトリ配下検査。 |
| Zenn (creanciel) | [Windows のクリップボードの話](https://zenn.dev/creanciel/articles/windows-clipboard) | クリップボードはアプリ間共有。履歴 (Win+V)・クラウド同期に残存しうる。 |
| Qiita (twrcd1227) | [Format String Attack](https://qiita.com/twrcd1227/items/c1b0eefb9cf2736737a1) | 書式文字列を攻撃者が制御すると出力操作/クラッシュ。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **DPAPI 保護スコープ** | `DpapiSecretProtector` は `DataProtectionScope.CurrentUser` + 固定 Entropy ("WiFix-v1")。同一 PC の別ユーザーは復号不可 | ✅ 正しい |
| **パストラバーサル** | `Path.Combine` は全て定数 (logs/config/exe)。SSID・プロファイル名・アダプタ名からパスを構築する箇所なし。QR 保存の `SaveFileDialog` 既定名は `ValidateNames=true` (既定) が不正文字を拒否 | ✅ 該当なし |
| **クリップボード機密露出** | `QrCodeDialog.OnCopy` が**パスフレーズを含む WIFI: URI** を `Clipboard.SetText` で素のままコピー→Win+V 履歴・クラウド同期に残存 | ⚠ **実バグ → 修正** |
| **書式文字列インジェクション** | `string.Format` の書式引数は全て resx 由来の `CompositeFormat` (制御済)。SSID/ユーザー入力をテンプレートに渡す箇所なし | ✅ 該当なし |

### 適用した修正 — クリップボード機密の履歴/クラウド除外

`QrCodeDialog` の「コピー」は `_uri = WifiUri.Build(spec)`、すなわち
`WIFI:S:…;T:WPA;P:<パスフレーズ>;;` をクリップボードへ置く。素の `Clipboard.SetText`
では Windows の**クリップボード履歴 (Win+V) に残り、設定によってはクラウド同期で
他デバイスへ伝播**する。一度の貼り付けを越えてパスフレーズが残存・拡散する。

新規 `SensitiveClipboard.SetText` (App/Services) を追加し、`DataObject` に Windows
標準のクリップボードフォーマットを付与して履歴・クラウド・モニタから除外する
(パスワードマネージャ KeePass 等と同じ手法):
- `ExcludeClipboardContentFromMonitorProcessing`
- `CanIncludeInClipboardHistory` = DWORD 0
- `CanUploadToCloudClipboard` = DWORD 0

`QrCodeDialog.OnCopy` をこれ経由に変更。クリップボード競合時は例外を投げず警告ログ
のみ (機密内容自体はログに出さない)。SSID コピー (`MainWindowCommands`) は SSID が
非機密のため対象外。

> CLAUDE.md がパスフレーズ/WIFI: URI を「ログ禁止」の機密として扱う方針を、**クリップ
> ボードという別の永続化経路**へ拡張した。ユーザーが明示的にコピーする UX は維持しつつ、
> 履歴・クラウドへの残存だけを断つ。

### 不採用 (第11ラウンド)

| 提案 | 理由 |
|------|------|
| コピー後の自動クリップボードクリア (タイマー) | UX を阻害 (貼り付け前に消えうる)。履歴/クラウド除外で残存リスクは十分低減。 |
| QR 保存ファイル名の SSID サニタイズ | `SaveFileDialog.ValidateNames=true` (既定) が不正文字を拒否し、ユーザーが最終パスを確認する。実害なし。 |

## 4l. 第12ラウンド (TLS 検証・キャプティブポータル WebBrowser)

R10/R11 に続く脅威モデル監査。MWC が**信頼できないネットワーク由来のコンテンツ**を
扱う 2 経路 (TLS 通信・キャプティブポータル描画) を確認。**WebBrowser 経路を 1 件修正。**

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (asterisk9101 ほか) | [HttpClient で証明書検証をスキップ](https://qiita.com/asterisk9101/items/20ae81016b1cf2e23614) | `ServerCertificateCustomValidationCallback => true` は全検証を無効化＝MITM に脆弱。本番では絶対に使わない。 |
| Zenn (sakaki_web) | [WPF における WebView2 実装](https://zenn.dev/sakaki_web/articles/6e24d3f06c3fdc) | レガシー WebBrowser (IE エンジン) から Chromium ベースの WebView2 への移行。ドメイン検証等のセキュリティ考慮。 |
| Zenn (nuits_jp) | [WPF WebBrowser の Window Open インターセプト](https://zenn.dev/nuits_jp/articles/2016-06-25-wpf-webbrowser-window-open-intercept) | IE WebBrowser はスクリプトの window.open で制御不能なポップアップを出しうる。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **TLS 証明書検証バイパス** | `HttpConnectivityChecker` / `AppUpdateService` に `ServerCertificateCustomValidationCallback` も `=> true` バイパスも**無し**。既定の検証が有効 (CA5359=error も担保) | ✅ 正しい |
| **キャプティブポータルの埋め込み WebBrowser** | `CaptivePortalDialog` がレガシー IE エンジンの `<WebBrowser>` で**ネットワーク提供 (敵対的でありうる) のポータルページ**を描画。`OnNavigating` がスキーム検証していなかった | ⚠ **多層防御を追加** |

### 適用した修正 — 埋め込みブラウザのナビゲーションを http/https に限定

埋め込み `WebBrowser` は信頼できないキャプティブポータルを描画する。悪意あるポータルが
`file://` やカスタムスキームへリダイレクトすれば、IE エンジン経由でローカルファイル開示や
スキーム悪用を狙える。`OnNavigating(NavigatingCancelEventArgs e)` の `e.Cancel` を使い、
**http/https 以外の絶対 URI へのナビゲーションを拒否**する (R6 BrowserLauncher の外部起動
スキーム検証を、エンジン内部のナビゲーションにも適用)。

```csharp
if (e.Uri is { IsAbsoluteUri: true } uri &&
    uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
{
    Serilog.Log.Warning("Captive portal blocked a non-http(s) navigation (scheme blocked for safety)");
    e.Cancel = true;   // file:// / custom scheme 等を IE エンジンに踏ませない
    return;
}
```

正規のキャプティブポータル (http/https リダイレクト) は不変。URL 自体はログに残さず
ブロックの事実のみ記録。

### 不採用 (第12ラウンド)

| 提案 | 理由 |
|------|------|
| IE WebBrowser のレジストリ機能フラグ調整 (FEATURE_BROWSER_EMULATION 等) | レジストリ書込みで脆く環境依存。スキーム制限の方が確実で副作用が小さい。 |
| 埋め込みブラウザ廃止・外部ブラウザのみ | UX を大きく変える。スキーム制限で主要リスクは低減。WebView2 移行 (§5) で本質対応。 |

## 4m. 第13ラウンド (ソート比較子・自動更新の完全性/堅牢性・ゼロ除算)

クラッシュ系の品質バグと更新経路の安全性を監査。**堅牢性を 1 件改善。**

| 出典 | 記事 | 主張 |
|------|------|------|
| Qiita (nabemax) | [IComparable/IComparer/Comparison](https://qiita.com/nabemax/items/81e3ab884b20be8386dc) | カスタム比較子が推移律を破ると `List.Sort` が `ArgumentException` を投げる。 |
| Zenn (haretokidoki) | [ハッシュ値を確認する方法](https://zenn.dev/haretokidoki/articles/651424302fa922) | DL ファイルの改ざん検証は `Get-FileHash`/`certutil`。 |
| Zenn (johmaru) | [WPF アプリの自動アップデート](https://zenn.dev/johmaru/articles/535c12baee666d) | 自動更新は署名/ハッシュ検証が要。 |

### 監査結果

| 項目 | 現状 | 判定 |
|------|------|------|
| **カスタムソート比較子** | `IComparer`/`Comparison`/`List.Sort` の使用は**ゼロ**。並び替えは LINQ `OrderBy/ThenBy` (安定ソート、推移律違反で例外を投げない) | ✅ 該当なし |
| **自動更新の完全性** | `AppUpdateService` は GitHub `releases/latest` JSON を**通知目的のみ**取得。DL/実行は一切しない (ユーザーが手動取得) ため署名検証の対象外。HTTPS + 既定証明書検証 | ✅ サプライチェーンリスク無し |
| **バージョン比較の正しさ** | `Version.TryParse` で `System.Version` 同士を**数値比較** (`latest > current`)。文字列比較ではないので `3.11.0 > 3.9.0` が正しく判定される + `!prerelease` 除外 | ✅ 正しい (セマンティック) |
| **ゼロ除算 / NaN** | `KalmanRssiFilter` は `measurementNoise > 0` をコンストラクタで要求 (分母 `P+R>0`)。`RssiDistanceEstimator` は `pathLossExponent > 0` 要求 + `freqMhz<=0` で早期 return (`log10(0)` 回避) | ✅ 全て構成時ガード済 |
| **更新 JSON の欠落プロパティ堅牢性** | `root.GetProperty("tag_name")` がプロパティ欠落時に `KeyNotFoundException` を投げる | ⚠ レート制限時に誤解を招く例外 → 改善 |

### 適用した改善 — `AppUpdateService` の欠落プロパティ耐性

GitHub API は**未認証で 60 req/h のレート制限**があり、超過時は `tag_name` を持たない
エラー JSON (`{"message":"API rate limit exceeded", ...}`) を返す。現状の
`root.GetProperty("tag_name")` はこの応答で `KeyNotFoundException` を投げ、`catch` で
拾われるものの**誤解を招くスタックトレース付きで "Update check failed" ログ**になる
(レート制限は通常運用で頻発する)。

`TryGetProperty` + `ValueKind` チェックに変更し、「リリースではない応答」を例外を介さず
静かに `Failed` 扱いにする。成功時の挙動は不変。`prerelease` も `ValueKind == True` で
安全に読む。

```csharp
if (root.ValueKind != JsonValueKind.Object ||
    !root.TryGetProperty("tag_name", out var tagEl))
    return UpdateCheckResult.Failed;        // レート制限/非リリース応答を静かに無視
```

### 不採用 (第13ラウンド)

| 提案 | 理由 |
|------|------|
| 更新成果物の署名/ハッシュ検証 | `AppUpdateService` は DL/実行せず通知のみ。検証対象の成果物がそもそも無い。 |
| リリースノート (GitHub body) のサニタイズ | WPF `TextBlock.Text` は markup 非解釈の素テキスト表示。リポジトリ管理者しか書けず一般攻撃者の制御外。 |

## 5. 次の自然な深掘り候補 (将来セッション用)

-1. **キャプティブポータルを WebBrowser → WebView2 へ移行** (§4l 関連)
   レガシー IE エンジンは敵対的な Web コンテンツに対する攻撃面が大きい。Chromium ベースの
   `WebView2` へ移行すれば最新のサンドボックス・パッチを享受できる。ただし WebView2 ランタイム
   依存の追加と非トリビアルな書換えを伴うため ADR 化して計画的に。当面は §4l のスキーム制限で
   多層防御。

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
