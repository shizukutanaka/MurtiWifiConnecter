# ADR-0008: i18n 戦略 — L.cs 型安全アクセサ

**ステータス**: 採用済み  
**日付**: 2026-04-26

---

## 背景

v1.0〜v1.7 ではハードコード日本語が 68 箇所存在した。`.resx` は用意していたが参照ゼロだった。

## 検討した案

| 案 | 長所 | 短所 |
|---|---|---|
| A. `Strings.ja.resx` + `ResourceManager.GetString(key)` | 標準 | 文字列キー=タイポリスク |
| B. **`L.cs` 静的プロパティ** (採用) | IntelliSense / コンパイル時検出 | 新キー追加時に `L.cs` も更新要 |
| C. Source Generator でキー生成 | 完全型安全 | ビルド複雑化 |

## 決定

`L.cs` を `Resources/L.cs` に置き、`ResourceManager` ラッパーとして実装。

- 静的プロパティ: `L.AppTitle`, `L.ActionRefresh` 等
- 動的メソッド: `L.Get("key")`, `L.Format("key", args)`
- フォールバック: キー不存在 → キー名をそのまま返す

## 制約

- 全ユーザー向けテキストは `L.` 経由必須
- `SettingsViewModel` の言語名のみネイティブ表記(例: `"日本語"`)は例外

## 検証指標

```
ハードコード日本語 = 0  (grep スキャンで継続確認)
L.cs 参照 ≥ 100 箇所
resx キー数 ≡ 全言語ファイルで一致
```
