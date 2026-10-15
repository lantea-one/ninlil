using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Ninlil.Common.Configuration;
using Ninlil.Common.Serializer.Converter;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dhcp.Data.MongoDb.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv4.Extension;
using Ninlil.Dhcp.Protocol.Dhcpv6.Extension;
using Ninlil.Dhcp.Protocol.Ra.Extension;
using Ninlil.Dhcp.Server.Core;
using Ninlil.Dhcp.Server.Core.Extension;
using Ninlil.Dhcp.Server.DnsIntegration;
using Ninlil.Dhcp.Server.DnsIntegration.Extension;
using Ninlil.Dhcp.Server.Ra.Extension;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dhcp.Server.Relay.Extension;
using Ninlil.Dhcp.Server.Tftp.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.IO;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Dns.Data.MongoDb.Maintenance;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Protocol.DnsCrypt.Extension;
using Ninlil.Dns.Protocol.DnsOverHttps;
using Ninlil.Dns.Protocol.DnsOverHttps.Extension;
using Ninlil.Dns.Protocol.DnsOverQuic;
using Ninlil.Dns.Protocol.DnsOverQuic.Extension;
using Ninlil.Dns.Protocol.DnsOverTls;
using Ninlil.Dns.Protocol.DnsOverTls.Extension;
using Ninlil.Dns.Protocol.Plain;
using Ninlil.Dns.Protocol.Plain.Extension;
using Ninlil.Dns.Server.AccessControl.Extension;
using Ninlil.Dns.Server.Acme.Extension;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Backup.Extension;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.ClientIdentity;
using Ninlil.Dns.Server.ClientIdentity.Extension;
using Ninlil.Dns.Server.ClientSchedule.Extension;
using Ninlil.Dns.Server.Cluster.Extension;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Extension;
using Ninlil.Dns.Server.Core.Certificates;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.DnsCrypt.Extension;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Events.ActiveMq.Extension;
using Ninlil.Dns.Server.Events.RabbitMq.Extension;
using Ninlil.Dns.Server.Filtering.Extension;
using Ninlil.Dns.Server.Forwarding.Extension;
using Ninlil.Dns.Server.Presets.Extension;
using Ninlil.Dns.Server.QueryLog.Extension;
using Ninlil.Dns.Server.RateLimiting.Extension;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Extension;
using Ninlil.Dns.Server.Rewrites.Extension;
using Ninlil.Dns.Server.SelfUpdate.Extension;
using Ninlil.Dns.Server.Tsig.Extension;
using Ninlil.Dns.Server.Webhooks.Extension;
using Ninlil.Web.EnvironmentVariables;
using Ninlil.Web.ExceptionHandling;
using Ninlil.Web.Extension;
using Ninlil.Web.HealthChecks;
using Ninlil.Web.Hubs;
using Ninlil.Web.Middleware;
using Ninlil.Web.Options;
using Ninlil.Web.Routing;
using Ninlil.Web.Services;
using Ninlil.Web.Services.Storage;
using Ninlil.Web.Startup;
using Swashbuckle.AspNetCore.Filters;

// NINLIL_DOTNET_* runtime settings (server GC and similar) are read by the native runtime before
// any managed code runs, so they can only be applied by relaunching the process with their
// DOTNET_* names set. That happens first, before any other startup work (see
// NativeRuntimeSettingsRelaunch).
NativeRuntimeSettingsRelaunch.Apply();

// This must run before WebApplication.CreateBuilder(args), which is what reads the framework
// environment variables this step fills in (ASPNETCORE_ENVIRONMENT, ASPNETCORE_URLS and so on).
// See FrameworkEnvironmentVariableBootstrap for the full list.
FrameworkEnvironmentVariableBootstrap.Apply();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Lets the Windows Service Control Manager start and stop the process (sc.exe create Ninlil ...).
// It does nothing unless the SCM started the process, so console runs, Linux and macOS are
// unchanged. When it is active it also sets the content root to the executable's directory
// instead of C:\Windows\System32.
builder.Host.UseWindowsService(options => options.ServiceName = "Ninlil");

// Every environment variable the application reads uses one NINLIL_-prefixed, single-underscore
// naming scheme: the bound Options classes directly, and the framework variables through the
// bootstrap step above. ASP.NET Core's default double-underscore
// EnvironmentVariablesConfigurationProvider (added unfiltered by CreateBuilder) is removed and
// replaced with NinlilEnvironmentVariablesConfigurationSource, whose mapping table is built by
// reflection over the Options classes in OptionsEnvironmentVariableRegistry.Roots.
//
// CreateBuilder can register more than one EnvironmentVariablesConfigurationSource (an early
// ASPNETCORE_-prefixed one for host configuration, and the unprefixed one just before the
// command-line arguments). The replacement goes where the last of them was, so it keeps that
// precedence: appsettings.json and appsettings.{Environment}.json are overridden by it, and
// command-line arguments still override it.
IReadOnlyDictionary<string, string> ninlilEnvironmentVariableMap =
    OptionsEnvironmentVariableMap.BuildMappings(
        OptionsEnvironmentVariableRegistry.Roots,
        OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
    );
NinlilEnvironmentVariablesConfigurationSource ninlilEnvironmentVariableSource = new(
    ninlilEnvironmentVariableMap
);

List<IConfigurationSource> environmentVariablesSources = builder
    .Configuration.Sources.Where(source => source is EnvironmentVariablesConfigurationSource)
    .ToList();

int ninlilSourceInsertIndex = builder.Configuration.Sources.Count;
if (environmentVariablesSources.Count > 0)
{
    // The sources before the last one in the filtered list sit at lower indexes (Where/ToList keeps
    // order), so removing them all moves the last one down by that many positions.
    int lastOriginalIndex = builder.Configuration.Sources.IndexOf(environmentVariablesSources[^1]);
    ninlilSourceInsertIndex = lastOriginalIndex - (environmentVariablesSources.Count - 1);

    foreach (IConfigurationSource source in environmentVariablesSources)
        builder.Configuration.Sources.Remove(source);

    ninlilSourceInsertIndex = Math.Clamp(
        ninlilSourceInsertIndex,
        0,
        builder.Configuration.Sources.Count
    );
}
builder.Configuration.Sources.Insert(ninlilSourceInsertIndex, ninlilEnvironmentVariableSource);

// Everything the host generates and keeps (an auto-generated SQLite database, the Data
// Protection key ring, the JWT signing key, the cluster node id) lives in one "data" directory
// next to the application.
string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDirectory);

// The directory is restricted to the process owner, as the self-signed TLS certificate and
// DNSCrypt provider keys are, because the Data Protection key ring is not encrypted at rest on
// Linux without a certificate, which operators are not asked to provide.
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(
        dataDirectory,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
    );

// The Data Protection key ring must survive a restart, or every secret encrypted with it (TOTP
// authenticator keys, TSIG secrets, DNSSEC keys, cloud provider credentials, the ACME account
// key) becomes undecryptable when a new process generates a fresh key. This is registered
// before the unconfigured AddDataProtection() call in AddNinlilDnsIdentity so this
// configuration wins; AddDataProtection() can be called more than once.
builder
    .Services.AddDataProtection()
    .SetApplicationName("Ninlil.Dns")
    .PersistKeysToFileSystem(new DirectoryInfo(dataDirectory));

// JSON Web Tokens need a signing key. One is generated and kept in the data directory so a fresh
// installation works without configuration (as the auto-generated SQLite connection string of
// AddNinlilDnsEntityFrameworkCore does) and a restart does not invalidate issued tokens.
string signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    string signingKeyFilePath = Path.Combine(dataDirectory, "jwt-signing-key.txt");

    // A blank existing file (an interrupted write by an older version) would give an empty
    // signing key, so it is regenerated like a missing one.
    signingKey = File.Exists(signingKeyFilePath) ? File.ReadAllText(signingKeyFilePath).Trim() : "";

    if (signingKey.Length == 0)
    {
        signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        AtomicFile.WriteAllBytes(
            signingKeyFilePath,
            Encoding.UTF8.GetBytes(signingKey),
            AtomicFilePermissions.OwnerOnly
        );
    }

    builder.Configuration["Jwt:SigningKey"] = signingKey;
}

// This node's cluster identity, generated once and kept so it survives a restart, like the JWT
// signing key above. It cannot be edited through the API (see ClusterOptions.NodeId): every
// peer that joins records it as RemoteNodeId, so changing it would leave their records stale.
if (string.IsNullOrWhiteSpace(builder.Configuration["Cluster:NodeId"]))
{
    string nodeIdFilePath = Path.Combine(dataDirectory, "cluster-node-id.txt");

    // A blank existing file stops startup instead of getting a new id (see ClusterNodeIdFile).
    string nodeId = ClusterNodeIdFile.ReadOrCreate(nodeIdFilePath);

    builder.Configuration["Cluster:NodeId"] = nodeId;
}

// The Kestrel HTTP/HTTPS listener, shared by the REST API, the SignalR hub and (mapped after
// Build() below) DNS-over-HTTPS. Its certificate comes from the same
// ReloadableTlsCertificateProvider the other TLS transports use, so replacing the certificate
// file (by hand or through an ACME renewal) updates this endpoint, DNS-over-TLS and
// DNS-over-QUIC without a restart, as long as they point at the same PfxPath (by default they
// all share the same self-signed certificate).
WebListenOptions webListenOptions =
    builder.Configuration.GetSection(WebListenOptions.SectionName).Get<WebListenOptions>()
    ?? new WebListenOptions();

// There is no dependency injection container yet, so this is a throwaway console logger for the
// provider's certificate load and reload messages.
ILogger kestrelCertificateLogger = LoggerFactory
    .Create(logging => logging.AddConsole())
    .CreateLogger("Ninlil.Web.Kestrel.Tls");

ReloadableTlsCertificateProvider kestrelCertificateProvider = new(
    webListenOptions.Certificate,
    kestrelCertificateLogger,
    webListenOptions.Certificate.CertificateReloadPollInterval
);

// Registered so TlsCertificateFreshnessHealthCheck (the /status endpoint) reuses this provider,
// the certificate Kestrel, DNS-over-TLS and DNS-over-QUIC share, instead of polling a second one.
builder.Services.AddSingleton(kestrelCertificateProvider);

// A full-system backup includes this certificate file. Registering the TlsCertificateOptions
// lets SystemBackupService resolve its path (PfxPath, else SelfSignedCertificateStatePath)
// without repeating the "an operator's PFX wins over the self-signed state file" logic.
builder.Services.AddSingleton(webListenOptions.Certificate);

// WebListenSettingsController reads this instance, the values Kestrel was bound with, since
// WebListenOptions is read once before Build() and is never live (see that controller).
builder.Services.AddSingleton(webListenOptions);

// Every address by default (one dual-stack wildcard per port); otherwise one socket per
// configured address and port. WebListenAddressResolver skips an address this host doesn't have
// (Kestrel would otherwise fail the whole startup on it) and falls back to loopback if none work.
IReadOnlyList<IPAddress> webListenAddresses =
    WebListenAddressResolver.Resolve(
        webListenOptions,
        LoggerFactory.Create(logging => logging.AddConsole()).CreateLogger("Ninlil.Web.Kestrel")
    ) ?? [IPAddress.IPv6Any];

builder.WebHost.ConfigureKestrel(kestrel =>
{
    foreach (IPAddress address in webListenAddresses)
    {
        kestrel.Listen(address, webListenOptions.HttpPort);
        kestrel.Listen(
            address,
            webListenOptions.HttpsPort,
            listenOptions =>
                listenOptions.UseHttps(httpsOptions =>
                {
                    httpsOptions.ServerCertificateSelector = (_, _) =>
                        kestrelCertificateProvider.Current;
                    httpsOptions.SslProtocols = webListenOptions.Certificate.AllowedProtocols;
                })
        );
    }
});

// Reverse-proxy trust for the Kestrel listener. It is off by default, as a capability-expanding
// setting, so forwarded headers are honored only after an operator configures a proxy
// allowlist.
ReverseProxyOptions reverseProxyOptions =
    builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
    ?? new ReverseProxyOptions();
builder.Services.Configure<ReverseProxyOptions>(
    builder.Configuration.GetSection(ReverseProxyOptions.SectionName)
);

if (reverseProxyOptions.Enable)
{
    // There is no dependency injection container yet, so a throwaway console logger warns about a
    // malformed allowlist entry. ReverseProxySettingsController validates every entry it writes,
    // so this fires only for a hand-edited configuration file.
    ILogger reverseProxyBootstrapLogger = LoggerFactory
        .Create(logging => logging.AddConsole())
        .CreateLogger("Ninlil.Web.ReverseProxy");

    // ForwardedHeadersMiddleware's KnownProxies/KnownIPNetworks allowlist, built once here from
    // ReverseProxyOptions, is what ReverseProxyTrustGatingMiddleware reads too, so the two cannot
    // disagree about which peers are trusted. ReverseProxyForwardedHeadersConfigurator does the
    // work (including clearing the framework's pre-trusted loopback defaults) so a test can call it
    // directly.
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersOptions =>
        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            reverseProxyOptions,
            reverseProxyBootstrapLogger
        )
    );
}

// Register the storage engine Storage:Engine names. MongoDB has its own implementation of every
// repository; the relational engines all use Entity Framework Core with one provider-neutral
// model.
StorageOptions storageOptions =
    builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? new StorageOptions();

// The registration validates the cluster settings (Storage:ReadReplica, Storage:PostgreSql and so
// on) and composes the connection strings. A contradiction refuses the start with exit code 78,
// like the database guards further down, before anything touches the database.
try
{
    if (storageOptions.Engine == StorageEngineEnum.MongoDb)
        builder.Services.AddNinlilDnsMongoDb(builder.Configuration);
    else
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
}
catch (DatabaseStartupRefusedException refusal)
{
    using ILoggerFactory refusalLoggerFactory = LoggerFactory.Create(logging =>
        logging.AddConsole()
    );
    refusalLoggerFactory
        .CreateLogger("Ninlil.Web.Storage")
        .LogCritical(refusal.InnerException, "Startup refused: {Message}", refusal.Message);
    Environment.ExitCode = DatabaseStartupRefusedException.ExitCode;
    return;
}

// The DHCP repositories are entity types in the same context or client, not a second storage
// engine (see NinlilDnsContext). They are registered right after the DNS-side call above, the
// order AddNinlilDhcpEntityFrameworkCore and AddNinlilDhcpMongoDb require. This only wires
// repositories; it opens no socket and does nothing visible whether or not DHCP is enabled.
if (storageOptions.Engine == StorageEngineEnum.MongoDb)
    builder.Services.AddNinlilDhcpMongoDb();
else
    builder.Services.AddNinlilDhcpEntityFrameworkCore();

// The JSON file the runtime settings controllers (PresetsController and the *SettingsController
// classes) merge their writes into. It defaults to appsettings.json next to the application and
// can be overridden, so a test can point it at its own file.
RuntimeConfigurationOptions runtimeConfigurationOptions =
    builder
        .Configuration.GetSection(RuntimeConfigurationOptions.SectionName)
        .Get<RuntimeConfigurationOptions>()
    ?? new RuntimeConfigurationOptions();
if (string.IsNullOrWhiteSpace(runtimeConfigurationOptions.ConfigurationFilePath))
    runtimeConfigurationOptions.ConfigurationFilePath = Path.Combine(
        builder.Environment.ContentRootPath,
        "appsettings.json"
    );
builder.Services.AddSingleton(runtimeConfigurationOptions);

// Encrypts an SMTP password that an earlier version stored in plaintext in that file.
builder.Services.AddHostedService<SmtpPasswordMigrationService>();

// The same for an SSO client secret typed into that file in plaintext.
builder.Services.AddHostedService<OidcClientSecretMigrationService>();

// The same for the secrets of the synced settings sections (the reverse-proxy shared
// secret), and the matching read side: the options classes get the decrypted value, or a
// fail-closed one when the stored secret cannot be decrypted.
builder.Services.AddHostedService<ConfigurationSecretMigrationService>();
builder.Services.AddNinlilConfigurationSecretResolution(builder.Configuration);

// The default appearance (palette and mode). AppearanceSettingsController reads it through
// IOptionsMonitor, so a write to the runtime configuration file takes effect at once (see
// AppearanceOptions for why that is safe here).
builder.Services.Configure<AppearanceOptions>(
    builder.Configuration.GetSection(AppearanceOptions.SectionName)
);

// Branding (site title and logo), read through IOptionsMonitor by BrandingSettingsController
// like AppearanceOptions, since nothing else on the server depends on it.
builder.Services.Configure<BrandingOptions>(
    builder.Configuration.GetSection(BrandingOptions.SectionName)
);

// A full-system backup covers the data directory (the Data Protection key ring) and the runtime
// configuration file. Their paths are set here after binding rather than configured separately,
// so each has one source of truth.
builder.Services.AddNinlilDnsServerBackup(builder.Configuration);
builder.Services.PostConfigure<BackupOptions>(options =>
{
    options.DataProtectionKeyRingDirectory = dataDirectory;
    options.RuntimeConfigurationFilePath = runtimeConfigurationOptions.ConfigurationFilePath;
});

// A storage switch is saved by the request side (StorageEngineSwitchService) and applied by
// PendingStorageSwitchApplier at the next start, before any background service runs. The
// pending record lives in the data directory, its connection strings protected by the Data
// Protection key ring.
builder.Services.AddSingleton(provider => new PendingStorageSwitchStore(
    dataDirectory,
    provider.GetRequiredService<IDataProtectionProvider>(),
    provider.GetRequiredService<ILogger<PendingStorageSwitchStore>>()
));
builder.Services.AddSingleton<StorageSwitchOutcomeRecorder>();
builder.Services.AddSingleton<PendingStorageSwitchApplier>();
builder.Services.AddScoped<IStorageEngineSwitchService, StorageEngineSwitchService>();

builder.Services.AddNinlilDnsIdentity(builder.Configuration);
builder.Services.AddNinlilDnsAuthentication(builder.Configuration);

// Adds the cluster channel's TSIG scheme (ClusterTsigAuthenticationDefaults.AuthenticationScheme)
// to the AuthenticationBuilder AddNinlilDnsAuthentication built, leaving its default schemes and
// JWT-or-API-token forwarding alone (see AddNinlilDnsServerClusterAuthentication).
builder.Services.AddNinlilDnsServerClusterAuthentication();

builder.Services.AddNinlilDnsAuthorization();

// The admin sign-in limiter, separate from the DNS query rate limiter registered below
// (AddNinlilDnsServerRateLimiting); see AdminLoginRateLimitOptions for how they differ.
builder.Services.AddNinlilDnsAdminLoginRateLimiting(builder.Configuration);

// A browser cannot attach an Authorization header to a WebSocket handshake, so the JWT bearer
// scheme also accepts the token from a SignalR connection's query string, the usual ASP.NET Core
// pattern. Only paths under /hubs are affected; REST requests must use the Authorization header.
builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        // Without this, the handler's inbound claim mapping renames "sub" to
        // ClaimTypes.NameIdentifier (and "unique_name" to ClaimTypes.Name), and
        // ClaimsPrincipalExtension.GetUserId, which reads "sub", returns null for a valid token.
        // This keeps claim types as UserClaimsBuilder issued them.
        options.MapInboundClaims = false;

        Func<MessageReceivedContext, Task> previousOnMessageReceived = options
            .Events
            ?.OnMessageReceived;

        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            string accessToken = context.Request.Query["access_token"];

            if (
                !string.IsNullOrEmpty(accessToken)
                && context.HttpContext.Request.Path.StartsWithSegments("/hubs")
            )
                context.Token = accessToken;

            if (previousOnMessageReceived is not null)
                await previousOnMessageReceived(context);
        };
    }
);

// The Server.Core event bus and query-processing pipeline, which every transport registered
// below feeds.
builder.Services.AddNinlilDnsServerCore(builder.Configuration);

// The DNS engine. Registration order matters for the three IDnsQueryResolver implementations:
// DnsQueryDispatcher tries them in registration order, so Authoritative comes before
// Forwarding, which comes before Recursive (see ServiceCollectionForwardingExtension). An
// authoritative answer wins, a configured forwarder is tried next, and iterative resolution is
// the last resort. Tsig and Dnssec come first because the zone-transfer and zone-signing
// services depend on them. Each feature stays behind its Enable flag or per-zone settings;
// registering it only makes it available.
builder.Services.AddNinlilDnsServerTsig(builder.Configuration);

// Cluster membership: the peer registry's invite and join services and the heartbeat engine,
// built on the TSIG keys registered above. All of it stays behind Cluster:Enable, off by default
// (see ClusterOptions); registering it only makes it available.
builder.Services.AddNinlilDnsServerCluster(builder.Configuration);

// An AdvertisedPort of 0 means "the HTTPS port peers reach this node on"; Server.Cluster cannot
// see WebListenOptions, so the port is handed to it here (see ClusterOptions.EffectiveAdvertisedPort).
builder.Services.PostConfigure<ClusterOptions>(options =>
    options.UseHttpsListenPortByDefault(webListenOptions.HttpsPort)
);

// Cluster config sync: the subscribers that send local changes (filter lists, access-control
// rules, rewrites, forwarders, users, roles and so on) to every peer, and the appliers for
// incoming ones. Harmless on a single node, since a broadcast does nothing when clustering is
// off or no peer is known (see ClusterConfigSyncBroadcaster).
builder.Services.AddNinlilDnsServerClusterSync();

// The one applier kept in this project rather than Ninlil.Dns.Server.ClusterSync: applying an
// incoming settings-section change needs RuntimeConfigurationOptions, which only this project
// declares. ClusterConfigSyncSettingsRecorder is the sending side that the EventBroker,
// RateLimiting, DnsCookie and ReverseProxy settings controllers call after a write.
builder.Services.AddScoped<
    Ninlil.Dns.Server.Core.ClusterSync.IClusterConfigSyncApplier,
    Ninlil.Web.ClusterSync.SettingsClusterConfigSyncApplier
>();
builder.Services.AddScoped<Ninlil.Web.ClusterSync.ClusterConfigSyncSettingsRecorder>();

builder.Services.AddNinlilDnsServerDnssec(builder.Configuration);
builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);
builder.Services.AddNinlilDnsServerAcme(builder.Configuration);
builder.Services.AddNinlilDnsServerForwarding(builder.Configuration);

// The access-control engine, registered before its two consumers (the recursion gate in
// Recursive and the bypass check in RateLimiting). Constructor injection does not need that
// order; it only lets a reader see the engine before the places that use it.
builder.Services.AddNinlilDnsServerAccessControl();
builder.Services.AddNinlilDnsServerRecursive(builder.Configuration);
builder.Services.AddNinlilDnsServerFiltering(builder.Configuration);
builder.Services.AddNinlilDnsServerClientSchedule();
builder.Services.AddNinlilDnsServerRewrites(builder.Configuration);
builder.Services.AddNinlilDnsServerRateLimiting(builder.Configuration);
builder.Services.AddNinlilDnsServerQueryLog(builder.Configuration);
builder.Services.AddNinlilDnsServerDnsCrypt(builder.Configuration);

// The configuration-preset engine (Open, Baseline, Protective, Family, FIPS);
// PresetsController is the only way to apply a preset.
builder.Services.AddNinlilDnsServerPresets();

// The self-update checker: a periodic, check-only comparison of the running version with an
// operator-configured release feed, which only ever produces a notice. See SelfUpdateOptions for
// why it never applies updates and has no built-in feed URL.
builder.Services.AddNinlilDnsServerSelfUpdate(builder.Configuration);

// The message-broker event transport. Only the transport DnsServer:Events:Transport names is
// registered: both broker extensions register IDnsServerEventBrokerTransport, so calling both
// would make the last one win. InMemory (the default) registers neither, and
// DnsServerEventPublisher, whose brokerTransport parameter is then unresolved, delivers events
// in-process only.
EventTransportEnum eventTransport =
    builder
        .Configuration.GetSection(DnsServerEventPublisherOptions.SectionName)
        .Get<DnsServerEventPublisherOptions>()
        ?.Transport
    ?? EventTransportEnum.InMemory;
switch (eventTransport)
{
    case EventTransportEnum.RabbitMq:
        builder.Services.AddNinlilDnsServerEventsRabbitMq(builder.Configuration);
        break;
    case EventTransportEnum.ActiveMq:
        builder.Services.AddNinlilDnsServerEventsActiveMq(builder.Configuration);
        break;
    case EventTransportEnum.InMemory:
    default:
        break;
}

// Outbound webhooks: WebhookCredentialProtector, WebhookDeliveryService with its HttpClient,
// and one IDnsServerEventSubscriber<TEvent> per event type in the webhook event catalog.
builder.Services.AddNinlilDnsServerWebhooks(builder.Configuration);

// The DNS transports. Plain DNS (UDP/TCP port 53) has no Enable flag and always starts: it is
// what makes this a DNS server, not a capability-expanding option. DNS-over-TLS, DNS-over-QUIC
// and DNSCrypt check their own Enable flags and do nothing when off, so registering them always
// is safe. DNS-over-HTTPS is different: AddNinlilDnsProtocolDnsOverHttps would host a second
// Kestrel in this process, so only DnsOverHttpsOptions is bound here and its GET and POST
// endpoints are mapped onto the shared Kestrel after Build() below.
builder.Services.AddNinlilDnsProtocolPlain(builder.Configuration);
builder.Services.AddNinlilDnsProtocolDnsOverTls(builder.Configuration);
builder.Services.Configure<DnsOverHttpsOptions>(
    builder.Configuration.GetSection(DnsOverHttpsOptions.SectionName)
);
builder.Services.AddNinlilDnsProtocolDnsOverQuic(builder.Configuration);
builder.Services.AddNinlilDnsProtocolDnsCrypt();

// The DHCPv4 and DHCPv6 engine choices (local engine or relay agent) and the DHCP-to-DNS
// integration live in DhcpServiceRegistration so a test can build them on a plain
// ServiceCollection. The integration is registered in every configuration, so DHCPv6 leases reach
// DNS even when DHCPv4 is relayed. They come after the DHCP repository registration above.
builder.Services.AddNinlilDhcpEngines(builder.Configuration);
builder.Services.AddNinlilDhcpProtocolDhcpv4(builder.Configuration);
builder.Services.AddNinlilDhcpProtocolDhcpv6(builder.Configuration);

// The only service that notices a lease's natural expiry (see Dhcpv4LeaseExpirySweepService).
// It also sweeps DHCPv6 leases, so it is registered always rather than inside the DHCPv4 branch.
builder.Services.AddHostedService<Dhcpv4LeaseExpirySweepService>();

// The engine-independent DHCP services for operators (lease force-release and scope
// statistics), registered always for the same reason: they work only against the DHCP
// repositories, so they do not depend on whether either protocol uses the local engine or a
// relay.
builder.Services.AddNinlilDhcpLeaseAdministration();

// The read-only TFTP server, registered always; it serves only when TftpServerOptions.Enable is
// set. It does not depend on the DHCPv4 choice above: a relay forwarding DHCP elsewhere can still
// serve PXE boot files from the same machine.
builder.Services.AddNinlilDhcpTftpServer(builder.Configuration);

// Router Advertisement, registered always and independent of every DHCP branch: it has no lease
// state and no relay variant. Icmpv6RaListenerHostedService checks RaProtocolOptions.Enable at
// boot, off by default like the other capability-expanding transports, because an unwanted
// Router Advertisement changes the default route and address configuration of every host on the
// link.
builder.Services.AddNinlilDhcpServerRa(builder.Configuration);
builder.Services.AddNinlilDhcpProtocolRa(builder.Configuration);

// IClientIdentityResolver maps a DNS query's source address to a persistent client identifier
// (a MAC address or DUID) for the per-client filtering and parental-control features and the
// per-client query log view. This call registers the ARP/neighbor-table resolver and the
// static-address-mapping resolver. Both work whether or not DHCP is enabled: the kernel fills
// the ARP cache for any device on a directly connected network, and a static mapping is plain
// operator configuration.
builder.Services.AddNinlilDnsServerClientIdentity(builder.Configuration);

// The resolver chain (static mappings, DHCP leases, ARP) is built in DhcpServiceRegistration. The
// DHCP-lease resolver is part of it in every configuration, like the DHCP-to-DNS integration.
builder.Services.AddNinlilClientIdentityResolverChain();

// DnsOverHttpsTransportGate gives DNS-over-HTTPS (mapped onto the shared Kestrel after Build()
// rather than hosting its own) the same start and stop lifecycle as the socket transports, since
// a mapped Minimal API route cannot be unmapped. The routes are always mapped, whatever
// DnsOverHttpsOptions.Enable says at startup, and the gate decides whether a request is
// answered, so TransportSettingsController can turn the transport on as well as off at runtime,
// and ManagedServiceRegistry can wrap it like any other transport.
builder.Services.AddSingleton<DnsOverHttpsTransportGate>();

// ManagedServiceRegistry gives ServicesController a named, categorized handle on every
// transport listener and background maintenance loop. It is a factory-built singleton so
// ManagedServiceRegistry.Build runs only after the host has started each wrapped service once
// (see ManagedServiceRegistry for why that makes wrapping the running instances safe).
builder.Services.AddSingleton<IManagedServiceRegistry>(serviceProvider =>
    ManagedServiceRegistry.Build(serviceProvider)
);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PendingTwoFactorSignInCache>();
builder.Services.AddSingleton<TwoFactorCodeAttemptLimiter>();
builder.Services.AddSingleton<TwoFactorEmailCodeCooldown>();

// Scoped, like IEmailSender and UserManager<TUser>. Used by the EmailLink mode of
// UsersController.ResetPassword and by AuthController.ForgotPassword.
builder.Services.AddScoped<PasswordResetLinkSender>();

// DnsEventsHubBroadcaster is one singleton, registered under its concrete type and under each
// IDnsServerEventSubscriber<TEvent> it implements, all resolving to the same instance, as
// DnsServerEventPublisher is.
builder.Services.AddSingleton<DnsEventsHubBroadcaster>();
builder.Services.AddSingleton<IDnsServerEventSubscriber<DnsQueryReceivedEvent>>(provider =>
    provider.GetRequiredService<DnsEventsHubBroadcaster>()
);
builder.Services.AddSingleton<IDnsServerEventSubscriber<DnsQueryAnsweredEvent>>(provider =>
    provider.GetRequiredService<DnsEventsHubBroadcaster>()
);
builder.Services.AddSingleton<IDnsServerEventSubscriber<DnsZoneChangedEvent>>(provider =>
    provider.GetRequiredService<DnsEventsHubBroadcaster>()
);
builder.Services.AddSingleton<IDnsServerEventSubscriber<DnsCacheEvictedEvent>>(provider =>
    provider.GetRequiredService<DnsEventsHubBroadcaster>()
);

// The hub's payloads need the IPAddress and enum handling the rest of the application uses:
// System.Text.Json cannot write an IPAddress by itself (reading an IPv4 address's ScopeId
// throws), so without the converter DnsQueryReceived and DnsQueryAnswered broadcasts would fail
// to serialize, and the dashboard expects enums (protocol, response code) as names, not numbers.
builder
    .Services.AddSignalR()
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.Converters.Add(new IpAddressJsonConverter());
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// The /status health endpoint: ten checks, one per subsystem, each reading state the subsystem
// already tracks rather than adding another polling layer (see Ninlil.Web.HealthChecks and
// EndpointRouteBuilderStatusExtension.MapNinlilDnsStatus). For example, "cluster" reads the
// peer registry as "forwarders" reads ForwarderHealthTracker, and "webhookDelivery" runs a cheap
// query over the delivery-attempt history WebhookDeliveryRetrySweepService also reads.
builder
    .Services.AddHealthChecks()
    .AddCheck<StorageReachabilityHealthCheck>("storage")
    .AddCheck<TlsCertificateFreshnessHealthCheck>("tlsCertificate")
    .AddCheck<UpstreamForwarderHealthCheck>("forwarders")
    .AddCheck<EventBrokerConnectionHealthCheck>("eventBroker")
    .AddCheck<DnssecSignatureExpiryHealthCheck>("dnssec")
    .AddCheck<TransportListenersHealthCheck>("transports")
    .AddCheck<DiskSpaceHealthCheck>("diskSpace")
    .AddCheck<ClusterHealthCheck>("cluster")
    .AddCheck<WebhookDeliveryHealthCheck>("webhookDelivery")
    .AddCheck<DhcpServiceHealthCheck>("dhcpServices");

// NinlilExceptionHandler turns every exception into an RFC 7807 ProblemDetails response.
// AddProblemDetails() registers the IProblemDetailsService it writes through, which automatic
// model validation ([ApiController]) and status-code pages also use, so all three produce the
// same response shape.
builder.Services.AddExceptionHandler<NinlilExceptionHandler>();
builder.Services.AddProblemDetails();

// MergePatchBodyFilter turns a non-object PATCH body or a wrong-typed field into 400 for every
// action that takes a [FromBody] JsonElement.
builder.Services.AddControllers(options => options.Filters.Add<MergePatchBodyFilter>());
builder.Services.AddEndpointsApiExplorer();

// Registers every IExamplesProvider<T> and IMultipleExamplesProvider<T> in this assembly (the
// Examples folder) with dependency injection, so ExampleFilters() below can resolve the one a
// SwaggerRequestExample or SwaggerResponseExample attribute names.
builder.Services.AddSwaggerExamplesFromAssemblyOf<Program>();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Ninlil.Dns", Version = "v1" });

    string xmlDocumentationPath = Path.Combine(
        AppContext.BaseDirectory,
        $"{Assembly.GetExecutingAssembly().GetName().Name}.xml"
    );
    if (File.Exists(xmlDocumentationPath))
        options.IncludeXmlComments(xmlDocumentationPath);

    // Applies every SwaggerRequestExample and SwaggerResponseExample attribute on the controllers,
    // writing the matching Examples provider's value into the OpenAPI document's "example" and
    // "examples" fields.
    options.ExampleFilters();

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "A JSON Web Token access token issued by POST /api/auth/login.",
        }
    );
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document), [] },
    });
});

WebApplication app = builder.Build();

// The NINLIL_ environment-variable provider runs before any logging exists, so it cannot report
// a variable it did not recognize. It is reported now, through the host's logger, so an operator
// who mistyped a NINLIL_ name (or set one this registry does not know) sees it in the startup log
// instead of the setting being silently ignored. Framework variables
// (NINLIL_ASPNETCORE_ENVIRONMENT and similar) are handled by FrameworkEnvironmentVariableBootstrap,
// not this provider's mapping table, so they are left out rather than reported as typos.
IReadOnlyList<string> unrecognizedApplicationVariableNames =
    ninlilEnvironmentVariableSource
        .Provider?.UnrecognizedVariableNames.Where(name =>
            !FrameworkEnvironmentVariableBootstrap.RecognizedVariableNames.Contains(name)
        )
        .ToList()
    ?? [];

if (unrecognizedApplicationVariableNames.Count > 0)
{
    ILogger startupLogger = app
        .Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger(
            "Ninlil.Web.EnvironmentVariables.NinlilEnvironmentVariablesConfigurationProvider"
        );
    foreach (string unrecognizedName in unrecognizedApplicationVariableNames)
        startupLogger.LogWarning(
            "Environment variable '{VariableName}' starts with the NINLIL_ prefix but does not "
                + "match any known configuration setting -- it was ignored. Check for a typo, or a "
                + "setting this application version does not have.",
            unrecognizedName
        );
}

// The first middleware, ahead of reverse-proxy trust gating, so it catches an exception thrown
// anywhere later, in middleware as well as controllers. With no arguments, UseExceptionHandler()
// dispatches to the IExceptionHandler registered above (NinlilExceptionHandler, which handles
// everything).
app.UseExceptionHandler();

// Reverse-proxy trust gating runs before authentication, static files and every endpoint, so
// each of them sees the client's address and scheme (HttpContext.Connection.RemoteIpAddress and
// HttpContext.Request.Scheme, which UseForwardedHeaders overwrites for a trusted request) rather
// than the proxy's.
if (reverseProxyOptions.Enable)
{
    app.UseMiddleware<ReverseProxyTrustGatingMiddleware>();
    app.UseForwardedHeaders();
}

// [ReadFromReplica] on an action that can receive a write would read stale rows before it writes,
// so the host refuses to start with one.
IReadOnlyList<string> misplacedReplicaReads = ReadFromReplicaEndpointCheck.FindMisplacedActions(
    app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
        .ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
);
if (misplacedReplicaReads.Count > 0)
    throw new InvalidOperationException(
        "[ReadFromReplica] is allowed only on GET actions. Misplaced on: "
            + string.Join("; ", misplacedReplicaReads)
    );

// Settings that are ignored or have no effect, found while the storage topology was composed.
ILogger topologyLogger = app
    .Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Ninlil.Web.Storage");
foreach (string warning in app.Services.GetRequiredService<StorageConnectionStrings>().Warnings)
    topologyLogger.LogWarning("{Message}", warning);

// Apply pending relational migrations at startup, so a fresh installation works without a
// separate migration step, as the auto-generated SQLite connection string of
// AddNinlilDnsEntityFrameworkCore intends. MongoDB is schemaless and has no migrations.
if (storageOptions.Engine != StorageEngineEnum.MongoDb)
{
    await using AsyncServiceScope migrationScope = app.Services.CreateAsyncScope();
    NinlilDnsContext dbContext =
        migrationScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
    try
    {
        await RelationalStartupMigrator.RunAsync(
            dbContext,
            storageOptions,
            app.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(RelationalStartupMigrator).FullName),
            app.Lifetime.ApplicationStopping
        );
    }
    catch (DatabaseStartupRefusedException refusal)
    {
        // The database is in a state this version must not run on. Exit 78 (EX_CONFIG) so the
        // systemd units, which list it in RestartPreventExitStatus, do not restart in a loop.
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RelationalStartupMigrator).FullName)
            .LogCritical(refusal.InnerException, "Startup refused: {Message}", refusal.Message);
        await migrationScope.DisposeAsync();
        await app.DisposeAsync();
        Environment.ExitCode = DatabaseStartupRefusedException.ExitCode;
        return;
    }
}
else
{
    // MongoDB has no migrations. The guard refuses a database a newer version has upgraded,
    // then runs the idempotent backfills and records them (see MongoStartupGuard).
    await using AsyncServiceScope backfillScope = app.Services.CreateAsyncScope();
    ILogger mongoGuardLogger = app
        .Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger(typeof(MongoStartupGuard).FullName);
    try
    {
        await MongoStartupGuard.RunAsync(
            backfillScope.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>(),
            mongoGuardLogger,
            app.Lifetime.ApplicationStopping
        );
    }
    catch (DatabaseStartupRefusedException refusal)
    {
        // Same exit path as the relational guard: 78 stops the systemd restart loop.
        mongoGuardLogger.LogCritical(
            refusal.InnerException,
            "Startup refused: {Message}",
            refusal.Message
        );
        await backfillScope.DisposeAsync();
        await app.DisposeAsync();
        Environment.ExitCode = DatabaseStartupRefusedException.ExitCode;
        return;
    }

    // Multi-document transactions need a replica set; say so once when the server has none.
    if (
        !await app
            .Services.GetRequiredService<MongoTransactionSupport>()
            .DetectAsync(app.Lifetime.ApplicationStopping)
    )
        mongoGuardLogger.LogWarning(
            "MongoDB is a standalone server, so a save that writes several documents is not atomic; "
                + "run a replica set (a single-node one is enough) for all-or-nothing saves."
        );
}

// A storage switch the operator requested is applied now: the configured database has been
// migrated above and no background service has started, so nothing writes while the data is
// copied (see PendingStorageSwitchApplier). MongoDB cannot take part in a switch.
if (storageOptions.Engine != StorageEngineEnum.MongoDb)
{
    try
    {
        await app
            .Services.GetRequiredService<PendingStorageSwitchApplier>()
            .ApplyAsync(app.Lifetime.ApplicationStopping);
    }
    catch (DatabaseStartupRefusedException refusal)
    {
        // The configured target could not be filled; same exit path as the guards above.
        app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(PendingStorageSwitchApplier).FullName)
            .LogCritical(refusal.InnerException, "Startup refused: {Message}", refusal.Message);
        await app.DisposeAsync();
        Environment.ExitCode = DatabaseStartupRefusedException.ExitCode;
        return;
    }
}

// One line says where the data lives now (after any storage switch), without secrets.
topologyLogger.LogInformation(
    "{Topology}",
    StorageTopologyDescription.Describe(
        app.Services.GetRequiredService<StorageOptions>(),
        app.Services.GetRequiredService<StorageConnectionStrings>()
    )
);

// The startup migration or guard and the storage switch are done, and each ran on the primary
// connection. From here a scope that asks for the replica may get it (no effect without a
// configured read replica).
app.Services.GetRequiredService<IStorageReadRouter>().Enable();

// TSIG key names are stored in lowercase; lowercase any row written with capitals (see
// TsigKeyNameNormalizer). It runs on every engine through the repository, after the schema is
// ready, and does nothing once every name is lowercase.
{
    await using AsyncServiceScope tsigNormalizerScope = app.Services.CreateAsyncScope();
    int normalizedTsigKeys = await TsigKeyNameNormalizer.RunAsync(
        tsigNormalizerScope.ServiceProvider.GetRequiredService<ITsigKeyRepository>(),
        tsigNormalizerScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
        app.Logger
    );
    if (normalizedTsigKeys > 0)
        app.Logger.LogInformation("Lowercased the names of {Count} TSIG keys.", normalizedTsigKeys);
}

// Client identifiers (DHCP reservations, client addresses, static address mappings) are stored
// in one normal form; rewrite rows stored in another form (see StoredClientIdentifierNormalizer).
// It runs on every engine through the repositories, on the primary, and does nothing once every
// row is in normal form.
{
    await using AsyncServiceScope identifierNormalizerScope = app.Services.CreateAsyncScope();
    int normalizedIdentifiers = await StoredClientIdentifierNormalizer.RunAsync(
        identifierNormalizerScope.ServiceProvider.GetRequiredService<IDhcpReservationRepository>(),
        identifierNormalizerScope.ServiceProvider.GetRequiredService<IDnsClientAddressRepository>(),
        identifierNormalizerScope.ServiceProvider.GetRequiredService<IDnsClientStaticAddressMappingRepository>(),
        identifierNormalizerScope.ServiceProvider.GetRequiredService<IUnitOfWork>(),
        app.Logger
    );
    if (normalizedIdentifiers > 0)
        app.Logger.LogInformation(
            "Normalised the client identifier of {Count} stored rows.",
            normalizedIdentifiers
        );
}

await AdministratorBootstrapper.EnsureBootstrapAdministratorAsync(app.Services, app.Configuration);

// Warn, without blocking or changing startup, about a listener configured on a privileged port
// (1023 or below) that this process cannot bind. It runs here, after the host's logger exists
// and the transport options are bound, but before any transport's BackgroundService tries to
// bind, so the operator sees an actionable notice before the listener's raw SocketException
// (see PrivilegedPortStartupDiagnostics, including why it uses a live bind probe).
PrivilegedPortStartupDiagnostics.WarnAboutUnbindablePrivilegedPorts(
    PrivilegedPortStartupDiagnostics.BuildConfiguredListeners(
        webListenOptions,
        app.Services.GetRequiredService<IOptions<PlainProtocolOptions>>().Value,
        app.Services.GetRequiredService<IOptions<DnsOverTlsOptions>>().Value,
        app.Services.GetRequiredService<IOptions<DnsOverQuicOptions>>().Value,
        app.Services.GetRequiredService<IOptions<DnsCryptOptions>>().Value
    ),
    new SocketPrivilegedPortBindProbe(),
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Ninlil.Web.Startup.PrivilegedPortStartupDiagnostics")
);

// Warn, without blocking startup, about setting combinations that are accepted but have no
// effect (see ConfigurationStartupDiagnostics).
ConfigurationStartupDiagnostics.LogWarnings(
    app.Services.GetRequiredService<IOptions<RecursiveServerOptions>>().Value,
    app.Services.GetRequiredService<IOptions<EmailSenderOptions>>().Value,
    app.Services.GetRequiredService<IOptions<OidcSsoOptions>>().Value,
    app.Services.GetRequiredService<IDataProtectionProvider>(),
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Ninlil.Web.Startup.ConfigurationStartupDiagnostics")
);

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/documentation", () => Results.Content(ReDocPage.Html, "text/html"));

// Serves the Angular admin console (src/angular) as static files from wwwroot. The
// NinlilBuildAngularAdminSpa target in Ninlil.Web.csproj builds the Angular workspace and copies
// its output there during an ordinary dotnet build or publish (unless NinlilSkipAngularBuild is
// set), so no separate front-end step is needed. This comes before authentication and
// authorization: the console's static files (bundles and index.html) are public, like Swagger UI
// above. Every data endpoint the console calls is still protected, and the console's route
// guards decide what a page shows once it has loaded.
//
// A single-file release binary carries the console inside the assembly (see NinlilEmbedWebRoot in
// Ninlil.Web.csproj) instead of a wwwroot directory. When there is no index.html on disk and the
// assembly has an embedded-file manifest, the embedded copy is served; UseDefaultFiles,
// UseStaticFiles and the fallback below all read the same web root provider. A wwwroot directory
// on disk always wins, so an operator can still replace the console.
if (!app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
{
    Assembly hostAssembly = typeof(Program).Assembly;
    if (
        hostAssembly
            .GetManifestResourceNames()
            .Contains("Microsoft.Extensions.FileProviders.Embedded.Manifest.xml")
    )
        app.Environment.WebRootFileProvider = new ManifestEmbeddedFileProvider(
            hostAssembly,
            "wwwroot"
        );
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// After authentication, so the user is known, and before the endpoint, so the request scope has
// not resolved a database context yet. See StorageReadIntentMiddleware.
app.UseMiddleware<StorageReadIntentMiddleware>();

// The admin sign-in limiter policies (ServiceCollectionAdminLoginRateLimitingExtension's
// AuthenticationEndpointPolicyName and PasswordResetEndpointPolicyName) apply only where
// [EnableRateLimiting] names them (AuthController.Login, TwoFactorLogin and ForgotPassword); this
// middleware is what enforces that attribute.
app.UseRateLimiter();

app.MapControllers();

// CloseOnAuthenticationExpiration ends a hub connection when the access token it opened with
// expires, so the client reconnects with its current token and the permission groups
// DnsEventsHub.OnConnectedAsync puts it in never outlive the permissions that token carried.
app.MapHub<DnsEventsHub>("/hubs/events", options => options.CloseOnAuthenticationExpiration = true);
app.MapNinlilDnsOverHttps();
app.MapNinlilDnsStatus();

// Sends any request that matches no endpoint and no file under wwwroot to the console's
// index.html, so a deep link (a browser opening /zones/123 directly) reaches the Angular router
// instead of a 404.
//
// The first negative lookahead excludes the server's reserved top-level paths (the REST API,
// Swagger and ReDoc, the SignalR hubs, the RFC 8484 DNS-over-HTTPS path and the status endpoint),
// each matched as a whole path segment (followed by "/" or the end), so a console route that only
// starts with one of these words is not caught. Without it, an unmatched /api/... request (a
// typo, a removed endpoint) would get the console's HTML instead of a 404. The list names the
// literal "dns-query" rather than reading DnsOverHttpsOptions.Path: the DNS-over-HTTPS routes
// are mapped at the configured path and are matched before this fallback anyway, so only a
// renamed path plus a console route called "dns-query" would notice.
//
// The second negative lookahead excludes any request whose last segment contains a dot, which
// looks like a static file rather than a console route. UseStaticFiles above serves files that
// exist, so this fallback only sees a missing one, most often a cached index.html asking for a
// content-hashed bundle that a rebuild replaced. Serving index.html for it would return 200 with
// text/html, which the browser rejects for a script or stylesheet; a 404 is clearer. Console
// routes never end in a dotted segment (identifiers are plain ids or slugs), so no route is
// mistaken for a file.
app.MapFallbackToFile(
    "{*path:regex(^(?!(api|swagger|documentation|hubs|dns-query|status)(/|$))(?!.*\\.[^/]+$).*$)}",
    "index.html"
);

app.Run();

/// <summary>
///     This class holds the ReDoc documentation page served at <c>/documentation</c>: plain HTML
///     that loads ReDoc's standalone script from its CDN and points it at the generated OpenAPI
///     document, which is all ReDoc needs, so no NuGet package is used.
/// </summary>
internal static class ReDocPage
{
    public const string Html = """
        <!DOCTYPE html>
        <html lang="en">
          <head>
            <meta charset="utf-8" />
            <title>Ninlil.Dns API Documentation</title>
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <style>body { margin: 0; padding: 0; }</style>
          </head>
          <body>
            <redoc spec-url="/swagger/v1/swagger.json"></redoc>
            <script src="https://cdn.jsdelivr.net/npm/redoc@2/bundles/redoc.standalone.js"></script>
          </body>
        </html>
        """;
}

/// <summary>
///     This partial class declaration lets <c>WebApplicationFactory&lt;Program&gt;</c> in the test
///     project refer to the entry-point type of this top-level-statement program.
/// </summary>
public partial class Program;
