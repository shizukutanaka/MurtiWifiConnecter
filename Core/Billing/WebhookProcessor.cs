using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Stripe;

namespace MurtiWifiConnecter.Core.Billing
{
    /// <summary>
    /// Processes Stripe webhook events for subscription lifecycle management.
    /// </summary>
    internal static class WebhookProcessor
    {
        /// <summary>
        /// Process a Stripe webhook event.
        /// </summary>
        public static async Task ProcessEventAsync(string payload, string signature)
        {
            // Verify signature and construct event
            var stripeEvent = await StripeClient.ConstructWebhookEventAsync(payload, signature);

            await Logger.LogInfo("Webhook received", "WebhookProcessor", new Dictionary<string, object>
            {
                ["eventType"] = stripeEvent.Type,
                ["eventId"] = stripeEvent.Id
            });

            // Route to appropriate handler based on event type
            switch (stripeEvent.Type)
            {
                // Subscription created (new purchase)
                case Events.CustomerSubscriptionCreated:
                    await HandleSubscriptionCreatedAsync(stripeEvent);
                    break;

                // Subscription updated (plan change, renewal)
                case Events.CustomerSubscriptionUpdated:
                    await HandleSubscriptionUpdatedAsync(stripeEvent);
                    break;

                // Subscription deleted (cancellation)
                case Events.CustomerSubscriptionDeleted:
                    await HandleSubscriptionDeletedAsync(stripeEvent);
                    break;

                // Payment succeeded
                case Events.InvoicePaymentSucceeded:
                    await HandlePaymentSucceededAsync(stripeEvent);
                    break;

                // Payment failed
                case Events.InvoicePaymentFailed:
                    await HandlePaymentFailedAsync(stripeEvent);
                    break;

                // Checkout session completed
                case Events.CheckoutSessionCompleted:
                    await HandleCheckoutCompletedAsync(stripeEvent);
                    break;

                default:
                    await Logger.LogInfo("Unhandled webhook event", "WebhookProcessor", new Dictionary<string, object>
                    {
                        ["eventType"] = stripeEvent.Type
                    });
                    break;
            }

            await AuditTrail.LogEventAsync("WebhookProcessed", new Dictionary<string, object>
            {
                ["eventType"] = stripeEvent.Type,
                ["eventId"] = stripeEvent.Id,
                ["timestamp"] = stripeEvent.Created.ToString("O")
            });
        }

        private static async Task HandleSubscriptionCreatedAsync(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            if (subscription == null) return;

            var edition = ParseEdition(subscription.Metadata);
            var periodEnd = subscription.CurrentPeriodEnd;

            await SubscriptionManager.HandleSubscriptionActivatedAsync(
                subscription.Id,
                edition,
                periodEnd
            );
        }

        private static async Task HandleSubscriptionUpdatedAsync(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            if (subscription == null) return;

            // Check if status changed to past_due
            if (subscription.Status == "past_due")
            {
                await SubscriptionManager.HandleSubscriptionPastDueAsync(subscription.Id);
                return;
            }

            // Check if edition changed
            var edition = ParseEdition(subscription.Metadata);
            var periodEnd = subscription.CurrentPeriodEnd;

            await SubscriptionManager.HandleSubscriptionUpdatedAsync(
                subscription.Id,
                edition,
                periodEnd
            );
        }

        private static async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
        {
            var subscription = stripeEvent.Data.Object as Subscription;
            if (subscription == null) return;

            await SubscriptionManager.HandleSubscriptionCancelledAsync(
                subscription.Id,
                subscription.CanceledAt
            );
        }

        private static async Task HandlePaymentSucceededAsync(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice == null || invoice.SubscriptionId == null) return;

            // Payment succeeded, renew subscription
            var periodEnd = invoice.PeriodEnd;
            await SubscriptionManager.HandleSubscriptionRenewedAsync(
                invoice.SubscriptionId,
                periodEnd
            );
        }

        private static async Task HandlePaymentFailedAsync(Event stripeEvent)
        {
            var invoice = stripeEvent.Data.Object as Invoice;
            if (invoice == null || invoice.SubscriptionId == null) return;

            // Payment failed, subscription will go to past_due
            await SubscriptionManager.HandleSubscriptionPastDueAsync(invoice.SubscriptionId);
        }

        private static async Task HandleCheckoutCompletedAsync(Event stripeEvent)
        {
            var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
            if (session == null) return;

            await Logger.LogInfo("Checkout completed", "WebhookProcessor", new Dictionary<string, object>
            {
                ["sessionId"] = session.Id,
                ["customerId"] = session.CustomerId,
                ["subscriptionId"] = session.SubscriptionId
            });

            // Subscription creation will be handled by CustomerSubscriptionCreated event
        }

        private static BillingEdition ParseEdition(IDictionary<string, string> metadata)
        {
            if (metadata == null || !metadata.TryGetValue("edition", out var editionStr))
            {
                return BillingEdition.Free;
            }

            if (Enum.TryParse<BillingEdition>(editionStr, true, out var edition))
            {
                return edition;
            }

            return BillingEdition.Free;
        }
    }
}
