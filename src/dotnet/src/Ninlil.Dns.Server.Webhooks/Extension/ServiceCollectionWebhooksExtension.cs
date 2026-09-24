using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's outbound subscribable webhooks feature: <see cref="WebhookCredentialProtector" />,
///     the actual HTTP delivery engine (<see cref="WebhookDeliveryService" />, its own dedicated
///     <see cref="HttpClient" />), <see cref="WebhookDeliveryRetrySweepService" /> (the persisted
///     retry/dead-letter/retention sweep every delivery beyond its own first attempt runs
///     through), and <see cref="WebhookDeliverySubscriber{TEvent}" /> closed once per
///     webhook-eligible event type.
/// </summary>
public static class ServiceCollectionWebhooksExtension
{
    /// <summary>
    ///     This method registers <see cref="WebhookCredentialProtector" />, the outbound delivery
    ///     engine, the retry/retention sweep hosted service, and every webhook-eligible event
    ///     type's own <see cref="IDnsServerEventSubscriber{TEvent}" /> registration. The
    ///     composition root is still expected to have already registered an
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookSubscriptionRepository" />/
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookDeliveryAttemptRepository" />/
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IUnitOfWork" /> implementation.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The configuration <see cref="WebhookDeliveryOptions" /> binds from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerWebhooks(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Data Protection backs the encryption of a subscription's own auth credentials and HMAC
        // signing secret at rest, the same mechanism every other secret-bearing entity in this
        // project already uses. Safe to call more than once -- Ninlil.Web's own
        // Program.cs already relies on that being true for its own explicit AddDataProtection()
        // call.
        instance.AddDataProtection();

        instance.AddSingleton<WebhookCredentialProtector>();

        instance.Configure<WebhookDeliveryOptions>(
            configuration.GetSection(WebhookDeliveryOptions.SectionName)
        );

        // A single, named HttpClient every delivery attempt (for every subscription) is created
        // from via IHttpClientFactory -- the same "typed/named client via AddHttpClient, not a
        // hand-constructed HttpClient" convention Server.Authoritative's own CloudSync connectors
        // already establish. The client's own request timeout is read from WebhookDeliveryOptions
        // at the point IHttpClientFactory actually builds a fresh handler for this named client
        // (roughly every two minutes, per that factory's own default handler lifetime), via a
        // real IOptionsMonitor<WebhookDeliveryOptions> rather than the plain, snapshotted
        // IOptions<WebhookDeliveryOptions> -- the latter's own .Value is computed once and cached
        // for the life of the process, so it would never actually let a configuration change
        // reach a freshly built handler at all.
        instance
            .AddHttpClient(WebhookDeliveryService.HttpClientName)
            .ConfigureHttpClient(
                (provider, client) =>
                {
                    WebhookDeliveryOptions deliveryOptions = provider
                        .GetRequiredService<IOptionsMonitor<WebhookDeliveryOptions>>()
                        .CurrentValue;
                    client.Timeout = deliveryOptions.RequestTimeout;
                }
            );

        instance.AddScoped<WebhookDeliveryService>();

        // The sole mechanism that ever performs a delivery's second or later attempt -- see this
        // service's own doc comment for the full restart-durability reasoning.
        instance.AddHostedService<WebhookDeliveryRetrySweepService>();

        // One closed-generic IDnsServerEventSubscriber<TEvent> registration per webhook-eligible
        // event type -- DnsServerEventDispatcher resolves every subscriber registered for an
        // event's own exact concrete runtime type via GetServices, so registering the same
        // WebhookDeliverySubscriber<TEvent> open generic closed over each of these 7 types is
        // sufficient; no per-event-type subclass is needed.
        instance.AddSingleton<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            WebhookDeliverySubscriber<DnsZoneChangedEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<DnsCacheEvictedEvent>,
            WebhookDeliverySubscriber<DnsCacheEvictedEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<DnssecZoneResigningCompletedEvent>,
            WebhookDeliverySubscriber<DnssecZoneResigningCompletedEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<AcmeCertificateRenewalSucceededEvent>,
            WebhookDeliverySubscriber<AcmeCertificateRenewalSucceededEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<AcmeCertificateRenewalFailedEvent>,
            WebhookDeliverySubscriber<AcmeCertificateRenewalFailedEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<CloudDnsZoneSyncFailedEvent>,
            WebhookDeliverySubscriber<CloudDnsZoneSyncFailedEvent>
        >();
        instance.AddSingleton<
            IDnsServerEventSubscriber<TrustAnchorRolloverEvent>,
            WebhookDeliverySubscriber<TrustAnchorRolloverEvent>
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
