using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Billing
{
    /// <summary>
    /// Central billing and subscription management system.
    /// Coordinates license validation, Stripe integration, and feature gating.
    /// </summary>
    public static class BillingManager
    {
        private static bool _initialized;
        private static readonly object _lock = new();

        public static async Task InitializeAsync()
        {
            lock (_lock)
            {
                if (_initialized) return;
                _initialized = true;
            }

            await Logger.LogInfo("Billing system initializing", "BillingManager", new Dictionary<string, object>
            {
                ["enabled"] = await IsBillingEnabledAsync()
            });

            // Initialize Stripe client if enabled
            if (await IsBillingEnabledAsync())
            {
                await StripeClient.InitializeAsync();
            }
        }

        /// <summary>
        /// Get current billing state from cache or config.
        /// </summary>
        public static async Task<BillingState> GetStateAsync(bool forceRefresh = false)
        {
            if (!await IsBillingEnabledAsync())
            {
                var config = await ConfigManager.GetConfigAsync();
                return new BillingState
                {
                    Edition = ParseEdition(config.GetDefaultBillingEdition()),
                    Source = BillingSource.Configuration,
                    Status = "billing_disabled",
                    Notes = "Billing enforcement is disabled in configuration"
                };
            }

            if (forceRefresh)
            {
                var syncResult = await SyncWithStripeAsync();
                if (syncResult.Success)
                {
                    var state = new BillingState
                    {
                        Edition = syncResult.Edition,
                        Source = syncResult.Source,
                        Status = syncResult.Status,
                        SubscriptionId = syncResult.SubscriptionId,
                        LastSyncedUtc = DateTimeOffset.UtcNow,
                        CurrentPeriodEndUtc = syncResult.CurrentPeriodEndUtc,
                        InGracePeriod = syncResult.InGracePeriod,
                        GraceDaysRemaining = syncResult.GraceDaysRemaining
                    };
                    await BillingStateCache.SetAsync(state);
                    return state;
                }
            }

            return await BillingStateCache.GetAsync();
        }

        /// <summary>
        /// Sync subscription state with Stripe API.
        /// </summary>
        public static async Task<StripeSyncResult> SyncWithStripeAsync()
        {
            if (!await IsBillingEnabledAsync())
            {
                return new StripeSyncResult
                {
                    Success = false,
                    Error = "Billing is disabled"
                };
            }

            try
            {
                var machineId = await GetMachineIdentifierAsync();
                return await StripeClient.GetSubscriptionStatusAsync(machineId);
            }
            catch (Exception ex)
            {
                await Logger.LogError("Stripe sync failed", "BillingManager", ex, new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });

                return new StripeSyncResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        /// <summary>
        /// Check if a feature is available for current billing edition.
        /// </summary>
        public static async Task<BillingAccessResult> CheckFeatureAccessAsync(string featureName)
        {
            var requiredEdition = FeatureGate.GetRequiredEdition(featureName);
            var state = await GetStateAsync();

            var allowed = state.Edition >= requiredEdition;

            return new BillingAccessResult(
                allowed: allowed,
                requiredEdition: requiredEdition,
                currentEdition: state.Edition,
                reason: allowed ? null : $"This feature requires {requiredEdition} edition or higher"
            );
        }

        /// <summary>
        /// Create a Stripe Checkout session for subscription purchase.
        /// </summary>
        public static async Task<string> CreateCheckoutSessionAsync(BillingEdition targetEdition)
        {
            if (!await IsBillingEnabledAsync())
            {
                throw new InvalidOperationException("Billing is not enabled");
            }

            if (targetEdition == BillingEdition.Free)
            {
                throw new ArgumentException("Cannot create checkout for Free edition");
            }

            var machineId = await GetMachineIdentifierAsync();
            return await StripeClient.CreateCheckoutSessionAsync(machineId, targetEdition);
        }

        /// <summary>
        /// Create a Stripe Customer Portal session for subscription management.
        /// </summary>
        public static async Task<string> CreatePortalSessionAsync()
        {
            if (!await IsBillingEnabledAsync())
            {
                throw new InvalidOperationException("Billing is not enabled");
            }

            var state = await GetStateAsync();
            if (string.IsNullOrEmpty(state.SubscriptionId))
            {
                throw new InvalidOperationException("No active subscription found");
            }

            var machineId = await GetMachineIdentifierAsync();
            return await StripeClient.CreatePortalSessionAsync(machineId);
        }

        /// <summary>
        /// Process a Stripe webhook event.
        /// </summary>
        public static async Task<bool> ProcessWebhookAsync(string payload, string signature)
        {
            if (!await IsBillingEnabledAsync())
            {
                return false;
            }

            try
            {
                await WebhookProcessor.ProcessEventAsync(payload, signature);
                return true;
            }
            catch (Exception ex)
            {
                await Logger.LogError("Webhook processing failed", "BillingManager", ex, new Dictionary<string, object>
                {
                    ["signature"] = signature?.Substring(0, Math.Min(20, signature?.Length ?? 0))
                });
                return false;
            }
        }

        /// <summary>
        /// Get billing diagnostics for troubleshooting.
        /// </summary>
        public static async Task<BillingDiagnostics> GetDiagnosticsAsync()
        {
            var state = await GetStateAsync();
            var config = await ConfigManager.GetConfigAsync();

            return new BillingDiagnostics
            {
                State = state,
                GeneratedUtc = DateTimeOffset.UtcNow,
                StripeApiKeyPresent = !string.IsNullOrEmpty(config.GetStripeApiKey()) ? "Yes (redacted)" : "No",
                WebhookSecretPresent = !string.IsNullOrEmpty(config.GetStripeWebhookSecret()) ? "Yes (redacted)" : "No",
                BillingEnabled = config.GetBillingEnabled(),
                ConfigDefaultEdition = config.GetDefaultBillingEdition()
            };
        }

        /// <summary>
        /// Apply a temporary billing override (for testing, grace period, etc).
        /// </summary>
        public static async Task ApplyOverrideAsync(BillingEdition edition, TimeSpan duration, string reason)
        {
            var state = await GetStateAsync();
            state.Override = new BillingOverride
            {
                Edition = edition,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(duration),
                Reason = reason
            };

            await BillingStateCache.SetAsync(state);
            await AuditTrail.LogEventAsync("BillingOverrideApplied", new Dictionary<string, object>
            {
                ["edition"] = edition.ToString(),
                ["duration"] = duration.ToString(),
                ["reason"] = reason
            });
        }

        /// <summary>
        /// Remove any active billing override.
        /// </summary>
        public static async Task ClearOverrideAsync()
        {
            var state = await GetStateAsync();
            state.Override = null;
            await BillingStateCache.SetAsync(state);

            await AuditTrail.LogEventAsync("BillingOverrideCleared", new Dictionary<string, object>());
        }

        private static async Task<bool> IsBillingEnabledAsync()
        {
            var config = await ConfigManager.GetConfigAsync();
            return config.GetBillingEnabled();
        }

        private static async Task<string> GetMachineIdentifierAsync()
        {
            // Use a stable machine identifier (hardware ID + installation path hash)
            var machineId = Environment.MachineName;
            var installPath = AppContext.BaseDirectory;
            var composite = $"{machineId}:{installPath}";

            // Hash to create a stable, privacy-preserving identifier
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(composite));
            return Convert.ToBase64String(hashBytes).Substring(0, 32);
        }

        private static BillingEdition ParseEdition(string value)
        {
            if (Enum.TryParse<BillingEdition>(value, true, out var edition))
            {
                return edition;
            }
            return BillingEdition.Free;
        }
    }
}
