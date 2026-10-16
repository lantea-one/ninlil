using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Acme.Caa;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's ACME certificate issuance and renewal.
/// </summary>
public static class ServiceCollectionAcmeExtension
{
    /// <summary>
    ///     This method registers the ACME account store, the DNS-01 challenge responder, the
    ///     issuer, the DNS-01 propagation waiter, the renewal service and the CAA reconciliation service and subscriber. The
    ///     composition root must also call
    ///     <c>Ninlil.Dns.Server.Authoritative.Extension.ServiceCollectionAuthoritativeExtension.AddNinlilDnsServerAuthoritative</c>,
    ///     since <see cref="AcmeDnsChallengeResponder" /> depends on
    ///     <c>AuthoritativeZoneCache</c> and <c>IZoneUpdateService</c>.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read ACME settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerAcme(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<AcmeOptions>(configuration.GetSection(AcmeOptions.SectionName));

        // The CAA auto-management defaults, used by CaaRecordReconciliationService.
        instance.Configure<CaaAutoManagementOptions>(
            configuration.GetSection(CaaAutoManagementOptions.SectionName)
        );

        // A singleton, because every renewal check must share one account identity.
        instance.AddSingleton<IAcmeAccountStore, FileAcmeAccountStore>();

        // The probe and the resolver hold no state. The clock is the waiter's timer source, so
        // tests can replace it.
        instance.AddSingleton<IAcmeChallengeProbe, UdpAcmeChallengeProbe>();
        instance.AddSingleton<
            IAcmeNameServerAddressResolver,
            SystemAcmeNameServerAddressResolver
        >();
        instance.TryAddSingleton(TimeProvider.System);

        // Scoped, because they depend on the scoped repositories and IZoneUpdateService.
        instance.AddScoped<AcmeDnsChallengeResponder>();
        instance.AddScoped<AcmeChallengePropagationWaiter>();
        instance.AddScoped<AcmeCertificateIssuer>();

        // Registered as its concrete type and, through the factory below, as IHostedService, so
        // AcmeSettingsController can resolve the running instance to call RenewOneDomainAsync.
        instance.AddSingleton<AcmeCertificateRenewalService>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AcmeCertificateRenewalService>()
        );

        // Scoped, like the services above (see the class summary for why it lives here).
        instance.AddScoped<CaaRecordReconciliationService>();

        // Another DnsZoneChangedEvent subscriber besides those Server.Authoritative registers
        // (cache reload, NOTIFY, DNSSEC re-sign); the dispatcher calls every registered one.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            CaaRecordReconciliationEventSubscriber
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
