using System.Security.Cryptography;
using MWC.Core.Abstractions;

namespace MWC.Platform.Windows;

/// <summary>
/// Windows DPAPI による現ユーザーバウンド暗号化。
/// 別ユーザー/別マシンへ移すと復号不可。
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // 追加エントロピー(アプリ識別)。値変更でリーク後の総入替可能。
    // ⚠ この値は既存ユーザーの保存済み暗号データに依存するため変更禁止。
    private static readonly byte[] Entropy =
    {
        0x57, 0x69, 0x46, 0x69, 0x78, 0x2D, 0x76, 0x31  // "WiFix-v1"
    };

    public byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
