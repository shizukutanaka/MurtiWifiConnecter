using System;
using System.Text.Json.Serialization;

namespace MurtiWifiConnecter.Core.Billing
{
    public enum BillingEdition
    {
        Free = 0,
        Professional = 1,
        Enterprise = 2
    }

    public enum BillingSource
    {
        Configuration,
        Stripe,
        Override,
        Webhook,
        Cached
    }

    public sealed class BillingState
    {
        public BillingEdition Edition { get; set; } = BillingEdition.Free;
        public BillingSource Source { get; set; } = BillingSource.Configuration;
        public string Status { get; set; } = "unknown";
        public string SubscriptionId { get; set; }
        public DateTimeOffset LastSyncedUtc { get; set; } = DateTimeOffset.MinValue;
        public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
        public bool InGracePeriod { get; set; }
        public int GraceDaysRemaining { get; set; }
        public string Notes { get; set; }
        public BillingOverride Override { get; set; }
    }

    public sealed class BillingOverride
    {
        public BillingEdition Edition { get; set; }
        public DateTimeOffset ExpiresUtc { get; set; }
        public string Reason { get; set; }
    }

    public sealed class BillingAccessResult
    {
        [JsonConstructor]
        public BillingAccessResult()
        {
        }

        public BillingAccessResult(bool allowed, BillingEdition requiredEdition, BillingEdition currentEdition, string reason = null)
        {
            Allowed = allowed;
            RequiredEdition = requiredEdition;
            CurrentEdition = currentEdition;
            Reason = reason;
        }

        public bool Allowed { get; set; }
        public BillingEdition RequiredEdition { get; set; }
        public BillingEdition CurrentEdition { get; set; }
        public string Reason { get; set; }
    }

    public sealed class StripeSyncResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }
        public BillingEdition Edition { get; set; } = BillingEdition.Free;
        public string Status { get; set; }
        public DateTimeOffset? CurrentPeriodEndUtc { get; set; }
        public bool InGracePeriod { get; set; }
        public int GraceDaysRemaining { get; set; }
        public string SubscriptionId { get; set; }
        public BillingSource Source { get; set; } = BillingSource.Stripe;
    }

    public sealed class BillingDiagnostics
    {
        public BillingState State { get; set; }
        public DateTimeOffset GeneratedUtc { get; set; }
        public string StripeApiKeyPresent { get; set; }
        public string WebhookSecretPresent { get; set; }
        public bool BillingEnabled { get; set; }
        public string ConfigDefaultEdition { get; set; }
    }
}
