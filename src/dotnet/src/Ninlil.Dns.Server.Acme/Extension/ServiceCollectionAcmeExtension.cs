using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    ///     This method registers the ACME account store, DNS-01 challenge responder, issuer,
    ///     and renewal background service. The composition root is still expected to have
    ///     already called
    ///     <c>Ninlil.Dns.Server.Authoritative.Extension.ServiceCollectionAuthoritativeExtension.AddNinlilDnsServerAuthoritative</c>,
    ///     since <see cref="AcmeDnsChallengeResponder" /> depends on
    ///     <c>AuthoritativeZoneCache</c>/<c>IZoneUpdateService</c>.
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

        // The global-default settings for CAA auto-management, read and acted on by
        // CaaRecordReconciliationService below.
        instance.Configure<CaaAutoManagementOptions>(
            configuration.GetSection(CaaAutoManagementOptions.SectionName)
        );

        // IAcmeAccountStore is a singleton -- its whole point (a durable, continuously reused
        // account identity) only makes sense shared across every renewal check, never scoped to
        // one, the same reasoning Server.DnsCrypt's own IDnsCryptCertificateStore already uses.
        instance.AddSingleton<IAcmeAccountStore, FileAcmeAccountStore>();

        // Scoped, since both depend (directly or transitively) on the scoped repositories and
        // IZoneUpdateService the persistence and Server.Authoritative layers register.
        instance.AddScoped<AcmeDnsChallengeResponder>();
        instance.AddScoped<AcmeCertificateIssuer>();

        // Registered both as its own concrete singleton type and (via the forwarding factory
        // below) as IHostedService, the same double-registration
        // ServiceCollectionAuthoritativeExtension.AddNinlilDnsServerAuthoritative already
        // establishes for AuthoritativeZoneCache -- so a caller with a genuine, real need to
        // reach this exact running instance directly (AcmeSettingsController's own manual
        // "issue/renew now" REST action, which drives AcmeCertificateRenewalService.RenewOneDomainAsync
        // outside its usual poll loop) can resolve it by its own concrete type, not only through
        // the generic IHostedService collection the generic host itself starts/stops.
        instance.AddSingleton<AcmeCertificateRenewalService>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AcmeCertificateRenewalService>()
        );

        // CaaRecordReconciliationService lives here, not Server.Authoritative, specifically
        // because CaaAutoManagementOptions (just above) already does -- see that class's own
        // doc comment for why putting it in Server.Authoritative instead would require an
        // actual circular project reference.
        // Scoped for the same reason as IZoneUpdateService/AcmeDnsChallengeResponder above.
        instance.AddScoped<CaaRecordReconciliationService>();

        // A second subscriber for DnsZoneChangedEvent, alongside every one
        // Server.Authoritative's own extension already registers (cache reload, NOTIFY,
        // DNSSEC re-sign) -- IDnsServerEventSubscriber<DnsZoneChangedEvent> is resolved as a
        // collection by DnsServerEventDispatcher, so this runs independently of every other one.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            CaaRecordReconciliationEventSubscriber
        >();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
