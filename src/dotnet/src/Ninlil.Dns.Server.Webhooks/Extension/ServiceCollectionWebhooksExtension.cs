using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's outbound webhooks: <see cref="WebhookCredentialProtector" />, the delivery engine
///     (<see cref="WebhookDeliveryService" /> and its named <see cref="HttpClient" />),
///     <see cref="WebhookDeliveryRetrySweepService" /> (retries, dead-lettering and retention),
///     and <see cref="WebhookDeliverySubscriber{TEvent}" /> for each webhook event type.
/// </summary>
public static class ServiceCollectionWebhooksExtension
{
    /// <summary>
    ///     This method registers <see cref="WebhookCredentialProtector" />, the outbound delivery
    ///     engine, the retry sweep hosted service, and an
    ///     <see cref="IDnsServerEventSubscriber{TEvent}" /> for each webhook event type. The
    ///     composition root must also register
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookSubscriptionRepository" />,
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IWebhookDeliveryAttemptRepository" /> and
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IUnitOfWork" />.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The configuration <see cref="WebhookDeliveryOptions" /> binds from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerWebhooks(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        // Data Protection encrypts the subscription credentials and signing secret at rest.
        // AddDataProtection is safe to call more than once; Ninlil.Web's Program.cs also calls
        // it.
        instance.AddDataProtection();

        instance.AddSingleton<WebhookCredentialProtector>();

        instance.Configure<WebhookDeliveryOptions>(
            configuration.GetSection(WebhookDeliveryOptions.SectionName)
        );

        // One named HttpClient for every delivery attempt. IHttpClientFactory runs this callback
        // for each client it creates, and it reads the timeout through IOptionsMonitor rather
        // than IOptions (whose Value is cached for the process lifetime), so a changed
        // RequestTimeout applies to the next attempt.
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

        // Makes every retry; see WebhookDeliveryRetrySweepService.
        instance.AddHostedService<WebhookDeliveryRetrySweepService>();

        // One WebhookDeliverySubscriber<TEvent> per webhook event type. DnsServerEventDispatcher
        // resolves subscribers for an event's exact runtime type, so the generic class closed
        // over each type is enough. These seven registrations must match
        // WebhookEventCatalog.EventTypes, which decides what a subscription may name.
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
