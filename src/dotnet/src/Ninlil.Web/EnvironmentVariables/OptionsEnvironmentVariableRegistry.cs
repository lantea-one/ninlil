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
///     This static class is the hand-maintained list of the top-level bound configuration
///     classes, the "which types exist" half of the <c>NINLIL_</c> environment variable mapping.
///     <see cref="OptionsEnvironmentVariableMap" /> reflects over each type for the other half,
///     the variable name of every property.
///     <para>
///         <b>Why a list rather than assembly scanning.</b> <c>Ninlil.Web</c> references every
///         project an options class can live in, but which assemblies are loaded at a given point
///         in <c>Program.cs</c> depends on which types earlier code has touched, so a scan could
///         silently miss one. An explicit list is as reliable as the one
///         <c>Configure&lt;TOptions&gt;</c> call each options class already has in its project's
///         <c>ServiceCollection...Extension</c>.
///     </para>
///     <para>
///         When an options class is added, add its <see cref="Type" /> here in the same change.
///         <c>Dhcpv6RelayOptions</c> is missing at present.
///     </para>
/// </summary>
public static class OptionsEnvironmentVariableRegistry
{
    /// <summary>
    ///     This property lists the top-level bound configuration classes. Each section path is
    ///     read by reflection from the class's public <c>SectionName</c> constant, except for
    ///     ASP.NET Core Identity's <see cref="IdentityOptions" />, which has none and is bound at
    ///     <see cref="ServiceCollectionIdentityExtension.IdentityOptionsSectionName" />, passed
    ///     here explicitly.
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
        // DHCP engine and DHCPv4/DHCPv6 transports (Ninlil.Dhcp.Server.Core, .Protocol.Dhcpv4,
        // .Protocol.Dhcpv6), the DHCPv4 relay agent (Ninlil.Dhcp.Server.Relay) and the TFTP
        // server. Dhcpv6RelayOptions, in the same relay project, is not listed.
        new(typeof(DhcpServerOptions)),
        new(typeof(Dhcpv4ProtocolOptions)),
        new(typeof(Dhcpv6ProtocolOptions)),
        new(typeof(Dhcpv4RelayOptions)),
        new(typeof(TftpServerOptions)),
        // Router Advertisement content (Ninlil.Dhcp.Server.Ra) and its ICMPv6 transport timing
        // (Ninlil.Dhcp.Protocol.Ra).
        new(typeof(RaOptions)),
        new(typeof(RaProtocolOptions)),
        // The web host (Ninlil.Web): Kestrel listening, reverse proxy trust, appearance,
        // branding and the runtime settings file path.
        new(typeof(WebListenOptions)),
        new(typeof(ReverseProxyOptions)),
        new(typeof(AppearanceOptions)),
        new(typeof(BrandingOptions)),
        new(typeof(RuntimeConfigurationOptions)),
    ];

    /// <summary>
    ///     This property lists the configuration paths read by key through the
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> indexer rather than an
    ///     options class: the two bootstrap administrator settings, and ASP.NET Core's
    ///     <c>AllowedHosts</c>, which host filtering reads (see <see cref="AllowedHostsConfigurationKey" />). The
    ///     <c>standaloneConfigurationPaths</c> parameter of
    ///     <see cref="OptionsEnvironmentVariableMap.BuildMappings" /> explains why they are an
    ///     exception.
    /// </summary>
    public static IReadOnlyList<string> StandaloneConfigurationPaths { get; } =
    [
        AdministratorBootstrapper.UserNameConfigurationKey,
        AdministratorBootstrapper.PasswordConfigurationKey,
        AllowedHostsConfigurationKey,
    ];

    /// <summary>
    ///     This constant contains the key ASP.NET Core's host filtering middleware, which
    ///     <c>WebApplication.CreateBuilder</c> adds, reads its semicolon-separated Host header
    ///     allowlist from. It is mapped so <c>NINLIL_ALLOWED_HOSTS</c> can set it. Unset, every
    ///     Host is accepted: an install is usually reached by an address or name the server cannot
    ///     know in advance, and nothing the server builds trusts the Host header.
    /// </summary>
    public const string AllowedHostsConfigurationKey = "AllowedHosts";
}
