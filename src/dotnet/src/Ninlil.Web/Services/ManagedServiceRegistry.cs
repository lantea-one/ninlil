using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.CloudSync;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Services;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Forwarding;
using Ninlil.Dns.Server.QueryLog;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Dns.Server.Webhooks;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class implements <see cref="IManagedServiceRegistry" />: it wraps each DNS transport
///     listener and background maintenance loop the host runs in a named, categorized
///     <see cref="ManagedServiceRegistration" />. <see cref="Build" /> creates it once, and it is
///     held as a singleton.
///     <para>
///         <b>The wrappers hold the running instances.</b> Every wrapped service is a singleton,
///         registered either with <c>AddHostedService&lt;T&gt;()</c> or, when another component
///         needs it too (<see cref="DnsQueryProcessingPipeline" />,
///         <see cref="AuthoritativeZoneCache" />, <see cref="SignedZoneCache" />,
///         <see cref="SecondaryZoneRefreshService" />, <see cref="ForwarderHealthTracker" />),
///         with <c>AddSingleton&lt;T&gt;()</c> plus an <see cref="IHostedService" /> registration
///         that returns the same instance. Resolving them from the root provider, as
///         <see cref="Build" /> does and as <c>TransportListenersHealthCheck</c> does, returns the
///         objects the generic host starts and stops. So when <c>ServicesController</c> calls
///         <see cref="ManagedServiceRegistration.StopAsync" />, it stops the instance that is
///         serving traffic, not a copy.
///     </para>
///     <para>
///         <b>Seeded as running.</b> <c>Program.cs</c> registers <see cref="Build" /> as a
///         singleton factory, so it runs the first time the registry is resolved, which is after
///         the host has started. By then the host has called <c>StartAsync</c> on every wrapped
///         service once, so each registration without its own running probe is created with
///         <c>isAlreadyRunning: true</c>. Calling <see cref="ManagedServiceRegistration.StartAsync" />
///         again instead would start them twice, which fails for a bound socket.
///     </para>
///     <para>
///         <b>DNS-over-HTTPS wraps a gate, not a hosted service.</b> <c>Program.cs</c> does not
///         register <see cref="DnsOverHttpsHostedService" />, which would run a second Kestrel;
///         DoH is mapped onto the shared Kestrel instead. The entry wraps
///         <see cref="DnsOverHttpsTransportGate" />, which
///         <c>EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps</c> opens after
///         mapping the routes, giving DoH the same start and stop as the socket transports (see
///         that class for why a gate rather than unmapping routes).
///     </para>
///     <para>
///         <b>CAA and PTR sync are not listed.</b> <c>CaaRecordReconciliationService</c> and
///         <c>PtrSyncService</c> run synchronously from
///         <c>CaaRecordReconciliationEventSubscriber</c> and <c>PtrSyncEventSubscriber</c> when a
///         <c>DnsZoneChangedEvent</c> fires. Neither is a <see cref="BackgroundService" /> or
///         <see cref="IHostedService" />, so there is no loop or socket to start or stop.
///     </para>
/// </summary>
public sealed class ManagedServiceRegistry : IManagedServiceRegistry
{
    /// <summary>
    ///     This field contains every registered service, keyed by
    ///     <see cref="IManagedServiceRegistration.Name" /> for <see cref="Find" />.
    /// </summary>
    private readonly Dictionary<string, IManagedServiceRegistration> _byName;

    /// <summary>
    ///     This constructor wraps an already-built list of registrations, indexing them by name.
    /// </summary>
    /// <param name="services">Every registered service.</param>
    private ManagedServiceRegistry(IReadOnlyList<IManagedServiceRegistration> services)
    {
        Services = services;
        _byName = services.ToDictionary(service => service.Name, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public IReadOnlyList<IManagedServiceRegistration> Services { get; }

    /// <inheritdoc />
    public IManagedServiceRegistration Find(string name) =>
        name is not null && _byName.TryGetValue(name, out IManagedServiceRegistration service)
            ? service
            : null;

    /// <summary>
    ///     This method builds the registry. It finds each service through
    ///     <see cref="IEnumerable{IHostedService}" /> for a plain <c>AddHostedService&lt;T&gt;()</c>
    ///     registration, or through
    ///     <see cref="ServiceProviderServiceExtensions.GetRequiredService{T}" /> for one also
    ///     registered under its concrete type, and wraps each in a
    ///     <see cref="ManagedServiceRegistration" />. The class summary explains why these are the
    ///     host's running instances.
    /// </summary>
    /// <param name="serviceProvider">The host's root service provider.</param>
    /// <returns>The fully built registry.</returns>
    public static ManagedServiceRegistry Build(IServiceProvider serviceProvider)
    {
        // Every AddHostedService<T>() registration is a singleton, so this list holds the
        // instances the generic host has already started.
        IReadOnlyList<IHostedService> hostedServices =
        [
            .. serviceProvider.GetServices<IHostedService>(),
        ];

        T Resolve<T>()
            where T : class, IHostedService =>
            TryResolve<T>()
            ?? throw new InvalidOperationException(
                $"{typeof(T).Name} was not registered as an IHostedService -- Program.cs's own "
                    + "service registration must have changed without this registry being "
                    + "updated to match."
            );

        // Every wrapped background service is registered whether or not its feature is enabled
        // and does nothing while the feature is off, so a feature turned on later with a PATCH
        // still has a service to run. Resolve<T>() can therefore require every entry; it uses
        // TryResolve<T>() only to build its error message.
        T TryResolve<T>()
            where T : class, IHostedService => hostedServices.OfType<T>().FirstOrDefault();

        List<IManagedServiceRegistration> services =
        [
            // Transport listeners. Each reports IsListening (also used by the /status health
            // check), so IsRunning says whether a socket is bound, not just whether StartAsync
            // was called.
            new ManagedServiceRegistration(
                "plain-udp",
                ManagedServiceCategoryEnum.Transport,
                Resolve<UdpDnsListener>(),
                () => Resolve<UdpDnsListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "plain-tcp",
                ManagedServiceCategoryEnum.Transport,
                Resolve<TcpDnsListener>(),
                () => Resolve<TcpDnsListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dns-over-tls",
                ManagedServiceCategoryEnum.Transport,
                Resolve<DnsOverTlsListener>(),
                () => Resolve<DnsOverTlsListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dns-over-quic",
                ManagedServiceCategoryEnum.Transport,
                Resolve<DnsOverQuicListener>(),
                () => Resolve<DnsOverQuicListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dnscrypt-udp",
                ManagedServiceCategoryEnum.Transport,
                Resolve<DnsCryptUdpListener>(),
                () => Resolve<DnsCryptUdpListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dnscrypt-tcp",
                ManagedServiceCategoryEnum.Transport,
                Resolve<DnsCryptTcpListener>(),
                () => Resolve<DnsCryptTcpListener>().IsListening,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dns-over-https",
                ManagedServiceCategoryEnum.Transport,
                serviceProvider.GetRequiredService<DnsOverHttpsTransportGate>(),
                () => serviceProvider.GetRequiredService<DnsOverHttpsTransportGate>().IsRunning,
                isAlreadyRunning: true
            ),
            // Background maintenance loops. These have no running signal, so IsRunning comes from
            // ManagedServiceRegistration's start/stop bookkeeping, seeded as started (see the
            // class summary).
            new ManagedServiceRegistration(
                "query-processing-pipeline",
                ManagedServiceCategoryEnum.Background,
                serviceProvider.GetRequiredService<DnsQueryProcessingPipeline>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "authoritative-zone-cache",
                ManagedServiceCategoryEnum.Background,
                serviceProvider.GetRequiredService<AuthoritativeZoneCache>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "signed-zone-cache",
                ManagedServiceCategoryEnum.Background,
                serviceProvider.GetRequiredService<SignedZoneCache>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "root-hints-priming",
                ManagedServiceCategoryEnum.Background,
                Resolve<RootHintsPrimingService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "event-dispatcher",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsServerEventDispatcher>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "event-broker-consumer",
                ManagedServiceCategoryEnum.Background,
                Resolve<BrokerEventConsumerService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "secondary-zone-refresh",
                ManagedServiceCategoryEnum.Background,
                serviceProvider.GetRequiredService<SecondaryZoneRefreshService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dnssec-resigning",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnssecResigningBackgroundService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dnssec-key-rollover",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnssecKeyRolloverBackgroundService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "cloud-zone-sync",
                ManagedServiceCategoryEnum.Background,
                Resolve<CloudDnsZoneSyncService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "dns-cache-eviction",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsCacheEvictionService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "trust-anchor-rollover",
                ManagedServiceCategoryEnum.Background,
                Resolve<TrustAnchorRolloverService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "acme-certificate-renewal",
                ManagedServiceCategoryEnum.Background,
                Resolve<AcmeCertificateRenewalService>(),
                isAlreadyRunning: true
            ),
            // This loop has its own IsRunning probe, unlike the others in this group. Its
            // ExecuteAsync returns at once when DnsCryptOptions.Enable is false, as on a new
            // install where DNSCrypt is off. With the seeded bookkeeping it would still report
            // running, so turning DNSCrypt on later would never call StartAsync and the
            // certificate store would stay empty.
            new ManagedServiceRegistration(
                "dnscrypt-certificate-rotation",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsCryptCertificateRotationService>(),
                () => Resolve<DnsCryptCertificateRotationService>().IsRunning,
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "forwarder-health-tracker",
                ManagedServiceCategoryEnum.Background,
                serviceProvider.GetRequiredService<ForwarderHealthTracker>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "rate-limit-cleanup",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsRateLimitCleanupService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "query-log-cleanup",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsQueryLogCleanupService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "filter-list-external-refresh",
                ManagedServiceCategoryEnum.Background,
                Resolve<DnsFilterListExternalRefreshService>(),
                isAlreadyRunning: true
            ),
            new ManagedServiceRegistration(
                "webhook-delivery-retry-sweep",
                ManagedServiceCategoryEnum.Background,
                Resolve<WebhookDeliveryRetrySweepService>(),
                isAlreadyRunning: true
            ),
        ];

        return new ManagedServiceRegistry(services);
    }
}
