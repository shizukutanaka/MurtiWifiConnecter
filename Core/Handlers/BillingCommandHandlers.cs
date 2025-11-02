using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MurtiWifiConnecter.Core.Billing;

namespace MurtiWifiConnecter.Core.Handlers
{
    /// <summary>
    /// Command handlers for billing and subscription management.
    /// </summary>
    public static class BillingCommandHandlers
    {
        /// <summary>
        /// Main billing command - shows status and options.
        /// Usage: billing [status|upgrade|manage|sync|features]
        /// </summary>
        public static async Task<int> ExecuteBilling(string[] args)
        {
            if (args.Length < 2)
            {
                return await ShowBillingStatusAsync();
            }

            var subcommand = args[1].ToLowerInvariant();

            return subcommand switch
            {
                "status" => await ShowBillingStatusAsync(),
                "upgrade" => await ShowUpgradeOptionsAsync(),
                "manage" => await OpenCustomerPortalAsync(),
                "sync" => await SyncSubscriptionAsync(),
                "features" => await ShowFeaturesAsync(),
                "diagnostics" or "diag" => await ShowDiagnosticsAsync(),
                _ => await ShowBillingHelpAsync(subcommand)
            };
        }

        /// <summary>
        /// Show current billing status and subscription details.
        /// </summary>
        private static async Task<int> ShowBillingStatusAsync()
        {
            var state = await BillingManager.GetStateAsync();
            var health = await SubscriptionManager.GetHealthAsync();

            UIHelper.PrintSection("Billing Status");
            Console.WriteLine();

            // Edition and status
            Console.WriteLine($"Edition:     {GetEditionDisplay(state.Edition)}");
            Console.WriteLine($"Status:      {GetStatusDisplay(state.Status, health.IsHealthy)}");
            Console.WriteLine($"Source:      {state.Source}");
            Console.WriteLine();

            // Subscription details
            if (!string.IsNullOrEmpty(state.SubscriptionId))
            {
                Console.WriteLine("Subscription Details:");
                Console.WriteLine($"  ID:              {state.SubscriptionId}");

                if (state.CurrentPeriodEndUtc.HasValue)
                {
                    Console.WriteLine($"  Renews:          {state.CurrentPeriodEndUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
                    Console.WriteLine($"  Days remaining:  {health.DaysUntilRenewal}");
                }

                Console.WriteLine($"  Last synced:     {state.LastSyncedUtc.ToLocalTime():yyyy-MM-dd HH:mm}");
                Console.WriteLine();
            }

            // Warnings
            if (health.Warnings.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠ Warnings:");
                foreach (var warning in health.Warnings)
                {
                    Console.WriteLine($"  • {warning}");
                }
                Console.ResetColor();
                Console.WriteLine();
            }

            // Feature limits
            var limits = FeatureGate.GetLimits(state.Edition);
            Console.WriteLine("Feature Limits:");
            Console.WriteLine($"  Max networks:        {(limits.MaxPreferredNetworks == int.MaxValue ? "Unlimited" : limits.MaxPreferredNetworks.ToString())}");
            Console.WriteLine($"  Log retention:       {limits.MaxLogRetentionDays} days");
            Console.WriteLine($"  Automation:          {(limits.AllowAutomation ? "Enabled" : "Disabled")}");
            Console.WriteLine($"  Advanced security:   {(limits.AllowAdvancedSecurity ? "Enabled" : "Disabled")}");
            Console.WriteLine();

            // Actions
            if (state.Edition == BillingEdition.Free)
            {
                Console.WriteLine("To unlock more features, run: billing upgrade");
            }
            else
            {
                Console.WriteLine("To manage your subscription, run: billing manage");
            }

            return 0;
        }

        /// <summary>
        /// Show upgrade options and pricing.
        /// </summary>
        private static async Task<int> ShowUpgradeOptionsAsync()
        {
            var state = await BillingManager.GetStateAsync();

            UIHelper.PrintSection("Upgrade Options");
            Console.WriteLine();

            // Professional tier
            if (state.Edition < BillingEdition.Professional)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Professional Edition - ¥1,980/month");
                Console.ResetColor();
                Console.WriteLine();

                var benefits = FeatureGate.GetUpgradeBenefits(state.Edition, BillingEdition.Professional);
                Console.WriteLine("Benefits:");
                foreach (var capability in benefits.NewCapabilities)
                {
                    Console.WriteLine($"  ✓ {capability}");
                }
                Console.WriteLine($"  ✓ Network limit: {benefits.NetworkLimitIncrease}");
                Console.WriteLine($"  ✓ Log retention: {benefits.LogRetentionIncrease}");
                Console.WriteLine();
            }

            // Enterprise tier
            if (state.Edition < BillingEdition.Enterprise)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Enterprise Edition - ¥9,800/month");
                Console.ResetColor();
                Console.WriteLine();

                var benefits = FeatureGate.GetUpgradeBenefits(state.Edition, BillingEdition.Enterprise);
                Console.WriteLine("Benefits:");
                foreach (var capability in benefits.NewCapabilities)
                {
                    Console.WriteLine($"  ✓ {capability}");
                }
                Console.WriteLine($"  ✓ Network limit: {benefits.NetworkLimitIncrease}");
                Console.WriteLine($"  ✓ Log retention: {benefits.LogRetentionIncrease}");
                Console.WriteLine();
            }

            // Purchase instructions
            if (state.Edition < BillingEdition.Enterprise)
            {
                Console.WriteLine("To purchase:");
                Console.WriteLine("  1. Run: billing upgrade professional");
                Console.WriteLine("  2. Run: billing upgrade enterprise");
                Console.WriteLine();
                Console.WriteLine("You will receive a secure checkout URL from Stripe.");
            }
            else
            {
                Console.WriteLine("You're already on the highest tier!");
            }

            return 0;
        }

        /// <summary>
        /// Create checkout session for upgrade.
        /// Usage: billing upgrade [professional|enterprise]
        /// </summary>
        public static async Task<int> ExecuteUpgrade(string[] args)
        {
            if (args.Length < 2)
            {
                UIHelper.PrintError("Please specify edition: professional or enterprise");
                return 1;
            }

            var targetEditionStr = args[1];
            if (!Enum.TryParse<BillingEdition>(targetEditionStr, true, out var targetEdition))
            {
                UIHelper.PrintError($"Invalid edition: {targetEditionStr}");
                Console.WriteLine("Valid options: professional, enterprise");
                return 1;
            }

            if (targetEdition == BillingEdition.Free)
            {
                UIHelper.PrintError("Cannot upgrade to Free edition");
                return 1;
            }

            var state = await BillingManager.GetStateAsync();
            if (state.Edition >= targetEdition)
            {
                UIHelper.PrintInfo($"You already have {state.Edition} edition");
                return 0;
            }

            try
            {
                Console.WriteLine($"Creating checkout session for {targetEdition} edition...");
                var checkoutUrl = await BillingManager.CreateCheckoutSessionAsync(targetEdition);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Checkout session created!");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Please open this URL in your browser to complete payment:");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(checkoutUrl);
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("After payment, run 'billing sync' to activate your subscription.");

                return 0;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"Failed to create checkout session: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Open Stripe Customer Portal for subscription management.
        /// </summary>
        private static async Task<int> OpenCustomerPortalAsync()
        {
            try
            {
                Console.WriteLine("Creating customer portal session...");
                var portalUrl = await BillingManager.CreatePortalSessionAsync();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Portal session created!");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("Please open this URL in your browser to manage your subscription:");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(portalUrl);
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine("You can cancel, update payment method, or view invoices.");

                return 0;
            }
            catch (InvalidOperationException ex)
            {
                UIHelper.PrintError(ex.Message);
                return 1;
            }
            catch (Exception ex)
            {
                UIHelper.PrintError($"Failed to create portal session: {ex.Message}");
                return 1;
            }
        }

        /// <summary>
        /// Sync subscription status with Stripe.
        /// </summary>
        private static async Task<int> SyncSubscriptionAsync()
        {
            Console.WriteLine("Syncing with Stripe...");

            var result = await BillingManager.SyncWithStripeAsync();

            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Sync successful");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine($"Edition:  {result.Edition}");
                Console.WriteLine($"Status:   {result.Status}");

                if (result.CurrentPeriodEndUtc.HasValue)
                {
                    Console.WriteLine($"Renews:   {result.CurrentPeriodEndUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}");
                }

                return 0;
            }
            else
            {
                UIHelper.PrintError($"Sync failed: {result.Error}");
                return 1;
            }
        }

        /// <summary>
        /// Show available and locked features.
        /// </summary>
        private static async Task<int> ShowFeaturesAsync()
        {
            var state = await BillingManager.GetStateAsync();

            UIHelper.PrintSection($"Features ({state.Edition} Edition)");
            Console.WriteLine();

            var availableFeatures = FeatureGate.GetAvailableFeatures(state.Edition);
            var lockedFeatures = FeatureGate.GetLockedFeatures(state.Edition);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Available Features:");
            Console.ResetColor();
            foreach (var feature in availableFeatures.Take(20))
            {
                Console.WriteLine($"  ✓ {feature}");
            }

            if (availableFeatures.Count > 20)
            {
                Console.WriteLine($"  ... and {availableFeatures.Count - 20} more");
            }

            Console.WriteLine();

            if (lockedFeatures.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Locked Features:");
                Console.ResetColor();
                foreach (var feature in lockedFeatures.Take(15))
                {
                    var required = FeatureGate.GetRequiredEdition(feature);
                    Console.WriteLine($"  ✗ {feature} (requires {required})");
                }

                if (lockedFeatures.Count > 15)
                {
                    Console.WriteLine($"  ... and {lockedFeatures.Count - 15} more");
                }

                Console.WriteLine();
                Console.WriteLine("To unlock these features, run: billing upgrade");
            }

            return 0;
        }

        /// <summary>
        /// Show billing diagnostics.
        /// </summary>
        private static async Task<int> ShowDiagnosticsAsync()
        {
            var diag = await BillingManager.GetDiagnosticsAsync();

            UIHelper.PrintSection("Billing Diagnostics");
            Console.WriteLine();

            Console.WriteLine("Configuration:");
            Console.WriteLine($"  Billing enabled:      {diag.BillingEnabled}");
            Console.WriteLine($"  Default edition:      {diag.ConfigDefaultEdition}");
            Console.WriteLine($"  Stripe API key:       {diag.StripeApiKeyPresent}");
            Console.WriteLine($"  Webhook secret:       {diag.WebhookSecretPresent}");
            Console.WriteLine();

            Console.WriteLine("Current State:");
            Console.WriteLine($"  Edition:              {diag.State.Edition}");
            Console.WriteLine($"  Status:               {diag.State.Status}");
            Console.WriteLine($"  Source:               {diag.State.Source}");
            Console.WriteLine($"  Last synced:          {diag.State.LastSyncedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();

            return 0;
        }

        /// <summary>
        /// Show billing help.
        /// </summary>
        private static async Task<int> ShowBillingHelpAsync(string invalidSubcommand = null)
        {
            if (!string.IsNullOrEmpty(invalidSubcommand))
            {
                UIHelper.PrintError($"Unknown billing command: {invalidSubcommand}");
                Console.WriteLine();
            }

            UIHelper.PrintSection("Billing Commands");
            Console.WriteLine();

            Console.WriteLine("billing status          Show current subscription status");
            Console.WriteLine("billing upgrade         Show upgrade options and pricing");
            Console.WriteLine("billing manage          Open customer portal to manage subscription");
            Console.WriteLine("billing sync            Sync subscription status with Stripe");
            Console.WriteLine("billing features        Show available and locked features");
            Console.WriteLine("billing diagnostics     Show billing system diagnostics");
            Console.WriteLine();

            return invalidSubcommand == null ? 0 : 1;
        }

        private static string GetEditionDisplay(BillingEdition edition)
        {
            return edition switch
            {
                BillingEdition.Free => "Free",
                BillingEdition.Professional => "Professional (¥1,980/mo)",
                BillingEdition.Enterprise => "Enterprise (¥9,800/mo)",
                _ => edition.ToString()
            };
        }

        private static string GetStatusDisplay(string status, bool healthy)
        {
            var color = healthy ? ConsoleColor.Green : ConsoleColor.Yellow;
            var statusText = status switch
            {
                "active" => "✓ Active",
                "trialing" => "✓ Trial",
                "past_due" => "⚠ Payment Required",
                "canceled" => "✗ Canceled",
                "expired" => "✗ Expired",
                "no_subscription" => "○ No Subscription",
                _ => status
            };

            Console.ForegroundColor = color;
            var result = statusText;
            Console.ResetColor();
            return result;
        }
    }
}
