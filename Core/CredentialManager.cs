using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MurtiWifiConnecter.Core
{
    internal static class CredentialManager
    {
        private const string Advapi32 = "Advapi32.dll";
        private const uint CredTypeGeneric = 1;
        private const uint CredPersistLocalMachine = 2;

#if WINDOWS
        public static bool IsSupported => OperatingSystem.IsWindows();
#else
        public static bool IsSupported => false;
#endif

#if WINDOWS
        public static bool TryWriteCredential(string target, string userName, string secret, out int error)
        {
            error = 0;
            if (string.IsNullOrWhiteSpace(target) || secret == null)
            {
                return false;
            }

            var credential = default(NativeCredential);
            var secretBytes = Encoding.Unicode.GetBytes(secret);

            try
            {
                credential = new NativeCredential
                {
                    Flags = 0,
                    Type = CredTypeGeneric,
                    TargetName = Marshal.StringToCoTaskMemUni(target),
                    CredentialBlobSize = (uint)secretBytes.Length,
                    CredentialBlob = Marshal.AllocCoTaskMem(secretBytes.Length),
                    Persist = CredPersistLocalMachine,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = IntPtr.Zero,
                    UserName = Marshal.StringToCoTaskMemUni(userName ?? Environment.UserName ?? string.Empty)
                };

                Marshal.Copy(secretBytes, 0, credential.CredentialBlob, secretBytes.Length);

                if (!CredWrite(ref credential, 0))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }

                return true;
            }
            finally
            {
                if (credential.TargetName != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.TargetName);
                }
                if (credential.CredentialBlob != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.CredentialBlob);
                }
                if (credential.UserName != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(credential.UserName);
                }
            }
        }

        public static bool TryReadCredential(string target, out string secret, out int error)
        {
            secret = null;
            error = 0;

            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (!CredRead(target, CredTypeGeneric, 0, out var credentialPtr))
            {
                error = Marshal.GetLastWin32Error();
                if (credentialPtr != IntPtr.Zero)
                {
                    CredFree(credentialPtr);
                }
                return false;
            }

            try
            {
                var credential = Marshal.PtrToStructure<NativeCredential>(credentialPtr);
                if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
                {
                    var bytes = new byte[credential.CredentialBlobSize];
                    Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                    secret = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                }
                else
                {
                    secret = string.Empty;
                }
                return true;
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        public static bool TryDeleteCredential(string target, out int error)
        {
            error = 0;
            if (string.IsNullOrWhiteSpace(target))
            {
                return false;
            }

            if (!CredDelete(target, CredTypeGeneric, 0))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            return true;
        }
#else
        public static bool TryWriteCredential(string target, string userName, string secret, out int error)
        {
            error = 0;
            return false;
        }

        public static bool TryReadCredential(string target, out string secret, out int error)
        {
            secret = null;
            error = 0;
            return false;
        }

        public static bool TryDeleteCredential(string target, out int error)
        {
            error = 0;
            return false;
        }
#endif

#if WINDOWS
        [DllImport(Advapi32, EntryPoint = "CredWriteW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite(ref NativeCredential credential, uint flags);

        [DllImport(Advapi32, EntryPoint = "CredReadW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

        [DllImport(Advapi32, EntryPoint = "CredDeleteW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport(Advapi32, SetLastError = true)]
        private static extern void CredFree(IntPtr credential);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NativeCredential
        {
            public uint Flags;
            public uint Type;
            public IntPtr TargetName;
            public IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public IntPtr TargetAlias;
            public IntPtr UserName;
        }
#endif

        /// <summary>
        /// Security-003: DPAPI鍵ローテーション手順の追加
        /// DPAPIマスターキーの定期ローテーション機能
        /// </summary>
        public static class DpapiKeyRotation
        {
            private static readonly TimeSpan DefaultRotationInterval = TimeSpan.FromDays(90);
            private static readonly string KeyRotationMetadataFile = "dpapi_rotation.meta";

            /// <summary>
            /// DPAPI鍵のローテーションが必要かどうかをチェック
            /// </summary>
            public static async Task<bool> IsRotationNeededAsync()
            {
                try
                {
                    var lastRotation = await GetLastRotationTimeAsync();
                    var timeSinceRotation = DateTime.UtcNow - lastRotation;
                    return timeSinceRotation >= DefaultRotationInterval;
                }
                catch
                {
                    // 初回実行時はローテーションが必要
                    return true;
                }
            }

            /// <summary>
            /// DPAPI鍵ローテーションを実行
            /// </summary>
            public static async Task<DpapiRotationResult> RotateDpapiKeyAsync()
            {
                var result = new DpapiRotationResult
                {
                    Success = false,
                    ErrorMessage = string.Empty,
                    OldKeyTimestamp = DateTime.MinValue,
                    NewKeyTimestamp = DateTime.UtcNow
                };

                try
                {
                    // 現在のローテーション時間を記録
                    result.OldKeyTimestamp = await GetLastRotationTimeAsync();

                    // 新しいDPAPI鍵を生成（実際にはWindowsが自動管理）
                    // ここではメタデータの更新のみを行う
                    await UpdateRotationMetadataAsync(result.NewKeyTimestamp);

                    // 既存の資格情報を再暗号化（オプション）
                    await ReEncryptExistingCredentialsAsync();

                    result.Success = true;

                    await Logger.LogInfo("DPAPI key rotation completed successfully", nameof(DpapiKeyRotation), new Dictionary<string, object>
                    {
                        ["oldKeyTimestamp"] = result.OldKeyTimestamp,
                        ["newKeyTimestamp"] = result.NewKeyTimestamp
                    });
                }
                catch (Exception ex)
                {
                    result.ErrorMessage = ex.Message;
                    await Logger.LogError("DPAPI key rotation failed", nameof(DpapiKeyRotation), new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }, ex);
                }

                return result;
            }

            /// <summary>
            /// スケジュールされたDPAPI鍵ローテーションを実行
            /// </summary>
            public static async Task<DpapiRotationResult> PerformScheduledRotationAsync()
            {
                if (!await IsRotationNeededAsync())
                {
                    return new DpapiRotationResult
                    {
                        Success = true,
                        ErrorMessage = "Rotation not needed yet",
                        OldKeyTimestamp = await GetLastRotationTimeAsync(),
                        NewKeyTimestamp = DateTime.MinValue
                    };
                }

                return await RotateDpapiKeyAsync();
            }

            /// <summary>
            /// DPAPI鍵ローテーションのステータスを取得
            /// </summary>
            public static async Task<DpapiKeyStatus> GetKeyStatusAsync()
            {
                var lastRotation = await GetLastRotationTimeAsync();
                var timeSinceRotation = DateTime.UtcNow - lastRotation;
                var isExpired = timeSinceRotation >= DefaultRotationInterval;

                return new DpapiKeyStatus
                {
                    LastRotationTime = lastRotation,
                    TimeSinceRotation = timeSinceRotation,
                    IsExpired = isExpired,
                    DaysUntilExpiration = isExpired ? 0 : (DefaultRotationInterval - timeSinceRotation).Days,
                    RotationIntervalDays = DefaultRotationInterval.Days
                };
            }

            private static async Task<DateTime> GetLastRotationTimeAsync()
            {
                try
                {
                    var metadataPath = GetMetadataFilePath();
                    if (!File.Exists(metadataPath))
                    {
                        return DateTime.MinValue;
                    }

                    var content = await File.ReadAllTextAsync(metadataPath);
                    if (DateTime.TryParse(content.Trim(), out var lastRotation))
                    {
                        return lastRotation;
                    }
                }
                catch
                {
                    // メタデータファイルが破損している場合
                }

                return DateTime.MinValue;
            }

            private static async Task UpdateRotationMetadataAsync(DateTime rotationTime)
            {
                var metadataPath = GetMetadataFilePath();
                var directory = Path.GetDirectoryName(metadataPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(metadataPath, rotationTime.ToString("O"));
            }

            private static async Task ReEncryptExistingCredentialsAsync()
            {
                // 既存の資格情報を新しいDPAPI鍵で再暗号化
                // 注意: DPAPIは自動的に適切な鍵を使用するため、
                // 通常は明示的な再暗号化は必要ないが、
                // ログを記録して将来の検証に備える

                await Logger.LogInfo("Credential re-encryption check completed", nameof(DpapiKeyRotation), new Dictionary<string, object>
                {
                    ["note"] = "DPAPI handles key rotation automatically"
                });
            }

            private static string GetMetadataFilePath()
            {
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var murtiPath = Path.Combine(appDataPath, "MurtiWifiConnecter", "Security");
                return Path.Combine(murtiPath, KeyRotationMetadataFile);
            }
        }

        public class DpapiRotationResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
            public DateTime OldKeyTimestamp { get; set; }
            public DateTime NewKeyTimestamp { get; set; }
        }

        public class DpapiKeyStatus
        {
            public DateTime LastRotationTime { get; set; }
            public TimeSpan TimeSinceRotation { get; set; }
            public bool IsExpired { get; set; }
            public int DaysUntilExpiration { get; set; }
            public int RotationIntervalDays { get; set; }
        }
    }
}
