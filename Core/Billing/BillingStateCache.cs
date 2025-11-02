using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core.Billing;

namespace MurtiWifiConnecter.Core.Billing
{
    internal static class BillingStateCache
    {
        private static readonly SemaphoreSlim CacheLock = new(1, 1);
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MurtiWifiConnecter",
            "billing");
        private static readonly string CachePath = Path.Combine(CacheDirectory, "state.json");
        private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(1);

        private static BillingState _state;
        private static DateTimeOffset _lastLoadUtc = DateTimeOffset.MinValue;

        public static async Task<BillingState> GetAsync(TimeSpan? ttl = null)
        {
            await CacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var effectiveTtl = ttl ?? DefaultTtl;
                if (_state != null && DateTimeOffset.UtcNow - _lastLoadUtc <= effectiveTtl)
                {
                    return _state;
                }

                if (!File.Exists(CachePath))
                {
                    _state = new BillingState();
                    _lastLoadUtc = DateTimeOffset.UtcNow;
                    return _state;
                }

                try
                {
                    var json = await File.ReadAllTextAsync(CachePath).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        _state = JsonSerializer.Deserialize<BillingState>(json, Serializer.Options) ?? new BillingState();
                    }
                    else
                    {
                        _state = new BillingState();
                    }
                }
                catch
                {
                    _state = new BillingState();
                }

                _lastLoadUtc = DateTimeOffset.UtcNow;
                return _state;
            }
            finally
            {
                CacheLock.Release();
            }
        }

        public static async Task SetAsync(BillingState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            await CacheLock.WaitAsync().ConfigureAwait(false);
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                await SecurityManager.EnsureSecureDirectoryAclAsync(CacheDirectory).ConfigureAwait(false);

                var json = JsonSerializer.Serialize(state, Serializer.Options);
                await File.WriteAllTextAsync(CachePath, json).ConfigureAwait(false);
                await SecurityManager.EnsureSecureFileAclAsync(CachePath).ConfigureAwait(false);

                _state = state;
                _lastLoadUtc = DateTimeOffset.UtcNow;
            }
            finally
            {
                CacheLock.Release();
            }
        }

        private static class Serializer
        {
            public static readonly JsonSerializerOptions Options = new()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };
        }
    }
}
