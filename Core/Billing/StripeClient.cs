using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stripe;
using Stripe.Checkout;

namespace MurtiWifiConnecter.Core.Billing
{
    /// <summary>
    /// Stripe API integration layer for subscription management.
    /// </summary>
    internal static class StripeClient
    {
        private static bool _initialized;
        private static string _apiKey;
        private static readonly object _lock = new();

        // Stripe Product Price IDs (configure these in Stripe Dashboard)
        private static readonly Dictionary<BillingEdition, string> PriceIds = new()
        {
            [BillingEdition.Professional] = "price_professional_monthly", // Replace with actual Stripe Price ID
            [BillingEdition.Enterprise] = "price_enterprise_monthly"      // Replace with actual Stripe Price ID
        };

        public static async Task InitializeAsync()
        {
            lock (_lock)
            {
                if (_initialized) return;

                // Priority order for API key resolution:
                // 1. Environment variable (MURTI_STRIPE_API_KEY)
                // 2. Configuration file setting
                // 3. Secure credential manager
                _apiKey = GetStripeApiKey();

                if (string.IsNullOrEmpty(_apiKey))
                {
                    throw new InvalidOperationException(
                        "Stripe API key not configured. Set environment variable 'MURTI_STRIPE_API_KEY' or 'Billing.Stripe.ApiKey' in config.");
                }

                // Validate API key format (should start with sk_ for secret keys)
                if (!_apiKey.StartsWith("sk_") && !_apiKey.StartsWith("pk_"))
                {
                    await Logger.LogWarning("Stripe API key format may be invalid", "StripeClient", new Dictionary<string, object>
                    {
                        ["keyPrefix"] = _apiKey.Substring(0, Math.Min(10, _apiKey.Length))
                    });
                }

                StripeConfiguration.ApiKey = _apiKey;
                _initialized = true;
            }

            await Logger.LogInfo("Stripe client initialized", "StripeClient", new Dictionary<string, object>
            {
                ["apiKeySource"] = GetApiKeySource(),
                ["apiKeyLength"] = _apiKey.Length
            });
        }

        private static string GetStripeApiKey()
        {
            // 1. Check environment variable first
            var envKey = Environment.GetEnvironmentVariable("MURTI_STRIPE_API_KEY");
            if (!string.IsNullOrEmpty(envKey))
            {
                return envKey;
            }

            // 2. Check configuration file
            var config = ConfigManager.GetConfigAsync().GetAwaiter().GetResult();
            var configKey = config.GetStripeApiKey();
            if (!string.IsNullOrEmpty(configKey))
            {
                return configKey;
            }

            // 3. Check Windows Credential Manager (for enterprise deployments)
            if (CredentialManager.IsSupported)
            {
                var target = "MurtiWifiConnecter_StripeKey";
                if (CredentialManager.TryReadCredential(target, out var username, out var password, out var error))
                {
                    return password;
                }
            }

            return string.Empty;
        }

        private static string GetApiKeySource()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MURTI_STRIPE_API_KEY")))
            {
                return "EnvironmentVariable";
            }

            var config = ConfigManager.GetConfigAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(config.GetStripeApiKey()))
            {
                return "ConfigurationFile";
            }

            if (CredentialManager.IsSupported)
            {
                var target = "MurtiWifiConnecter_StripeKey";
                if (CredentialManager.TryReadCredential(target, out _, out _, out _))
                {
                    return "CredentialManager";
                }
            }

            return "NotFound";
        }

        /// <summary>
        /// Verify Stripe webhook signature for security
        /// </summary>
        public static async Task<Event> ConstructWebhookEventAsync(string payload, string signature)
        {
            EnsureInitialized();

            try
            {
                // Get webhook secret from environment variable or config
                var webhookSecret = GetWebhookSecret();

                if (string.IsNullOrEmpty(webhookSecret))
                {
                    await Logger.LogWarning("Webhook secret not configured", "StripeClient");
                    throw new InvalidOperationException("Webhook secret not configured");
                }

                // Verify signature using Stripe's webhook verification
                var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);

                await Logger.LogInfo("Webhook signature verified", "StripeClient", new Dictionary<string, object>
                {
                    ["eventType"] = stripeEvent.Type,
                    ["eventId"] = stripeEvent.Id
                });

                return stripeEvent;
            }
            catch (StripeException ex)
            {
                await Logger.LogError($"Webhook signature verification failed: {ex.Message}", "StripeClient", new Dictionary<string, object>
                {
                    ["signature"] = signature.Substring(0, Math.Min(20, signature.Length)),
                    ["error"] = ex.Message
                });
                throw new SecurityException("Webhook signature verification failed", ex);
            }
        }

        private static string GetWebhookSecret()
        {
            // 1. Check environment variable first (recommended for production)
            var envSecret = Environment.GetEnvironmentVariable("MURTI_STRIPE_WEBHOOK_SECRET");
            if (!string.IsNullOrEmpty(envSecret))
            {
                return envSecret;
            }

            // 2. Check configuration file
            var config = ConfigManager.GetConfigAsync().GetAwaiter().GetResult();
            var configSecret = config.GetStripeWebhookSecret();
            if (!string.IsNullOrEmpty(configSecret))
            {
                return configSecret;
            }

            // 3. Check Windows Credential Manager
            if (CredentialManager.IsSupported)
            {
                var target = "MurtiWifiConnecter_WebhookSecret";
                if (CredentialManager.TryReadCredential(target, out var username, out var secret, out var error))
                {
                    return secret;
                }
            }

            return string.Empty;
        }
        public static async Task<string> CreateCheckoutSessionAsync(string customerId, BillingEdition targetEdition)
        {
            EnsureInitialized();

            if (!PriceIds.TryGetValue(targetEdition, out var priceId))
            {
                throw new ArgumentException($"No price configured for {targetEdition} edition");
            }

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new()
                    {
                        Price = priceId,
                        Quantity = 1
                    }
                },
                SuccessUrl = "https://yourdomain.com/billing/success?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = "https://yourdomain.com/billing/cancel",
                ClientReferenceId = customerId,
                Metadata = new Dictionary<string, string>
                {
                    ["machine_id"] = customerId,
                    ["edition"] = targetEdition.ToString(),
                    ["product"] = "MurtiWifiConnecter"
                },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        ["machine_id"] = customerId,
                        ["edition"] = targetEdition.ToString()
                    }
                },
                PaymentMethodTypes = new List<string> { "card" }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            await Logger.LogInfo("Checkout session created", "StripeClient", new Dictionary<string, object>
            {
                ["sessionId"] = session.Id,
                ["edition"] = targetEdition.ToString(),
                ["customerId"] = customerId
            });

            await AuditTrail.LogEventAsync("CheckoutSessionCreated", new Dictionary<string, object>
            {
                ["edition"] = targetEdition.ToString(),
                ["sessionId"] = session.Id
            });

            return session.Url;
        }

        /// <summary>
        /// Create a Stripe Customer Portal session for subscription management.
        /// </summary>
        public static async Task<string> CreatePortalSessionAsync(string customerId)
        {
            EnsureInitialized();

            // Find customer by metadata
            var customerService = new CustomerService();
            var searchOptions = new CustomerSearchOptions
            {
                Query = $"metadata['machine_id']:'{customerId}'"
            };

            var customers = await customerService.SearchAsync(searchOptions);
            var customer = customers.Data.FirstOrDefault();

            if (customer == null)
            {
                throw new InvalidOperationException("No Stripe customer found for this installation");
            }

            var options = new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customer.Id,
                ReturnUrl = "https://yourdomain.com/billing"
            };

            var service = new Stripe.BillingPortal.SessionService();
            var session = await service.CreateAsync(options);

            await Logger.LogInfo("Portal session created", "StripeClient", new Dictionary<string, object>
            {
                ["customerId"] = customer.Id,
                ["sessionId"] = session.Id
            });

            return session.Url;
        }

        /// <summary>
        /// Get current subscription status from Stripe API.
        /// </summary>
        public static async Task<StripeSyncResult> GetSubscriptionStatusAsync(string machineId)
        {
            EnsureInitialized();

            try
            {
                // Search for customer by machine_id metadata
                var customerService = new CustomerService();
                var searchOptions = new CustomerSearchOptions
                {
                    Query = $"metadata['machine_id']:'{machineId}'"
                };

                var customers = await customerService.SearchAsync(searchOptions);
                var customer = customers.Data.FirstOrDefault();

                if (customer == null)
                {
                    // No customer found, user hasn't subscribed
                    return new StripeSyncResult
                    {
                        Success = true,
                        Edition = BillingEdition.Free,
                        Status = "no_subscription",
                        Source = BillingSource.Stripe
                    };
                }

                // Get active subscriptions
                var subscriptionService = new SubscriptionService();
                var subscriptionOptions = new SubscriptionListOptions
                {
                    Customer = customer.Id,
                    Status = "all",
                    Limit = 10
                };

                var subscriptions = await subscriptionService.ListAsync(subscriptionOptions);
                var activeSubscription = subscriptions.Data
                    .Where(s => s.Status == "active" || s.Status == "trialing" || s.Status == "past_due")
                    .OrderByDescending(s => s.Created)
                    .FirstOrDefault();

                if (activeSubscription == null)
                {
                    // Customer exists but no active subscription
                    return new StripeSyncResult
                    {
                        Success = true,
                        Edition = BillingEdition.Free,
                        Status = "subscription_ended",
                        Source = BillingSource.Stripe
                    };
                }

                // Parse edition from subscription metadata
                var edition = BillingEdition.Free;
                if (activeSubscription.Metadata.TryGetValue("edition", out var editionStr))
                {
                    Enum.TryParse<BillingEdition>(editionStr, true, out edition);
                }

                // Check for grace period (past_due status)
                var inGracePeriod = activeSubscription.Status == "past_due";
                var graceDaysRemaining = 0;

                if (inGracePeriod && activeSubscription.CurrentPeriodEnd.HasValue)
                {
                    var gracePeriodEnd = activeSubscription.CurrentPeriodEnd.Value.AddDays(7); // 7 day grace period
                    graceDaysRemaining = Math.Max(0, (int)(gracePeriodEnd - DateTime.UtcNow).TotalDays);
                }

                return new StripeSyncResult
                {
                    Success = true,
                    Edition = edition,
                    Status = activeSubscription.Status,
                    SubscriptionId = activeSubscription.Id,
                    CurrentPeriodEndUtc = activeSubscription.CurrentPeriodEnd,
                    InGracePeriod = inGracePeriod,
                    GraceDaysRemaining = graceDaysRemaining,
                    Source = BillingSource.Stripe
                };
            }
            catch (StripeException ex)
            {
                await Logger.LogError("Stripe API error", "StripeClient", ex, new Dictionary<string, object>
                {
                    ["statusCode"] = ex.HttpStatusCode,
                    ["stripeCode"] = ex.StripeError?.Code
                });

                return new StripeSyncResult
                {
                    Success = false,
                    Error = $"Stripe API error: {ex.StripeError?.Message ?? ex.Message}",
                    Edition = BillingEdition.Free
                };
            }
        }

        /// <summary>
        /// Verify webhook signature and parse event.
        /// </summary>
        public static async Task<Event> ConstructWebhookEventAsync(string payload, string signature)
        {
            EnsureInitialized();

            var config = await ConfigManager.GetConfigAsync();
            var webhookSecret = config.GetStripeWebhookSecret();

            if (string.IsNullOrEmpty(webhookSecret))
            {
                throw new InvalidOperationException("Stripe webhook secret not configured");
            }

            try
            {
                var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
                return stripeEvent;
            }
            catch (StripeException ex)
            {
                await Logger.LogError("Webhook signature verification failed", "StripeClient", ex, new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                });
                throw;
            }
        }

        private static void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException("StripeClient not initialized. Call InitializeAsync first.");
            }
        }
    }
}
