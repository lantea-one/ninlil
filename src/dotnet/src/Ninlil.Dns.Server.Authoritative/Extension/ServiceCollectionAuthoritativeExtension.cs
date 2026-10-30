using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
///     This class provides the <see cref="IServiceCollection" /> extension that registers the
///     authoritative engine: the zone and signed-zone caches, the NOTIFY, dynamic update and
///     authoritative resolvers, zone transfer (serving and pulling), secondary refresh, DNSSEC
///     signing and rollover, cloud sync, PTR sync, forwarder zone content and catalog zones.
/// </summary>
public static class ServiceCollectionAuthoritativeExtension
{
    /// <summary>
    ///     This method registers everything <c>Ninlil.Dns.Server.Authoritative</c> needs. The caller
    ///     must also call <c>AddNinlilDnsServerCore</c>, <c>AddNinlilDnsServerTsig</c> and
    ///     <c>AddNinlilDnsServerDnssec</c>, register the repositories, and call this before registering
    ///     the forwarding and recursive resolvers, so hosted zones are answered authoritatively first.
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

        // One AuthoritativeZoneCache instance, used directly by the subscriber and resolver and
        // started by the host as an IHostedService (which loads every zone).
        instance.AddSingleton<AuthoritativeZoneCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );

        // The expiry clock. TryAdd keeps a clock another extension or a test registered.
        instance.TryAddSingleton(TimeProvider.System);

        // Which secondary zones have expired (RFC 1034 §4.3.5): filled by the refresh service,
        // read by the authoritative resolver and the zone transfer service.
        instance.AddSingleton<SecondaryZoneExpiryTracker>();

        // One SecondaryZoneRefreshService instance: the concrete type, the hosted service that runs
        // the poll loop, and ISecondaryZoneRefreshTrigger for NotifyQueryResolver.
        instance.AddSingleton<SecondaryZoneRefreshService>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<SecondaryZoneRefreshService>()
        );
        instance.AddSingleton<ISecondaryZoneRefreshTrigger>(provider =>
            provider.GetRequiredService<SecondaryZoneRefreshService>()
        );

        // The resolvers run in this order. NOTIFY and UPDATE messages have their own opcodes, so
        // their resolvers never take ordinary queries.
        instance.AddSingleton<IDnsQueryResolver, NotifyQueryResolver>();

        // The resolvers are singletons; DynamicUpdateQueryResolver reaches its scoped dependencies
        // (TSIG, repositories, IZoneUpdateService) through IServiceScopeFactory per call.
        instance.AddSingleton<IDnsQueryResolver, DynamicUpdateQueryResolver>();
        instance.AddSingleton<IDnsQueryResolver, AuthoritativeQueryResolver>();
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneChangedEventSubscriber
        >();

        // Scoped, because ITsigService is scoped (through ITsigKeyRepository).
        instance.AddScoped<IZoneTransferService, ZoneTransferService>();

        // One IZoneTransferClient per ZoneTransferTransportEnum value; SecondaryZoneRefreshService
        // picks the one matching ZoneEntity.TransferTransport.
        instance.AddScoped<IZoneTransferClient, TcpZoneTransferClient>();
        instance.AddScoped<IZoneTransferClient, TlsZoneTransferClient>();

        // QuicZoneTransferClient is marked for Linux, macOS and Windows (System.Net.Quic), the only
        // runtime families this solution builds for (Directory.Build.props), so the platform warning
        // is suppressed here rather than on the whole method.
#pragma warning disable CA1416
        instance.AddScoped<IZoneTransferClient, QuicZoneTransferClient>();
#pragma warning restore CA1416

        // Singleton: every scope (requests and background tasks) must share the same per-zone locks,
        // or changes from different scopes would not be serialized.
        instance.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();

        // Scoped, because it uses the scoped repositories and unit of work.
        instance.AddScoped<IZoneUpdateService, ZoneUpdateService>();

        // INotifySender has no scoped dependencies but is scoped like the rest.
        instance.AddScoped<INotifySender, UdpNotifySender>();

        // DnsZoneChangedEvent has several subscribers; the dispatcher calls them in registration
        // order.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ZoneNotifyEventSubscriber
        >();

        // Scoped, because it uses the scoped repositories and IDnssecKeyManagementService.
        instance.AddScoped<IZoneSigningService, ZoneSigningService>();

        // One SignedZoneCache instance, used directly by its subscriber and the DNSSEC background
        // services, and started by the host (which signs or loads every signed zone).
        instance.AddSingleton<SignedZoneCache>();
        instance.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<SignedZoneCache>()
        );

        // Re-sign a zone when it changes.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            SignedZoneChangedEventSubscriber
        >();

        // Re-signs zones before their signatures expire.
        instance.AddHostedService<DnssecResigningBackgroundService>();

        // Advances RFC 6781 key rollovers; an operator starts them through
        // IDnssecKeyManagementService.StartRolloverAsync.
        instance.AddHostedService<DnssecKeyRolloverBackgroundService>();

        // CloudCredentialProtector only wraps the singleton IDataProtectionProvider. The HTTP
        // providers get their base addresses here. CloudDnsZoneSyncService creates a scope per zone.
        instance.AddSingleton<CloudCredentialProtector>();
        instance.AddHttpClient<ICloudDnsProvider, CloudflareDnsProvider>(client =>
            client.BaseAddress = new Uri(CloudflareDnsProvider.DefaultBaseAddress)
        );
        instance.AddHttpClient<ICloudDnsProvider, DigitalOceanDnsProvider>(client =>
            client.BaseAddress = new Uri(DigitalOceanDnsProvider.DefaultBaseAddress)
        );
        instance.AddSingleton<ICloudDnsProvider, Route53DnsProvider>();

        // One instance is both the hosted loop and the on-demand trigger (ICloudZoneSyncTrigger),
        // so a manual push and a loop pass share one lock and never overlap.
        instance.AddSingleton<CloudDnsZoneSyncService>();
        instance.AddSingleton<ICloudZoneSyncTrigger>(provider =>
            provider.GetRequiredService<CloudDnsZoneSyncService>()
        );
        instance.AddHostedService(provider =>
            provider.GetRequiredService<CloudDnsZoneSyncService>()
        );

        // Scoped, because it uses the scoped repositories and unit of work.
        instance.AddScoped<PtrSyncService>();

        // Sync PTR records when a zone changes.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            PtrSyncEventSubscriber
        >();

        // Keeps a Forwarder zone's synthetic apex SOA and TXT records in step with its
        // ForwarderAddresses. Scoped, like the other services using repositories.
        instance.AddScoped<ForwarderZoneContentSyncService>();

        // Sync a Forwarder zone's apex records when the zone changes.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            ForwarderZoneContentSyncEventSubscriber
        >();

        // Scoped, because IClusterNodeRepository is. Used by the catalog membership,
        // auto-provisioning and address sync services (the last through its per-round scope).
        instance.AddScoped<ClusterPeerAddressResolver>();

        // The RFC 9432 producer, called by ZonesController's catalog membership actions.
        instance.AddScoped<CatalogZoneMembershipService>();

        // The RFC 9432 consumer, used only through its event subscriber.
        instance.AddScoped<CatalogZoneAutoProvisioningService>();

        // Provision catalog member zones when a SecondaryCatalog zone changes.
        instance.AddScoped<
            IDnsServerEventSubscriber<DnsZoneChangedEvent>,
            CatalogZoneAutoProvisioningEventSubscriber
        >();

        // Keeps catalog zones' address lists in step with the cluster peers.
        instance.AddHostedService<CatalogClusterAddressSyncService>();

        return instance;
    }
}
