using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MurtiWifiConnecter.Core.Billing
{
    /// <summary>
    /// Manages subscription lifecycle events and state transitions.
    /// </summary>
    internal static class SubscriptionManager
    {
        /// <summary>
        /// Handle subscription activation (new purchase or reactivation).
        /// </summary>
        public static async Task HandleSubscriptionActivatedAsync(string subscriptionId, BillingEdition edition, DateTimeOffset periodEnd)
        {
            var state = new BillingState
            {
                Edition = edition,
                Source = BillingSource.Webhook,
                Status = "active",
                SubscriptionId = subscriptionId,
                LastSyncedUtc = DateTimeOffset.UtcNow,
                CurrentPeriodEndUtc = periodEnd,
                InGracePeriod = false,
                GraceDaysRemaining = 0,
                Notes = "Subscription activated via webhook"
            };

            await BillingStateCache.SetAsync(state);

            await Logger.LogInfo("Subscription activated", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["edition"] = edition.ToString(),
                ["periodEnd"] = periodEnd.ToString("O")
            });

            await AuditTrail.LogEventAsync("SubscriptionActivated", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["edition"] = edition.ToString(),
                ["periodEnd"] = periodEnd.ToString("O")
            });
        }

        /// <summary>
        /// Handle subscription renewal.
        /// </summary>
        public static async Task HandleSubscriptionRenewedAsync(string subscriptionId, DateTimeOffset newPeriodEnd)
        {
            var state = await BillingStateCache.GetAsync();
            state.LastSyncedUtc = DateTimeOffset.UtcNow;
            state.CurrentPeriodEndUtc = newPeriodEnd;
            state.InGracePeriod = false;
            state.GraceDaysRemaining = 0;
            state.Status = "active";
            state.Source = BillingSource.Webhook;

            await BillingStateCache.SetAsync(state);

            await Logger.LogInfo("Subscription renewed", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["newPeriodEnd"] = newPeriodEnd.ToString("O")
            });

            await AuditTrail.LogEventAsync("SubscriptionRenewed", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["periodEnd"] = newPeriodEnd.ToString("O")
            });
        }

        /// <summary>
        /// Handle subscription cancellation.
        /// </summary>
        public static async Task HandleSubscriptionCancelledAsync(string subscriptionId, DateTimeOffset? periodEnd)
        {
            var state = await BillingStateCache.GetAsync();
            state.Status = "canceled";
            state.LastSyncedUtc = DateTimeOffset.UtcNow;
            state.Source = BillingSource.Webhook;

            // If period end is in future, continue access until then
            if (periodEnd.HasValue && periodEnd.Value > DateTimeOffset.UtcNow)
            {
                state.CurrentPeriodEndUtc = periodEnd.Value;
                state.Notes = "Subscription canceled, access continues until period end";
            }
            else
            {
                // Immediate downgrade to Free
                state.Edition = BillingEdition.Free;
                state.CurrentPeriodEndUtc = null;
                state.Notes = "Subscription canceled, downgraded to Free";
            }

            await BillingStateCache.SetAsync(state);

            await Logger.LogInfo("Subscription canceled", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["periodEnd"] = periodEnd?.ToString("O") ?? "null",
                ["immediateDowngrade"] = !periodEnd.HasValue || periodEnd.Value <= DateTimeOffset.UtcNow
            });

            await AuditTrail.LogEventAsync("SubscriptionCanceled", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["accessUntil"] = periodEnd?.ToString("O") ?? "immediate"
            });
        }

        /// <summary>
        /// Handle subscription going into past_due status (payment failed).
        /// </summary>
        public static async Task HandleSubscriptionPastDueAsync(string subscriptionId)
        {
            var state = await BillingStateCache.GetAsync();
            state.Status = "past_due";
            state.LastSyncedUtc = DateTimeOffset.UtcNow;
            state.InGracePeriod = true;
            state.Source = BillingSource.Webhook;

            // Calculate grace period (7 days from current period end)
            if (state.CurrentPeriodEndUtc.HasValue)
            {
                var gracePeriodEnd = state.CurrentPeriodEndUtc.Value.AddDays(7);
                state.GraceDaysRemaining = Math.Max(0, (int)(gracePeriodEnd - DateTimeOffset.UtcNow).TotalDays);
            }
            else
            {
                state.GraceDaysRemaining = 7;
            }

            state.Notes = "Payment failed, grace period active";

            await BillingStateCache.SetAsync(state);

            await Logger.LogWarning("Subscription past due", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["graceDaysRemaining"] = state.GraceDaysRemaining
            });

            await AuditTrail.LogEventAsync("SubscriptionPastDue", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["graceDaysRemaining"] = state.GraceDaysRemaining
            });
        }

        /// <summary>
        /// Handle subscription edition change (upgrade/downgrade).
        /// </summary>
        public static async Task HandleSubscriptionUpdatedAsync(string subscriptionId, BillingEdition newEdition, DateTimeOffset? newPeriodEnd)
        {
            var state = await BillingStateCache.GetAsync();
            var oldEdition = state.Edition;

            state.Edition = newEdition;
            state.LastSyncedUtc = DateTimeOffset.UtcNow;
            state.Source = BillingSource.Webhook;

            if (newPeriodEnd.HasValue)
            {
                state.CurrentPeriodEndUtc = newPeriodEnd;
            }

            var changeType = newEdition > oldEdition ? "upgrade" : "downgrade";
            state.Notes = $"Subscription {changeType}d from {oldEdition} to {newEdition}";

            await BillingStateCache.SetAsync(state);

            await Logger.LogInfo("Subscription updated", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["oldEdition"] = oldEdition.ToString(),
                ["newEdition"] = newEdition.ToString(),
                ["changeType"] = changeType
            });

            await AuditTrail.LogEventAsync("SubscriptionUpdated", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["from"] = oldEdition.ToString(),
                ["to"] = newEdition.ToString(),
                ["type"] = changeType
            });
        }

        /// <summary>
        /// Handle subscription expiration (grace period ended, no payment).
        /// </summary>
        public static async Task HandleSubscriptionExpiredAsync(string subscriptionId)
        {
            var state = await BillingStateCache.GetAsync();
            state.Edition = BillingEdition.Free;
            state.Status = "expired";
            state.LastSyncedUtc = DateTimeOffset.UtcNow;
            state.InGracePeriod = false;
            state.GraceDaysRemaining = 0;
            state.CurrentPeriodEndUtc = null;
            state.Source = BillingSource.Webhook;
            state.Notes = "Subscription expired, downgraded to Free";

            await BillingStateCache.SetAsync(state);

            await Logger.LogWarning("Subscription expired", "SubscriptionManager", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId
            });

            await AuditTrail.LogEventAsync("SubscriptionExpired", new Dictionary<string, object>
            {
                ["subscriptionId"] = subscriptionId,
                ["downgradedTo"] = "Free"
            });
        }

        /// <summary>
        /// Check if subscription needs renewal warning.
        /// </summary>
        public static async Task<(bool shouldWarn, int daysRemaining)> CheckRenewalWarningAsync()
        {
            var state = await BillingStateCache.GetAsync();

            if (!state.CurrentPeriodEndUtc.HasValue || state.Edition == BillingEdition.Free)
            {
                return (false, 0);
            }

            var daysRemaining = (int)(state.CurrentPeriodEndUtc.Value - DateTimeOffset.UtcNow).TotalDays;

            // Warn if less than 7 days remaining
            var shouldWarn = daysRemaining <= 7 && daysRemaining > 0;

            return (shouldWarn, daysRemaining);
        }

        /// <summary>
        /// Get subscription health status.
        /// </summary>
        public static async Task<SubscriptionHealth> GetHealthAsync()
        {
            var state = await BillingStateCache.GetAsync();

            var health = new SubscriptionHealth
            {
                Edition = state.Edition,
                Status = state.Status,
                IsHealthy = state.Status == "active" && !state.InGracePeriod,
                InGracePeriod = state.InGracePeriod,
                GraceDaysRemaining = state.GraceDaysRemaining,
                DaysUntilRenewal = 0,
                Warnings = new List<string>()
            };

            // Calculate days until renewal
            if (state.CurrentPeriodEndUtc.HasValue)
            {
                health.DaysUntilRenewal = (int)(state.CurrentPeriodEndUtc.Value - DateTimeOffset.UtcNow).TotalDays;

                if (health.DaysUntilRenewal <= 3 && health.DaysUntilRenewal > 0)
                {
                    health.Warnings.Add($"Subscription renews in {health.DaysUntilRenewal} days");
                }
            }

            // Add warnings
            if (state.InGracePeriod)
            {
                health.Warnings.Add($"Payment failed, {state.GraceDaysRemaining} days remaining in grace period");
            }

            if (state.Status == "canceled")
            {
                health.Warnings.Add("Subscription is canceled and will not renew");
            }

            return health;
        }

        public sealed class SubscriptionHealth
        {
            public BillingEdition Edition { get; set; }
            public string Status { get; set; }
            public bool IsHealthy { get; set; }
            public bool InGracePeriod { get; set; }
            public int GraceDaysRemaining { get; set; }
            public int DaysUntilRenewal { get; set; }
            public List<string> Warnings { get; set; }
        }
    }
}
