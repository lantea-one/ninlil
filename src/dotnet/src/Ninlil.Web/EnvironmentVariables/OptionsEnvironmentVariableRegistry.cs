using Microsoft.AspNetCore.Identity;
using Ninlil.Common.Configuration;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Protocol.Ra;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Ra;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Tftp;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Events.RabbitMq;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Forwarding;
using Ninlil.Dns.Server.QueryLog;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.SelfUpdate;
using Ninlil.Dns.Server.Tsig.Options;
using Ninlil.Dns.Server.Webhooks;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.EnvironmentVariables;

/// <summary>
///     This static class is the single, explicit, hand-maintained list of every real,
///     top-level bound configuration class this whole application registers -- the "which types
///     exist" half of this project's own NINLIL_ environment-variable mapping generator (the "how
///     to name each one's own properties" half is reflected automatically, once, by
///     <see cref="OptionsEnvironmentVariableMap" />).
///     <para>
///         <b>Deliberately an explicit registry, not assembly-scanning</b>: this project's own
///         single combined host (<c>Ninlil.Web</c>) already references every project a bound
///         configuration class could possibly live in, so scanning every loaded assembly for a
///         <c>*Options</c>-named type with a public <c>SectionName</c> constant was a real
///         alternative -- but which assemblies are actually *loaded* (as opposed to merely
///         referenced) at any given point in <c>Program.cs</c>'s own top-level statements depends
///         on exactly which types earlier code has already touched, an order-of-execution detail
///         that would make "did assembly-scanning actually find everything" a fragile question to
///         answer with confidence. An explicit list is exactly as reliable as this project's own
///         already-established convention of one hand-written <c>Configure&lt;TOptions&gt;</c> call
///         per bound class in each project's own <c>ServiceCollectionXyzExtension</c> -- adding a
///         new bound configuration class already means adding one line there; this registry asks
///         for exactly one more line in the same spirit, not a new maintenance burden shape.
///     </para>
///     <para>
///         Keep this list in sync with reality the same way every other cross-cutting registry in
///         this project already is kept in sync: when a new <c>Options</c>-bound class is added
///         anywhere in this solution (and wired into DI via its own project's
///         <c>ServiceCollectionXyzExtension</c>), add its own <see cref="Type" /> here too, in the
///         same pull request.
///     </para>
/// </summary>
public static class OptionsEnvironmentVariableRegistry
{
    /// <summary>
    ///     This property lists every real, top-level bound configuration class in the application,
    ///     as of this writing, each one's own configuration section path read directly from its
    ///     own public <c>SectionName</c> constant via reflection -- except
    ///     <see cref="IdentityOptions" /> (ASP.NET Core Identity's own built-in type), which has no
    ///     <c>SectionName</c> constant of its own and is bound instead at
    ///     <see cref="ServiceCollectionIdentityExtension.IdentityOptionsSectionName" />, passed
    ///     here as an explicit override.
    /// </summary>
    public static IReadOnlyList<OptionsEnvironmentVariableRoot> Roots { get; } =
    [
        // Storage engine selection (Ninlil.Dns.Data.Abstractions).
        new(typeof(StorageOptions)),
        // Identity / auth (Ninlil.Dns.Identity).
        new(typeof(IdentityOptions), ServiceCollectionIdentityExtension.IdentityOptionsSectionName),
        new(typeof(JwtOptions)),
        new(typeof(EmailSenderOptions)),
        new(typeof(AdminLoginRateLimitOptions)),
        new(typeof(OidcSsoOptions)),
        // Protocol transports (Ninlil.Dns.Protocol.*).
        new(typeof(PlainProtocolOptions)),
        new(typeof(DnsOverTlsOptions)),
        new(typeof(ProxyProtocolOptions)),
        new(typeof(DnsOverHttpsOptions)),
        new(typeof(DnsOverQuicOptions)),
        new(typeof(DnsCryptOptions)),
        // Authoritative engine (Ninlil.Dns.Server.Authoritative).
        new(typeof(AuthoritativeServerOptions)),
        new(typeof(ZoneTransferOptions)),
        new(typeof(ZoneTransferClientOptions)),
        new(typeof(NotifyOptions)),
        new(typeof(SecondaryZoneRefreshOptions)),
        new(typeof(ZoneSigningOptions)),
        new(typeof(DnssecKeyRolloverOptions)),
        new(typeof(CatalogSyncOptions)),
        new(typeof(PtrSyncOptions)),
        // Recursive resolver + shared server-core cross-cutting concerns.
        new(typeof(RecursiveServerOptions)),
        new(typeof(DnsQueryProcessingPipelineOptions)),
        new(typeof(DnsServerEventPublisherOptions)),
        new(typeof(DnsCookieOptions)),
        new(typeof(OutboundQueryOptions)),
        new(typeof(EdnsClientSubnetOptions)),
        new(typeof(Dns64Options)),
        // DNSSEC, ACME/CAA.
        new(typeof(DnssecAlgorithmPolicyOptions)),
        new(typeof(AcmeOptions)),
        new(typeof(CaaAutoManagementOptions)),
        // TSIG.
        new(typeof(TsigServiceOptions)),
        // Message-broker event transports.
        new(typeof(ActiveMqEventBrokerOptions)),
        new(typeof(RabbitMqEventBrokerOptions)),
        // Filtering, forwarding, rewrites, rate limiting, query log.
        new(typeof(DnsFilterOptions)),
        new(typeof(ForwardingOptions)),
        new(typeof(DnsRewriteOptions)),
        new(typeof(DnsRateLimitOptions)),
        new(typeof(QueryLoggingOptions)),
        // Passive client presence tracking.
        new(typeof(ClientPresenceOptions)),
        // MAC vendor lookup's automatic IEEE registry refresh.
        new(typeof(MacVendorLookupOptions)),
        // Webhooks, backup, self-update, clustering.
        new(typeof(WebhookDeliveryOptions)),
        new(typeof(BackupOptions)),
        new(typeof(SelfUpdateOptions)),
        new(typeof(ClusterOptions)),
        // DHCP server engine + DHCPv4/DHCPv6 transports (Ninlil.Dhcp.Server.Core /
        // .Protocol.Dhcpv4 / .Protocol.Dhcpv6), plus the mutually exclusive DHCPv4 relay-agent
        // engine (Ninlil.Dhcp.Server.Relay) -- DHCPv6 has no relay-agent counterpart yet.
        new(typeof(DhcpServerOptions)),
        new(typeof(Dhcpv4ProtocolOptions)),
        new(typeof(Dhcpv6ProtocolOptions)),
        new(typeof(Dhcpv4RelayOptions)),
        new(typeof(TftpServerOptions)),
        // Router Advertisement content (Ninlil.Dhcp.Server.Ra) + its own raw ICMPv6 transport
        // timing (Ninlil.Dhcp.Protocol.Ra) -- no per-client lease state and no relay-agent
        // counterpart at all, unlike DHCPv4/DHCPv6.
        new(typeof(RaOptions)),
        new(typeof(RaProtocolOptions)),
        // The web host itself (Ninlil.Web) -- Kestrel listen options, reverse proxy trust,
        // appearance/branding, and the runtime-settings-file path.
        new(typeof(WebListenOptions)),
        new(typeof(ReverseProxyOptions)),
        new(typeof(AppearanceOptions)),
        new(typeof(BrandingOptions)),
        new(typeof(RuntimeConfigurationOptions)),
    ];

    /// <summary>
    ///     This property lists every real configuration path read directly by string key (via
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" />'s own indexer) rather
    ///     than through a bound <c>Options</c> class -- see
    ///     <see cref="OptionsEnvironmentVariableMap.BuildMappings" />'s own
    ///     <c>standaloneConfigurationPaths</c> parameter doc comment for why these two exist as a
    ///     deliberate exception to "every real setting is a bound Options class."
    /// </summary>
    public static IReadOnlyList<string> StandaloneConfigurationPaths { get; } =
    [
        AdministratorBootstrapper.UserNameConfigurationKey,
        AdministratorBootstrapper.PasswordConfigurationKey,
    ];
}
