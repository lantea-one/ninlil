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
///     This class implements <see cref="IManagedServiceRegistry" /> by wrapping every real DNS
///     transport listener and background maintenance loop this host constructs into a named,
///     categorized <see cref="ManagedServiceRegistration" />, built once via <see cref="Build" />
///     and then held as an ordinary singleton.
///     <para>
///         <b>Instance identity is the crux of this whole mechanism.</b> Every wrapped service
///         class below is registered by its own project's own <c>ServiceCollection*Extension</c>
///         as a <em>singleton</em> -- either directly via <c>AddHostedService&lt;T&gt;()</c>
///         (which itself registers a singleton <see cref="IHostedService" /> descriptor keyed to
///         that concrete type), or, for a handful of services another component also needs to
///         reach directly (<see cref="DnsQueryProcessingPipeline" />,
///         <see cref="AuthoritativeZoneCache" />, <see cref="SignedZoneCache" />,
///         <see cref="SecondaryZoneRefreshService" />, <see cref="ForwarderHealthTracker" />), via
///         an explicit <c>AddSingleton&lt;T&gt;()</c> plus a factory-based
///         <c>AddSingleton&lt;IHostedService&gt;(sp =&gt; sp.GetRequiredService&lt;T&gt;())</c>
///         pointing at that same singleton. Either way, resolving
///         <see cref="IEnumerable{IHostedService}" /> from this host's own root service provider
///         -- exactly what <see cref="Build" /> does below, the same mechanism
///         <c>TransportListenersHealthCheck</c> already established for exactly this "reach a
///         specific already-running hosted service by its concrete type" need -- returns the
///         <em>identical, already-constructed instances</em> the ASP.NET Core generic host itself
///         calls <c>StartAsync</c>/<c>StopAsync</c> on at process startup/shutdown. A
///         <see cref="ManagedServiceRegistration" /> built from one of these is therefore not a
///         second, parallel copy of the service with its own independent lifecycle -- it is a
///         named handle onto the exact same object the host manages, so a future
///         <c>ServicesController</c> calling <see cref="ManagedServiceRegistration.StopAsync" />
///         genuinely halts the one real instance answering traffic, and a subsequent
///         <see cref="ManagedServiceRegistration.StartAsync" /> genuinely resumes that same
///         instance -- never a distinct, orphaned duplicate the host never touches.
///     </para>
///     <para>
///         <see cref="Build" /> is called lazily, from a singleton factory registration in
///         <c>Program.cs</c>, the first time anything actually resolves
///         <see cref="IManagedServiceRegistry" /> -- always after <c>WebApplication.Run</c> (or an
///         integration test's own first request) has already started the generic host, which is
///         what makes it safe to pass <c>isAlreadyRunning: true</c> to every
///         <see cref="ManagedServiceRegistration" /> constructed here that has no richer
///         <c>IsListening</c>-style probe of its own: by the time this method ever runs, the host
///         has already called <c>StartAsync</c> on every one of them once, directly, bypassing
///         this wrapper entirely -- so a fresh registration's own start/stop bookkeeping needs to
///         be seeded as already-started, not literally re-derived by calling
///         <see cref="ManagedServiceRegistration.StartAsync" /> a second, redundant (and, for a
///         component that does not tolerate a concurrent double-start such as a bound socket,
///         actively dangerous) time.
///     </para>
///     <para>
///         <b>DNS-over-HTTPS is the one entry with no real <see cref="IHostedService" /> of its
///         own to wrap</b> in this host: unlike every other project's transport,
///         <c>Program.cs</c> deliberately never registers <see cref="DnsOverHttpsHostedService" />
///         here (that class self-hosts a second, separate Kestrel instance, which this combined
///         host does not want) -- it instead maps RFC 8484's GET/POST forms directly onto this
///         host's own already-running shared Kestrel. <see cref="DnsOverHttpsTransportGate" />
///         (registered as its own singleton, opened by
///         <c>EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps</c> right after
///         mapping those routes) is what this entry wraps instead, giving DNS-over-HTTPS the same
///         genuine hot start/stop lifecycle every socket-based transport already has -- see that
///         class's own doc comment for why a gate, not a real endpoint unmap, is what that
///         actually takes.
///     </para>
///     <para>
///         <b>CAA automation and PTR sync are deliberately absent from this registry</b>, despite
///         superficially resembling DNSSEC re-signing and cloud zone sync as background services
///         -- checked against the real code, not assumed:
///         <c>CaaRecordReconciliationService</c>/<c>PtrSyncService</c> are both purely
///         event-reactive, invoked synchronously from
///         <c>CaaRecordReconciliationEventSubscriber</c>/<c>PtrSyncEventSubscriber</c> whenever a
///         <c>DnsZoneChangedEvent</c> fires -- neither is a <see cref="BackgroundService" /> or
///         <see cref="IHostedService" /> at all, so there is no persistent loop, socket, or
///         subscription for this registry to start or stop; the only thing genuinely there to
///         control is a permanent event-bus subscription that already runs whenever any event
///         subscriber does, indistinguishable from "the server is up at all."
///     </para>
/// </summary>
public sealed class ManagedServiceRegistry : IManagedServiceRegistry
{
    /// <summary>
    ///     This field contains every registered service, keyed by its own stable
    ///     <see cref="IManagedServiceRegistration.Name" /> for <see cref="Find" />'s own lookup.
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
    ///     This method builds the full registry by resolving every real service instance this
    ///     host constructs -- via <see cref="IEnumerable{IHostedService}" /> for a plain
    ///     <c>AddHostedService&lt;T&gt;()</c> registration, or directly via
    ///     <see cref="ServiceProviderServiceExtensions.GetRequiredService{T}" /> for one this
    ///     project's own composition already dual-registers as both its concrete type and
    ///     <see cref="IHostedService" /> -- and wrapping each in a named, categorized
    ///     <see cref="ManagedServiceRegistration" />. See this class's own doc comment for why
    ///     resolving the same singleton <see cref="IServiceProvider" /> this way is what makes
    ///     every wrapper here a handle onto the real, host-managed instance rather than an
    ///     independent copy.
    /// </summary>
    /// <param name="serviceProvider">This host's own root service provider.</param>
    /// <returns>The fully built registry.</returns>
    public static ManagedServiceRegistry Build(IServiceProvider serviceProvider)
    {
        // Resolved once -- every AddHostedService<T>() registration in this project is a
        // singleton descriptor, so this enumerable contains the exact same instances the generic
        // host itself already started, regardless of how many times (or from where) it is
        // resolved afterward.
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

        // Every background service this registry wraps, DnsFilterListExternalRefreshService
        // included, is registered unconditionally by its own project's
        // ServiceCollection*Extension and simply self-no-ops internally when its own feature is
        // disabled (the same "off by default" shape invariant #6 describes) -- Server.Filtering's
        // own AddNinlilDnsServerFiltering used to register DnsFilterListExternalRefreshService
        // (and IDnsFilterEngine) only when DnsFilterOptions.Enable was already true at process
        // startup, which meant a live PATCH turning filtering on could never make either exist at
        // all; both are now always registered, so Resolve<T>() (which uses TryResolve<T>() below
        // internally, purely to build its own "missing registration" exception message) is safe
        // for every entry, with no TryResolve-and-conditionally-add special case left.
        T TryResolve<T>()
            where T : class, IHostedService => hostedServices.OfType<T>().FirstOrDefault();

        List<IManagedServiceRegistration> services =
        [
            // Transport listeners -- every one of these has a real, independently-observed
            // IsListening signal (consulted by the /status health check), so IsRunning
            // here reflects whether a socket is genuinely bound right now, not merely whether
            // StartAsync has been called.
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
            // Background maintenance loops -- none of these expose a richer running signal of
            // their own, so IsRunning here falls back to ManagedServiceRegistration's own
            // start/stop bookkeeping, seeded as already-started (see this class's own doc comment
            // for why that is the correct seed value, not a hardcoded guess).
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
            // Unlike every other entry in this "background maintenance loop" group, this one has
            // a real, independently-observed IsRunning signal of its own (added directly
            // alongside this registration -- see that property's own doc comment for why the
            // bookkeeping fallback every other background loop here relies on is specifically
            // wrong for this one service): DnsCryptCertificateRotationService's own ExecuteAsync
            // returns immediately, without ever actually starting its rotation loop, whenever
            // DnsCryptOptions.Enable is false at the moment it runs -- true of a fresh
            // installation's very first boot, since DNSCrypt (like every capability-expanding
            // transport other than Plain DNS) starts disabled by default. Without this probe, a
            // later REST-driven live enable of DNSCrypt would see IsRunning already report
            // true (this registration's own bookkeeping fallback, seeded by isAlreadyRunning
            // below to match every hosted service in this process having already been started
            // once by the generic host) and therefore never actually call StartAsync again,
            // silently leaving DNSCrypt's own certificate store empty forever.
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
