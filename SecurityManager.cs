using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MurtiWifiConnecter
{
    /// <summary>
    /// シンプルなセキュリティマネージャー
    /// </summary>
    public static class SecurityManager
    {
        private static readonly byte[] _entropyBytes = Encoding.UTF8.GetBytes("MurtiWiFi2024");
        
        /// <summary>
        /// WiFiパスワードを暗号化
        /// </summary>
        public static SecureString EncryptPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) 
                return new SecureString(string.Empty);
            
            try
            {
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var encryptedBytes = ProtectedData.Protect(passwordBytes, _entropyBytes, DataProtectionScope.CurrentUser);
                var base64 = Convert.ToBase64String(encryptedBytes);
                
                // 元のメモリをクリア
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
                
                return new SecureString(base64);
            }
            catch
            {
                return new SecureString(string.Empty);
            }
        }
        
        /// <summary>
        /// WiFiパスワードを復号化
        /// </summary>
        public static string DecryptPassword(SecureString encryptedPassword)
        {
            if (encryptedPassword?.Value == null || string.IsNullOrEmpty(encryptedPassword.Value))
                return string.Empty;
                
            try
            {
                var base64 = encryptedPassword.Value;
                var encryptedBytes = Convert.FromBase64String(base64);
                var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, _entropyBytes, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decryptedBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// WiFi認証情報の基本検証
        /// </summary>
        public static bool ValidateWiFiCredentials(string ssid, string password)
        {
            // SSIDの基本検証
            if (string.IsNullOrWhiteSpace(ssid) || ssid.Length > 32)
                return false;
            
            // パスワードの基本検証（WPA/WPA2/WPA3の一般的な要件）
            if (!string.IsNullOrEmpty(password))
            {
                if (password.Length < 8 || password.Length > 63)
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// ファイルの安全な削除
        /// </summary>
        public static bool SecureDeleteFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return true;
                
                // ファイルをゼロで上書きしてから削除
                var fileInfo = new FileInfo(filePath);
                var fileLength = fileInfo.Length;
                
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[1024];
                    for (long i = 0; i < fileLength; i += buffer.Length)
                    {
                        fileStream.Write(buffer, 0, (int)Math.Min(buffer.Length, fileLength - i));
                    }
                    fileStream.Flush();
                }
                
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// セキュアな文字列管理
    /// </summary>
    public class SecureString : IDisposable
    {
        private string _value;
        private bool _disposed;
        
        public SecureString(string value)
        {
            _value = value ?? string.Empty;
        }
        
        public string Value => _disposed ? string.Empty : _value;
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _value = string.Empty;
                _disposed = true;
            }
        }
    }
}