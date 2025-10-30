using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core
{
    public static class AuditTrail
    {
        private static readonly SemaphoreSlim _writeLock = new(1, 1);
        private static readonly object _initLock = new();
        private static bool _initialized;
        private static string _auditDirectory = string.Empty;
        private static DateTime _currentDate = DateTime.MinValue;
        private static string _currentAuditFile = string.Empty;
        private const int MaxAuditFiles = 30;
        private static byte[] _integrityKey;
        private const string AuditDigestExtension = ".hmac";
        private const string IntegrityKeyName = "audit_integrity";

        public static async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            lock (_initLock)
            {
                if (_initialized)
                {
                    return;
                }

                _auditDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MurtiWifiConnecter",
                    "audit");

                Directory.CreateDirectory(_auditDirectory);
                _initialized = true;
            }

            await EnsureIntegrityKeyLoadedAsync().ConfigureAwait(false);
            await Logger.InitializeAsync().ConfigureAwait(false);
        }

        public static async Task RecordEventAsync(string category, string action, IDictionary<string, object>? details = null, string severity = "Info")
        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(category))
            {
                category = "General";
            }

            if (string.IsNullOrWhiteSpace(action))
            {
                action = "Unknown";
            }

            var entry = new AuditEntry
            {
                Timestamp = DateTime.UtcNow,
                Category = category,
                Action = action,
                Severity = severity,
                Actor = Environment.UserName,
                Machine = Environment.MachineName,
                Details = details
            };

            await WriteEntryAsync(entry).ConfigureAwait(false);
            await Logger.LogInfo("Audit event recorded", nameof(AuditTrail), new Dictionary<string, object>
            {
                ["category"] = category,
                ["action"] = action,
                ["severity"] = severity
            });
        }

        private static async Task EnsureInitializedAsync()
        {
            if (_initialized)
            {
                return;
            }

            await InitializeAsync().ConfigureAwait(false);
        }

        private static async Task WriteEntryAsync(AuditEntry entry)
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var nowDate = entry.Timestamp.Date;
                if (nowDate != _currentDate || string.IsNullOrWhiteSpace(_currentAuditFile))
                {
                    _currentDate = nowDate;
                    _currentAuditFile = Path.Combine(_auditDirectory, $"audit_{_currentDate:yyyy-MM-dd}.jsonl");
                }

                await EnsureIntegrityKeyLoadedAsync().ConfigureAwait(false);
                entry.Signature = ComputeEntrySignature(entry);

                var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                await File.AppendAllTextAsync(_currentAuditFile, json + Environment.NewLine).ConfigureAwait(false);
                await WriteIntegrityDigestAsync(_currentAuditFile).ConfigureAwait(false);

                CleanupOldAudits();
            }
            catch (Exception ex)
            {
                await Logger.LogError("Audit write failure", nameof(AuditTrail), new Dictionary<string, object>
                {
                    ["file"] = _currentAuditFile
                }, ex).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public sealed class AuditPurgeResult
        {
            public int AuditFilesRemoved { get; init; }
            public int DigestFilesRemoved { get; init; }
        }

        public static async Task<AuditPurgeResult> PurgeAuditLogsAsync(int retentionDays = 90, bool secureDelete = true)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(_auditDirectory) || !Directory.Exists(_auditDirectory))
            {
                return new AuditPurgeResult();
            }

            var cutoff = DateTime.Now.AddDays(-Math.Max(retentionDays, 0));
            var auditRemoved = 0;
            var digestRemoved = 0;

            var directory = new DirectoryInfo(_auditDirectory);
            var auditFiles = directory.GetFiles("audit_*.jsonl", SearchOption.TopDirectoryOnly);

            foreach (var file in auditFiles)
            {
                if (file.LastWriteTime >= cutoff)
                {
                    continue;
                }

                try
                {
                    var digestPath = file.FullName + AuditDigestExtension;

                    if (secureDelete)
                    {
                        await SecurityManager.SecureDeleteFileAsync(file.FullName).ConfigureAwait(false);
                    }
                    else
                    {
                        file.Delete();
                    }

                    auditRemoved++;

                    if (File.Exists(digestPath))
                    {
                        if (secureDelete)
                        {
                            await SecurityManager.SecureDeleteFileAsync(digestPath).ConfigureAwait(false);
                        }
                        else
                        {
                            File.Delete(digestPath);
                        }

                        digestRemoved++;
                    }
                }
                catch (Exception ex)
                {
                    await Logger.LogWarning("Failed to purge audit log", nameof(AuditTrail), new Dictionary<string, object>
                    {
                        ["file"] = file.FullName,
                        ["error"] = ex.Message
                    }).ConfigureAwait(false);
                }
            }

            if (auditRemoved > 0 || digestRemoved > 0)
            {
                await Logger.LogInfo("Audit logs purged", nameof(AuditTrail), new Dictionary<string, object>
                {
                    ["auditFilesRemoved"] = auditRemoved,
                    ["digestFilesRemoved"] = digestRemoved,
                    ["retentionDays"] = retentionDays,
                    ["secureDeletion"] = secureDelete
                }).ConfigureAwait(false);
            }

            return new AuditPurgeResult
            {
                AuditFilesRemoved = auditRemoved,
                DigestFilesRemoved = digestRemoved
            };
        }

        private static void CleanupOldAudits()
        {
            try
            {
                var files = new DirectoryInfo(_auditDirectory).GetFiles("audit_*.jsonl");
                if (files.Length <= MaxAuditFiles)
                {
                    return;
                }

                Array.Sort(files, (a, b) => a.CreationTimeUtc.CompareTo(b.CreationTimeUtc));

                for (int i = 0; i < files.Length - MaxAuditFiles; i++)
                {
                    DeleteDigest(files[i].FullName);
                    files[i].Delete();
                }
            }
            catch
            {
                // Ignore cleanup errors to avoid secondary failures
            }
        }

        public static async Task<bool> VerifyAuditFileAsync(string fileName)
        {
            await EnsureInitializedAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var path = Path.Combine(_auditDirectory, fileName);
            if (!File.Exists(path))
            {
                return false;
            }

            var digestPath = path + AuditDigestExtension;
            if (!File.Exists(digestPath))
            {
                await Logger.LogWarning("Audit digest missing", nameof(AuditTrail), new Dictionary<string, object>
                {
                    ["file"] = path
                });
                return false;
            }

            try
            {
                var expected = (await File.ReadAllTextAsync(digestPath, Encoding.UTF8).ConfigureAwait(false)).Trim();
                var expectedBytes = Convert.FromBase64String(expected);
                var actualBytes = await ComputeIntegrityDigestAsync(path).ConfigureAwait(false);

                if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
                {
                    await AuditTrail.RecordEventAsync("Audit", "DigestMismatch", new Dictionary<string, object>
                    {
                        ["file"] = path
                    }, "Critical");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError("Audit digest verification failed", nameof(AuditTrail), new Dictionary<string, object>
                {
                    ["file"] = path,
                    ["error"] = ex.Message
                }, ex);
                return false;
            }
        }

        private sealed class AuditEntry
        {
            public DateTime Timestamp { get; set; }
            public string Category { get; set; }
            public string Action { get; set; }
            public string Severity { get; set; }
            public string Actor { get; set; }
            public string Machine { get; set; }
            public IDictionary<string, object> Details { get; set; }
            public string Signature { get; set; }
        }

        private static string ComputeEntrySignature(AuditEntry entry)
        {
            EnsureIntegrityKeyLoadedAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            var payloadBuilder = new StringBuilder();
            payloadBuilder.Append(entry.Timestamp.ToString("O"));
            payloadBuilder.Append('|').Append(entry.Category ?? string.Empty);
            payloadBuilder.Append('|').Append(entry.Action ?? string.Empty);
            payloadBuilder.Append('|').Append(entry.Severity ?? string.Empty);
            payloadBuilder.Append('|').Append(entry.Actor ?? string.Empty);
            payloadBuilder.Append('|').Append(entry.Machine ?? string.Empty);
            payloadBuilder.Append('|').Append(SerializeDetails(entry.Details));

            using var hmac = new HMACSHA256(_integrityKey);
            var bytes = Encoding.UTF8.GetBytes(payloadBuilder.ToString());
            return Convert.ToBase64String(hmac.ComputeHash(bytes));
        }

        private static string SerializeDetails(IDictionary<string, object> details)
        {
            if (details == null || details.Count == 0)
            {
                return string.Empty;
            }

            var ordered = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var kvp in details)
            {
                ordered[kvp.Key ?? string.Empty] = kvp.Value;
            }

            return JsonSerializer.Serialize(ordered, new JsonSerializerOptions
            {
                WriteIndented = false
            });
        }

        private static async Task WriteIntegrityDigestAsync(string path)
        {
            try
            {
                var digest = await ComputeIntegrityDigestAsync(path).ConfigureAwait(false);
                var digestText = Convert.ToBase64String(digest);
                await File.WriteAllTextAsync(path + AuditDigestExtension, digestText, Encoding.UTF8).ConfigureAwait(false);
                await SecurityManager.EnsureSecureFileAclAsync(path + AuditDigestExtension).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await Logger.LogError("Audit digest write failed", nameof(AuditTrail), new Dictionary<string, object>
                {
                    ["file"] = path,
                    ["error"] = ex.Message
                }, ex).ConfigureAwait(false);
            }
        }

        private static async Task<byte[]> ComputeIntegrityDigestAsync(string path)
        {
            await EnsureIntegrityKeyLoadedAsync().ConfigureAwait(false);
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var hmac = new HMACSHA256(_integrityKey);
            return await hmac.ComputeHashAsync(stream).ConfigureAwait(false);
        }

        private static void DeleteDigest(string path)
        {
            try
            {
                var digestPath = path + AuditDigestExtension;
                if (File.Exists(digestPath))
                {
                    File.Delete(digestPath);
                }
            }
            catch
            {
            }
        }

        private static async Task EnsureIntegrityKeyLoadedAsync()
        {
            if (_integrityKey != null)
            {
                return;
            }

            var key = await SecurityManager.GetIntegrityKeyAsync(IntegrityKeyName).ConfigureAwait(false);
            Interlocked.CompareExchange(ref _integrityKey, key, null);
        }
    }
}
