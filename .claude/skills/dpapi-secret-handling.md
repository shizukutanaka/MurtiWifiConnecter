# Skill: dpapi-secret-handling

## 用途
パスワード・機密情報を安全に扱うパターン。

## 実装場所
`src/MWC.Platform.Windows/DpapiSecretProtector.cs`

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
