// ─────────────────────────────────────────────────────────────────────────────
//  System.Security.Cryptography.ProtectedData の **型検査専用スタブ**。製品には含めない。
//
//  なぜ在るか:
//    DPAPI は Windows 専用で、`System.Security.Cryptography.ProtectedData` は
//    NuGet パッケージ (または Windows 用の参照パック) からしか入らない。
//    そのため `DpapiSecretProtector` はこの環境でコンパイルできなかった。
//
//  循環しない理由 (WpfMinimal.Stub.cs のダイアログ群とはここが違う):
//    これは**公開された安定した BCL API** であり、署名は検査対象のコードから
//    逆算したものではない。よって「呼び出しが実際の API と合っているか」を
//    ここで確かめる意味がある。
//      public static byte[] Protect  (byte[] userData,   byte[]? optionalEntropy, DataProtectionScope scope)
//      public static byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope)
//
//  ★ 検査しないこと: DPAPI の実挙動 (ユーザーバウンド性、エントロピーの効果)。
//    それは Windows 実機でしか確かめられない。
// ─────────────────────────────────────────────────────────────────────────────
namespace System.Security.Cryptography
{
    public enum DataProtectionScope { CurrentUser = 0, LocalMachine = 1 }

    public static class ProtectedData
    {
        public static byte[] Protect(byte[] userData, byte[]? optionalEntropy, DataProtectionScope scope)
            => userData;

        public static byte[] Unprotect(byte[] encryptedData, byte[]? optionalEntropy, DataProtectionScope scope)
            => encryptedData;
    }
}
