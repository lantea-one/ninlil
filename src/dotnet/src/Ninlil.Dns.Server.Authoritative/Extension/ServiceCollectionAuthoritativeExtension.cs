using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Forwarding;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Ptr;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Authoritative.Update;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Extension;

/// <summary>
///     This class provides our <see cref="IServiceCollection" /> extension for wiring up
///     Ninlil's authoritative DNS engine: the in-memory zone cache, its
///     <see cref="IDnsQueryResolver" /> implementation, and the subscriber that keeps the cache
///     in sync with the database.
/// </summary>
public static class ServiceCollectionAuthoritativeExtension
{
    /// <summary>
    ///     This method registers everything <c>Ninlil.Dns.Server.Authoritative</c> needs. The
    ///     composition root is still expected to have already called
    ///     <c>Ninlil.Dns.Server.Core.Extension.ServiceCollectionServerCoreExtension.AddNinlilDnsServerCore</c>
    ///     -- and to register this resolver before any recursive resolver, so a hosted zone is
    ///     always answered authoritatively even when recursion is also enabled -- along with
    ///     whichever <see cref="Ninlil.Dns.Data.Abstractions.Repository.IZoneRepository" /> and
    ///     <see cref="Ninlil.Dns.Data.Abstractions.Repository.IRecordRepository" /> implementation
    ///     the application is configured to use, plus
    ///     <c>Ninlil.Dns.Server.Tsig.Extension.ServiceCollectionTsigExtension.AddNinlilDnsServerTsig</c>,
    ///     since <see cref="IZoneTransferService" /> depends on it.
    /// </summary>
    /// <param name="instance">The current <see cref="IServiceCollection" /> instance.</param>
    /// <param name="configuration">The application configuration to read authoritative settings from.</param>
    /// <returns>The modified <paramref name="instance" /> of <see cref="IServiceCollection" />.</returns>
    public static IServiceCollection AddNinlilDnsServerAuthoritative(
        this IServiceCollection instance,
        IConfiguration configuration
    )
    {
        instance.Configure<AuthoritativeServerOptions>(
            configuration.GetSection(AuthoritativeServerOptions.SectionName)
        );
        instance.Configure<ZoneTransferOptions>(
            configuration.GetSection(ZoneTransferOptions.SectionName)
        );
        instance.Configure<NotifyOptions>(configuration.GetSection(NotifyOptions.SectionName));
        instance.Configure<ZoneTransferClientOptions>(
            configuration.GetSection(ZoneTransferClientOptions.SectionName)
        );
        instance.Configure<SecondaryZoneRefreshOptions>(
            configuration.GetSection(SecondaryZoneRefreshOptions.SectionName)
        );
        instance.Configure<ZoneSigningOptions>(
            configuration.GetSection(ZoneSigningOptions.SectionName)
        );
        instance.Configure<DnssecKeyRolloverOptions>(
            configuration.GetSection(DnssecKeyRolloverOptions.SectionName)
        );
        instance.Configure<PtrSyncOptions>(configuration.GetSection(PtrSyncOptions.SectionName));
        instance.Configure<CatalogSyncOptions>(
            configuration.GetSection(CatalogSyncOptions.SectionName)
        );

        // AuthoritativeZoneCache is registered once, as a singleton, under both its concrete type
        // (which ZoneChangedEventSubscriber and AuthoritativeQueryResolver depend on directly)
        // and IHostedService (so the host starts it, triggering its initial full load).
        instance.AddSingleton<AuthoritativeZoneCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );

        // SecondaryZoneRefreshService is likewise registered once, as a singleton, under its
        // concrete type, IHostedService (so the host runs its poll loop), and
        // ISecondaryZoneRefreshTrigger (so NotifyQueryResolver can ask it to check a zone right
        // away, without depending on the whole background-service machinery itself).
        instance.AddSingleton<SecondaryZoneRefreshService>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<SecondaryZoneRefreshService>()
        );
        instance.AddSingleton<ISecondaryZoneRefreshTrigger>(provider =>
            provider.GetRequiredService<SecondaryZoneRefreshService>()
        );

        // NotifyQueryResolver is registered before AuthoritativeQueryResolver purely to signal
        // intent -- a NOTIFY's Opcode never matches an ordinary query, so the two can never
        // actually compete for the same request regardless of registration order.
        instance.AddSingleton<IDnsQueryResolver, NotifyQueryResolver>();

        // DynamicUpdateQueryResolver is registered singleton too, on the same terms as
        // NotifyQueryResolver above: its own scoped dependencies (TSIG, repositories,
        // IZoneUpdateService) are reached through IServiceScopeFactory per call, not through the
        // constructor, so nothing scoped ever gets captured by this singleton dispatcher chain.
        instance.AddSingleton<IDnsQueryResolver, DynamicUpdateQueryResolver>();
        instance.AddSingleton<IDnsQueryResolver, AuthoritativeQueryResolver>();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneChangedEventSubscriber
        >();

        // Scoped, rather than singleton, since it depends on ITsigService, which is itself
        // scoped (it depends on the scoped ITsigKeyRepository).
        instance.AddScoped<IZoneTransferService, ZoneTransferService>();

        // Three implementations, one per ZoneTransferTransportEnum value, all registered against
        // the same interface -- SecondaryZoneRefreshService resolves the full IEnumerable<T> of
        // them and selects the one whose own Transport matches a zone's own
        // ZoneEntity.TransferTransport, mirroring ForwarderQueryResolver's/
        // ForwarderHealthTracker's own _clientsByProtocol dictionary shape for upstream forwarder
        // clients.
        instance.AddScoped<IZoneTransferClient, TcpZoneTransferClient>();
        instance.AddScoped<IZoneTransferClient, TlsZoneTransferClient>();

        // QuicZoneTransferClient carries its own SupportedOSPlatform("linux"/"macos"/"windows")
        // scope (it uses System.Net.Quic.QuicConnection/QuicStream directly) -- exactly this
        // project's own three RuntimeIdentifiers families (Directory.Build.props), so registering
        // it here is safe on every platform this solution actually builds or ships for. Suppressed
        // locally, at this one real call site, rather than attributing the whole of this method
        // (which registers plenty of platform-agnostic pieces too) or every caller transitively.
#pragma warning disable CA1416
        instance.AddScoped<IZoneTransferClient, QuicZoneTransferClient>();
#pragma warning restore CA1416

        // Singleton -- every one of ZoneUpdateService's own per-zone locks must be shared across
        // every DI scope in the whole process (every HTTP request, every background dynamic-
        // update/transfer/cloud-sync/CAA-PTR task), or two concurrent changes to the same zone
        // from two different scopes would each get their own, mutually-oblivious lock instance
        // and never actually serialize against each other at all.
        instance.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();

        // Scoped for the same reason: it depends on the scoped repositories and unit of work
        // the persistence layer registers.
        instance.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        // INotifySender has no scoped dependencies of its own, but is registered scoped anyway
        // to stay consistent with everything else here.
        instance.AddScoped<INotifySender, UdpNotifySender>();

        // A second subscriber for the same event type as ZoneChangedEventSubscriber above --
        // IDnsServerEventSubscriber<DnsZoneChangedEvent> is resolved as a collection by
        // DnsServerEventDispatcher, so both run independently for every zone change.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneNotifyEventSubscriber
        >();

        // Scoped for the same reason as IZoneTransferService/IZoneUpdateService above: it
        // depends on the scoped repositories and IDnssecKeyManagementService.
        instance.AddScoped<IZoneSigningService, ZoneSigningService>();

        // SignedZoneCache follows the exact same triple-registration pattern as
        // AuthoritativeZoneCache above: a singleton, reached directly by
        // SignedZoneChangedEventSubscriber and DnssecResigningBackgroundService, and also
        // registered as IHostedService so the host starts it, triggering its initial full sign.
        instance.AddSingleton<SignedZoneCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<SignedZoneCache>()
        );

        // A third subscriber for DnsZoneChangedEvent, alongside the cache-reload and NOTIFY ones
        // above -- re-signing a changed zone is its own independent concern from either of those.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            SignedZoneChangedEventSubscriber
        >();

        // The proactive re-signing loop is a plain BackgroundService with no other interface any
        // other component needs to reach it through, unlike SecondaryZoneRefreshService's
        // trigger interface -- nothing ever needs to ask it to check a zone immediately.
        instance.AddHostedService<DnssecResigningBackgroundService>();

        // The RFC 6781 key-rollover walker is likewise a plain BackgroundService -- a rollover is
        // always started explicitly, through IDnssecKeyManagementService.StartRolloverAsync, never
        // by this service itself, so nothing needs a trigger interface to reach it through either.
        instance.AddHostedService<DnssecKeyRolloverBackgroundService>();

        // CloudCredentialProtector is a singleton -- IDataProtectionProvider itself is one, and
        // this wraps it with no other state. CloudDnsZoneSyncService reaches every scoped
        // repository it needs through IServiceScopeFactory per zone, per tick, the same pattern
        // SecondaryZoneRefreshService and TcpDnsListener's AXFR handling already use.
        instance.AddSingleton<CloudCredentialProtector>();
        instance.AddHttpClient<ICloudDnsProvider, CloudflareDnsProvider>(client =>
            client.BaseAddress = new Uri(CloudflareDnsProvider.DefaultBaseAddress)
        );
        instance.AddHttpClient<ICloudDnsProvider, DigitalOceanDnsProvider>(client =>
            client.BaseAddress = new Uri(DigitalOceanDnsProvider.DefaultBaseAddress)
        );
        instance.AddSingleton<ICloudDnsProvider, Route53DnsProvider>();
        instance.AddHostedService<CloudDnsZoneSyncService>();

        // PtrSyncService lives directly in this project -- unlike
        // CaaRecordReconciliationService, PtrSyncOptions has no cross-project placement
        // constraint forcing it elsewhere. Scoped for the same reason as IZoneUpdateService
        // above: it depends on the scoped repositories and unit of work the persistence layer
        // registers.
        instance.AddScoped<PtrSyncService>();

        // A fourth subscriber for DnsZoneChangedEvent, alongside cache-reload, NOTIFY, and
        // DNSSEC re-sign above -- syncing a changed zone's implied PTR records is its own
        // independent concern from any of those.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            PtrSyncEventSubscriber
        >();

        // ForwarderZoneContentSyncService reconciles a ZoneTypeEnum.Forwarder zone's own
        // synthetic apex SOA/TXT content -- Phase 16c's own real, versioned wire content for what
        // used to be a Forwarder zone's own record-free metadata shell. Scoped for the same
        // reason as IZoneUpdateService/PtrSyncService above.
        instance.AddScoped<ForwarderZoneContentSyncService>();

        // A sixth subscriber for DnsZoneChangedEvent, alongside cache-reload, NOTIFY, DNSSEC
        // re-sign, and PTR sync above -- syncing a Forwarder zone's own apex content is its own
        // independent concern from any of those.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ForwarderZoneContentSyncEventSubscriber
        >();

        // ClusterPeerAddressResolver is scoped -- it depends on the scoped
        // IClusterNodeRepository -- and is shared by CatalogZoneMembershipService,
        // CatalogZoneAutoProvisioningService, and CatalogClusterAddressSyncService (which reaches
        // it through its own per-round scope, the same way every other periodic hosted service
        // here reaches its own scoped dependencies).
        instance.AddScoped<ClusterPeerAddressResolver>();

        // CatalogZoneMembershipService is the RFC 9432 producer-side surface
        // ZonesController's new catalog-membership actions call directly; scoped for the same
        // reason as IZoneUpdateService/PtrSyncService above.
        instance.AddScoped<CatalogZoneMembershipService>();

        // CatalogZoneAutoProvisioningService is the consumer-side counterpart, reached only
        // through its own event subscriber below -- never directly by a controller.
        instance.AddScoped<CatalogZoneAutoProvisioningService>();

        // A fifth subscriber for DnsZoneChangedEvent, alongside cache-reload, NOTIFY, DNSSEC
        // re-sign, and PTR sync above -- auto-provisioning a SecondaryCatalog zone's own member
        // zones is its own independent concern from any of those.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            CatalogZoneAutoProvisioningEventSubscriber
        >();

        // The periodic address-reconciliation loop is a plain BackgroundService with no other
        // interface any other component needs to reach it through, the same shape
        // DnssecResigningBackgroundService already follows.
        instance.AddHostedService<CatalogClusterAddressSyncService>();

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
