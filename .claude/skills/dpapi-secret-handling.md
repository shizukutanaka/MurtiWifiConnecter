# Skill: dpapi-secret-handling

## 用途
パスワード・機密情報を安全に扱う**あるべきパターン**(必須事項)。

## 現状とのギャップ(2026-09 追記。適用前に必ず読むこと)
以下は CLAUDE.md が必須と定める設計であり、`DpapiSecretProtector.Protect`/
`Unprotect` 自体は実際にこのとおり実装されている。**ただし**:
- `Protect`/`Unprotect` は DI 登録されているのみで、コード内のどこからも
  呼び出されていない(`docs/FEATURE-AUDIT.md` §1b「準孤立」参照)。
- 実際のパスフレーズは `WifiProfileSpec.Passphrase`(string)から
  `ConnectDialog` の `PasswordBox.Password` まで一貫して**平文の string**として
  扱われており、下記の SecureString パターンはどこにも使われていない
  (`docs/FEATURE-AUDIT.md` §2b「CLAUDE.md ルールと実装の乖離」参照——
  **ユーザー裁定待ちであり、この skill を根拠に勝手に実装を変更しないこと**。
  2026-07 に無断でルール側を緩和しようとして差し戻された経緯がある)。

この skill は「新しく機密情報を扱うコードを書くときはこう書け」という規範であり、
「現在こう動いている」という説明ではない。両者を混同しないこと。

## 実装場所
`src/MWC.Platform.Windows/DpapiSecretProtector.cs`(呼び出し元は無い。上記参照)

## 必須パターン

### SecureString → 使用 → ゼロクリア
```csharp
IntPtr ptr = Marshal.SecureStringToGlobalAllocUnicode(secureStr);
try
{
    string plain = Marshal.PtrToStringUni(ptr) ?? "";
    // ← ここでだけ plain を使う
}
finally
{
    Marshal.ZeroFreeGlobalAllocUnicode(ptr);  // 必須
}
```

### DPAPI 保護
```csharp
// 保護: byte[] plaintext → byte[] ciphertext
byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

// 復号: byte[] ciphertext → byte[] plaintext
byte[] plain  = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
```

## 禁止事項
- `string` 型でパスワードを長期保持しない
- Temp ファイルにパスワードを書き出さない
- ログ/例外メッセージにパスワードを含めない
- `DataProtectionScope.LocalMachine` は使わない(別ユーザーが読める)

## テスト
純粋な単体テストはOS依存なので `[Fact(Skip="requires Windows")]` で管理。
