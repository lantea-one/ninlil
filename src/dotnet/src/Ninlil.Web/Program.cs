using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Ninlil.Common.Configuration;
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
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Extension;
using Ninlil.Dns.Data.MongoDb.Maintenance;
using Ninlil.Dns.Identity.Extension;
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
using Ninlil.Web.Services;
using Ninlil.Web.Services.Storage;
using Ninlil.Web.Startup;
using Swashbuckle.AspNetCore.Filters;

// NINLIL_DOTNET_* runtime settings (server GC and similar) are read by the native runtime before
// any managed code runs, so the only way to apply them is to relaunch the process with their real
// DOTNET_* names in its environment -- done first, before any other startup work is wasted on a
// process image that is about to be replaced. See NativeRuntimeSettingsRelaunch.
NativeRuntimeSettingsRelaunch.Apply();

// This must run before anything else below, including WebApplication.CreateBuilder(args)
// immediately after it -- that single call is what actually reads every framework-level
// environment variable this bootstrap step redirects (ASPNETCORE_ENVIRONMENT, ASPNETCORE_URLS,
// and so on), so the real variable it reads has to already hold the right value by the time it
// runs. See FrameworkEnvironmentVariableBootstrap's own doc comment for exactly which variables
// this covers.
FrameworkEnvironmentVariableBootstrap.Apply();

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Every environment variable this application itself reads -- both this project's own
// Options-bound configuration and (via the bootstrap step above) the framework-level variables
// ASP.NET Core's own host-building code reads -- uses a single, consistent NINLIL_-prefixed,
// single-underscore-only naming scheme; ASP.NET Core's own default double-underscore
// EnvironmentVariablesConfigurationProvider (added with no prefix filter as part of
// CreateBuilder's own default configuration chain above) is removed and replaced with this
// project's own provider, built from a reflection-generated mapping table covering every real
// bound Options class in OptionsEnvironmentVariableRegistry.Roots. The replacement is inserted at
// the same position the *last* (highest-precedence, closest to the end of the source list, since
// a later source overrides an earlier one) removed provider held, preserving its original
// precedence relative to appsettings.json/appsettings.{Environment}.json (still overridden by it)
// and command-line arguments (still overriding it), exactly as the default double-underscore
// provider's own precedence always was -- CreateBuilder's default chain can register more than
// one EnvironmentVariablesConfigurationSource (an early ASPNETCORE_-prefixed one feeding host
// configuration, and the real unprefixed one immediately before command-line arguments), and only
// the position of the *last* one is the one that actually matters here.
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
    // Every source before this one in the filtered list above sits at a lower original index
    // (Where/ToList preserves original order), so removing all of them below shifts this one's
    // own original index down by exactly that many positions.
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

// Every path this host generates or persists on its own (an auto-generated SQLite database, the
// Data Protection key ring, an auto-generated JWT signing key) lives in one shared "data"
// directory next to the application.
string dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDirectory);

// This directory holds the SQLite database, the Data Protection key ring, and the JWT signing
// key -- restrict it to this process's own owner, the same filesystem-permission-only posture
// this project already uses for its self-signed TLS certificate and DNSCrypt provider keys,
// since Data Protection's own key ring is not encrypted at rest on Linux without a certificate
// this project has no reason to require an operator to provide.
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(
        dataDirectory,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
    );

// Data Protection's own key ring must survive a restart -- without this, every previously
// encrypted secret (TOTP authenticator keys, TSIG secrets, DNSSEC keys, cloud provider
// credentials, the ACME account key) becomes permanently undecryptable the moment this process
// restarts and generates a fresh, ephemeral key of its own instead. Registered before
// AddNinlilDnsIdentity's own (unconfigured) AddDataProtection() call so that this
// configuration wins -- AddDataProtection() is safe to call more than once.
builder
    .Services.AddDataProtection()
    .SetApplicationName("Ninlil.Dns")
    .PersistKeysToFileSystem(new DirectoryInfo(dataDirectory));

// A signing key is required to issue or validate a JSON Web Token; generate and persist one
// automatically so a fresh installation works without any configuration at all -- the same
// "works out of the box" philosophy AddNinlilDnsEntityFrameworkCore's own auto-generated
// SQLite connection string already follows -- and so a restart does not invalidate every
// previously-issued token.
string signingKey = builder.Configuration["Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(signingKey))
{
    string signingKeyFilePath = Path.Combine(dataDirectory, "jwt-signing-key.txt");

    if (File.Exists(signingKeyFilePath))
    {
        signingKey = File.ReadAllText(signingKeyFilePath).Trim();
    }
    else
    {
        signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        File.WriteAllText(signingKeyFilePath, signingKey);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                signingKeyFilePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
            );
    }

    builder.Configuration["Jwt:SigningKey"] = signingKey;
}

// This node's own stable cluster identity -- generated once, automatically, and persisted so it
// survives a restart, the same "works out of the box, and stays stable across a restart"
// convention the JWT signing key immediately above already establishes. See ClusterOptions.NodeId's
// own doc comment for why this value is never operator-editable through the REST API: every peer
// that joins with this node records it as their own registry row's own RemoteNodeId, so changing
// it later would make every peer's own record stale.
if (string.IsNullOrWhiteSpace(builder.Configuration["Cluster:NodeId"]))
{
    string nodeIdFilePath = Path.Combine(dataDirectory, "cluster-node-id.txt");

    string nodeId = File.Exists(nodeIdFilePath)
        ? File.ReadAllText(nodeIdFilePath).Trim()
        : Guid.NewGuid().ToString();

    if (!File.Exists(nodeIdFilePath))
        File.WriteAllText(nodeIdFilePath, nodeId);

    builder.Configuration["Cluster:NodeId"] = nodeId;
}

// This host's own Kestrel HTTP/HTTPS binding -- shared by the REST API, the SignalR hub, and
// (mapped below, after Build()) DNS-over-HTTPS. The HTTPS certificate is loaded through the same
// ReloadableTlsCertificateProvider every other TLS-based transport in this process uses, so an
// operator (or an ACME renewal) replacing the underlying certificate file updates every one of
// them -- this endpoint, DoT, DoQ -- with no restart, as long as each is pointed at the same
// PfxPath (or, as is true by default, all of them resolve to the exact same self-signed
// certificate state path already).
WebListenOptions webListenOptions =
    builder.Configuration.GetSection(WebListenOptions.SectionName).Get<WebListenOptions>()
    ?? new WebListenOptions();

// No dependency injection container exists yet at this point in Program.cs -- this is a
// throwaway bootstrap logger, used only for this provider's own certificate load/reload
// messages until the real host (and its own configured logging providers) exists.
ILogger kestrelCertificateLogger = LoggerFactory
    .Create(logging => logging.AddConsole())
    .CreateLogger("Ninlil.Web.Kestrel.Tls");

ReloadableTlsCertificateProvider kestrelCertificateProvider = new(
    webListenOptions.Certificate,
    kestrelCertificateLogger,
    webListenOptions.Certificate.CertificateReloadPollInterval
);

// Registered so the /status endpoint's own TlsCertificateFreshnessHealthCheck can reuse this
// exact provider -- the same certificate DoT/DoQ/Kestrel itself all already share -- rather
// than constructing (and separately polling) a second instance of its own.
builder.Services.AddSingleton(kestrelCertificateProvider);

// A full-system backup bundles the exact same certificate file Kestrel/DoT/DoQ all already share,
// resolved through this same TlsCertificateOptions instance's own PfxPath/SelfSignedCertificateStatePath
// pair -- register it directly so SystemBackupService can resolve that path without duplicating
// the "an operator-provided PFX wins over the self-signed state file" logic a second time.
builder.Services.AddSingleton(webListenOptions.Certificate);

// WebListenSettingsController reads this exact instance back -- the values Kestrel was actually
// bound with above, not a freshly re-parsed one -- since WebListenOptions is read exactly once,
// before Build(), and can never be "live" the way every other settings controller's own options
// class is; see that controller's own doc comment for the full reasoning.
builder.Services.AddSingleton(webListenOptions);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Listen(IPAddress.IPv6Any, webListenOptions.HttpPort);
    kestrel.Listen(
        IPAddress.IPv6Any,
        webListenOptions.HttpsPort,
        listenOptions =>
            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificateSelector = (_, _) =>
                    kestrelCertificateProvider.Current;
                httpsOptions.SslProtocols = webListenOptions.Certificate.AllowedProtocols;
            })
    );
});

// Reverse-proxy trust for this same shared Kestrel listener -- off by default (invariant #6, a
// capability-expanding toggle), so a request's forwarded headers are honored only once an
// operator explicitly opts in with a real reverse proxy's own address/network allowlist.
ReverseProxyOptions reverseProxyOptions =
    builder.Configuration.GetSection(ReverseProxyOptions.SectionName).Get<ReverseProxyOptions>()
    ?? new ReverseProxyOptions();
builder.Services.Configure<ReverseProxyOptions>(
    builder.Configuration.GetSection(ReverseProxyOptions.SectionName)
);

if (reverseProxyOptions.Enable)
{
    // No dependency injection container exists yet -- the same throwaway bootstrap logger
    // convention kestrelCertificateLogger above already establishes, used only to warn about a
    // malformed allowlist entry a non-REST edit of the configuration file may have introduced
    // (ReverseProxySettingsController's own PATCH action already validates every entry before
    // persisting it, so this should never actually fire against configuration this host's own
    // REST API wrote).
    ILogger reverseProxyBootstrapLogger = LoggerFactory
        .Create(logging => logging.AddConsole())
        .CreateLogger("Ninlil.Web.ReverseProxy");

    // ForwardedHeadersMiddleware's own KnownProxies/KnownIPNetworks allowlist is the single
    // source of truth ReverseProxyTrustGatingMiddleware itself reads back (see that class's own
    // doc comment) -- computed once, here, from this same ReverseProxyOptions instance, so the
    // two middlewares can never disagree about which peers are trusted. The actual population
    // logic (including clearing this framework's own pre-trusted-loopback defaults first) lives
    // in ReverseProxyForwardedHeadersConfigurator, not inline here, specifically so it can be
    // exercised directly by a permanent test -- see that class's own doc comment.
    builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersOptions =>
        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            reverseProxyOptions,
            reverseProxyBootstrapLogger
        )
    );
}

// Select and register whichever storage engine Storage:Engine configures -- MongoDB has its own,
// independent implementation of every repository; every relational engine is served by Entity
// Framework Core against the same provider-agnostic model.
StorageOptions storageOptions =
    builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? new StorageOptions();

if (storageOptions.Engine == StorageEngineEnum.MongoDb)
    builder.Services.AddNinlilDnsMongoDb(builder.Configuration);
else
    builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);

// DHCP's own repositories are mapped entity types within that same shared context/client, never
// a second storage engine of their own (see NinlilDnsContext's own doc comment) -- registering
// them right here, unconditionally, right after whichever DNS-side call above already registered
// the shared context/client itself, mirrors the exact two-call composition order both
// AddNinlilDhcpEntityFrameworkCore and AddNinlilDhcpMongoDb document as their own precondition.
// This registration is harmless on its own regardless of whether the DHCP engine is actually
// enabled (Dhcpv4ProtocolOptions.Enable) -- it only adds repository/DI wiring, never opens a
// socket or otherwise changes this process's own externally-visible behavior.
if (storageOptions.Engine == StorageEngineEnum.MongoDb)
    builder.Services.AddNinlilDhcpMongoDb();
else
    builder.Services.AddNinlilDhcpEntityFrameworkCore();

// The JSON file this host's own runtime settings controllers (PresetsController,
// EventBrokerSettingsController, RateLimitingSettingsController, DnsCookieSettingsController)
// merge a write into -- defaults to this host's own appsettings.json, next to the running
// application, but is itself overridable so a test can point it at an isolated file instead.
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

// This host's own global default appearance (palette/mode) -- AppearanceSettingsController reads
// this back via IOptionsMonitor, not the usual IOptions, so a write this controller merges into
// the runtime configuration file above takes effect immediately, with no restart required. See
// AppearanceOptions's own doc comment for why that is safe specifically for this options class.
builder.Services.Configure<AppearanceOptions>(
    builder.Configuration.GetSection(AppearanceOptions.SectionName)
);

// This host's own global branding (site title/logo) -- BrandingSettingsController reads this back
// via IOptionsMonitor, the same shape as AppearanceOptions above and for the same reason: branding
// has zero server-side runtime effect beyond that controller (and the Angular SPA's own read of
// it) reading the value back out.
builder.Services.Configure<BrandingOptions>(
    builder.Configuration.GetSection(BrandingOptions.SectionName)
);

// A full-system backup covers the same data directory (the Data Protection key ring) and the
// same runtime configuration file every other part of this host already resolves its own path
// for -- forced here, after binding, rather than left independently configurable, so this
// project never has two different sources of truth for either path.
builder.Services.AddNinlilDnsServerBackup(builder.Configuration);
builder.Services.PostConfigure<BackupOptions>(options =>
{
    options.DataProtectionKeyRingDirectory = dataDirectory;
    options.RuntimeConfigurationFilePath = runtimeConfigurationOptions.ConfigurationFilePath;
});

// The live storage-engine switch: a real, in-process mechanism that mutates the
// exact StorageOptions singleton AddNinlilDnsEntityFrameworkCore/AddNinlilDnsMongoDb
// registered above in place, rather than any Docker/systemd/process-respawn trick -- see
// StorageEngineSwitchService's own doc comment for exactly how and why that is sufficient.
// Registered as scoped, matching IBackupService's own lifetime, which it depends on directly;
// StorageEngineSwitchState is the one piece of this feature that must be a singleton, since it
// is the in-progress gate shared across every request, including by StorageSwitchGateMiddleware.
builder.Services.AddSingleton<StorageEngineSwitchState>();
builder.Services.AddScoped<IStorageEngineSwitchService, StorageEngineSwitchService>();

builder.Services.AddNinlilDnsIdentity(builder.Configuration);
builder.Services.AddNinlilDnsAuthentication(builder.Configuration);

// Layered onto the same AuthenticationBuilder AddNinlilDnsAuthentication just built, adding one
// additional scheme (ClusterTsigAuthenticationDefaults.AuthenticationScheme) without touching
// the DefaultScheme/DefaultChallengeScheme/JWT-or-API-token forwarding selection that call
// already configured -- see AddNinlilDnsServerClusterAuthentication's own doc comment.
builder.Services.AddNinlilDnsServerClusterAuthentication();

builder.Services.AddNinlilDnsAuthorization();

// Genuinely distinct from, and independent of, Server.RateLimiting's own DNS-query rate limiter
// registered further below (AddNinlilDnsServerRateLimiting) -- see
// AdminLoginRateLimitOptions's own doc comment for the full "fails closed, not open" reasoning
// this admin sign-in limiter deliberately follows instead.
builder.Services.AddNinlilDnsAdminLoginRateLimiting(builder.Configuration);

// A browser cannot attach an Authorization header to a WebSocket handshake -- let the JWT
// bearer scheme also accept the token from a SignalR hub connection's own query string, the
// standard ASP.NET Core SignalR + JWT integration pattern. Scoped to paths under /hubs only, so
// every ordinary REST request still must use the real Authorization header.
builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        // Without this, the handler's own legacy inbound claim mapping silently renames "sub"
        // to ClaimTypes.NameIdentifier (and "unique_name" to ClaimTypes.Name) on the resulting
        // ClaimsPrincipal -- found only by a real end-to-end smoke test, not by inspection, when
        // every endpoint reading the current user's own "sub" claim directly
        // (ClaimsPrincipalExtension.GetUserId) returned null despite a genuinely valid,
        // successfully authenticated token. Disabling it keeps claim types exactly as
        // UserClaimsBuilder issued them.
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

// Server.Core's own internal event bus and query-processing pipeline -- every transport
// registered below feeds this same pipeline.
builder.Services.AddNinlilDnsServerCore(builder.Configuration);

// The DNS engine itself. Registration order matters for the three IDnsQueryResolver
// implementations specifically -- DnsQueryDispatcher tries them in dependency-injection
// registration order, so Authoritative must come before Forwarding, which must come before
// Recursive (see ServiceCollectionForwardingExtension's own doc comment) -- an authoritative
// answer always wins, a configured forwarder is tried next, and this server's own iterative
// resolution is the final fallback. Tsig and Dnssec are registered first since Authoritative's
// own zone-transfer and zone-signing services depend on them. Every one of these stays gated by
// its own existing Enable flag or per-zone configuration; registering them here only makes the
// capability available, it does not turn any of them on by default.
builder.Services.AddNinlilDnsServerTsig(builder.Configuration);

// The cluster membership foundation: the peer registry's own invite/join services and
// background heartbeat engine, all built on the TSIG key infrastructure just registered above.
// Everything here stays gated by Cluster:Enable (off by default -- see ClusterOptions's own
// doc comment); registering it unconditionally only makes the capability available.
builder.Services.AddNinlilDnsServerCluster(builder.Configuration);

// The cluster's own operational config sync -- the appliers/subscribers that forward a local
// filter-list/access-control-rule/rewrite/forwarder change to every cluster peer over the
// channel just registered above, and apply an incoming one locally. Registering this
// unconditionally is harmless on a single-node deployment: every subscriber's own broadcast is a
// cheap no-op whenever ClusterOptions.Enable is false or no peer is currently known (see
// ClusterConfigSyncBroadcaster's own doc comment).
builder.Services.AddNinlilDnsServerClusterSync();

// The one applier the cluster config-sync convention keeps here rather than in
// Ninlil.Dns.Server.ClusterSync: applying an incoming settings-section change needs
// RuntimeConfigurationOptions, which only this project declares. ClusterConfigSyncSettingsRecorder
// is the matching sending-side helper the four covered settings controllers
// (EventBroker/RateLimiting/DnsCookie/ReverseProxy) call after a write.
builder.Services.AddScoped<
    Ninlil.Dns.Server.Core.ClusterSync.IClusterConfigSyncApplier,
    Ninlil.Web.ClusterSync.SettingsClusterConfigSyncApplier
>();
builder.Services.AddScoped<Ninlil.Web.ClusterSync.ClusterConfigSyncSettingsRecorder>();

builder.Services.AddNinlilDnsServerDnssec(builder.Configuration);
builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);
builder.Services.AddNinlilDnsServerAcme(builder.Configuration);
builder.Services.AddNinlilDnsServerForwarding(builder.Configuration);

// The real per-client/network access-control engine -- registered before both of
// its confirmed consumers (Recursive's own recursion-gate check and RateLimiting's own bypass
// check below), even though ordinary constructor-injected singleton resolution does not actually
// require registration order for a single interface like IDnsAccessControlEngine; kept here
// purely so a reader sees the engine defined before the two places that consult it.
builder.Services.AddNinlilDnsServerAccessControl();
builder.Services.AddNinlilDnsServerRecursive(builder.Configuration);
builder.Services.AddNinlilDnsServerFiltering(builder.Configuration);
builder.Services.AddNinlilDnsServerClientSchedule();
builder.Services.AddNinlilDnsServerRewrites(builder.Configuration);
builder.Services.AddNinlilDnsServerRateLimiting(builder.Configuration);
builder.Services.AddNinlilDnsServerQueryLog(builder.Configuration);
builder.Services.AddNinlilDnsServerDnsCrypt(builder.Configuration);

// The configuration-preset engine (Open/Baseline/Protective/Family/FIPS) -- this host is
// the first real invocation surface PresetApplier has ever had; see PresetsController.
builder.Services.AddNinlilDnsServerPresets();

// The self-update checker: a periodic, check-only comparison of this host's own running
// version against an operator-configured release feed -- never anything more than a real,
// visible notice. See SelfUpdateOptions's own doc comment for the full "check-only, forever"
// reasoning and why this project deliberately does not hardcode an assumed release-feed URL.
builder.Services.AddNinlilDnsServerSelfUpdate(builder.Configuration);

// The message-broker event transport -- only the specific transport DnsServer:Events
// :Transport actually names gets registered at all, so IDnsServerEventBrokerTransport never ends
// up bound to whichever of RabbitMq/ActiveMq's own extensions happened to run last (both
// extensions register the same interface; calling both unconditionally would make that a
// meaningless last-registration-wins race). Selecting InMemory (the default) registers neither,
// which is exactly equivalent to never referencing either broker project at all --
// DnsServerEventPublisher's own optional brokerTransport constructor parameter simply stays
// unresolved, and it already falls back to purely in-process delivery in that case.
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

// The outbound subscribable webhooks feature, fully wired end to end -- schema/event-catalog/REST
// management surface plus the real HTTP delivery engine: WebhookCredentialProtector,
// WebhookDeliveryService and its own dedicated HttpClient, and one
// IDnsServerEventSubscriber<TEvent> registration per curated "noteworthy"
// event type (DnsZoneChanged, DnsCacheEvicted, DnssecZoneResigningCompleted,
// AcmeCertificateRenewalSucceeded/Failed, CloudDnsZoneSyncFailed, TrustAnchorRollover).
builder.Services.AddNinlilDnsServerWebhooks(builder.Configuration);

// Every DNS transport. Plain DNS (UDP/TCP port 53) is the one exception to every other
// transport's own off-by-default Enable flag -- it is the baseline expectation of "this is a
// DNS server," not a capability-expanding toggle, so PlainProtocolOptions has no Enable flag of
// its own at all and this always starts. DoT/DoQ/DnsCrypt each check their own Enable flag
// internally and simply do nothing when disabled, so registering them here unconditionally is
// safe regardless of an operator's actual configuration. DNS-over-HTTPS is deliberately
// different: instead of AddNinlilDnsProtocolDnsOverHttps (which would self-host a second,
// separate Kestrel instance in this same process), only DnsOverHttpsOptions itself is bound
// here -- the actual GET/POST endpoints are mapped onto this host's own shared Kestrel further
// down, after Build().
builder.Services.AddNinlilDnsProtocolPlain(builder.Configuration);
builder.Services.AddNinlilDnsProtocolDnsOverTls(builder.Configuration);
builder.Services.Configure<DnsOverHttpsOptions>(
    builder.Configuration.GetSection(DnsOverHttpsOptions.SectionName)
);
builder.Services.AddNinlilDnsProtocolDnsOverQuic(builder.Configuration);
builder.Services.AddNinlilDnsProtocolDnsCrypt();

// The DHCPv4 UDP transport is registered unconditionally the same way as every other optional
// transport above: Dhcpv4ProtocolOptions.Enable is checked once, at boot, by the thin
// Dhcpv4ListenerHostedService wrapper -- the real, genuinely restartable listener itself
// (Dhcpv4UdpListener) stays independently startable/stoppable for the rest of the process's life
// regardless of that flag (see IDhcpProtocolService's own doc comment) -- off by default per this
// project's own "capability-expanding toggles default off" invariant -- a second DHCP server
// sharing a network with an existing one is a genuinely disruptive misconfiguration, not merely a
// passive feature an operator might casually leave on.
//
// Exactly which IDhcpv4RequestHandler implementation answers a decoded request, however, is a
// genuine either/or choice this listener depends on, never both at once: this server can either
// answer a request itself from local scope/lease state (Ninlil.Dhcp.Server.Core) or forward it to
// an upstream DHCPv4 server as an RFC 3046 relay agent (Ninlil.Dhcp.Server.Relay) -- a real host
// does not, on the same listening instance, both answer authoritatively AND forward to someone
// else for the same request. Dhcpv4RelayOptions.Enable is therefore read directly here, before
// dependency injection is built, exactly mirroring how DnsServerEventPublisherOptions.Transport is
// read above to decide which single message-broker transport to register -- registering both
// engines and letting whichever one dependency injection resolves last silently win would be the
// same undocumented "last registration wins" footgun that precedent already avoids. Either branch
// must still run after this process's own DHCP repository registration above, since
// AddNinlilDhcpServerCore's own services depend directly on Ninlil.Dhcp.Data.Abstractions's
// repository interfaces (AddNinlilDhcpv4Relay depends on no repository at all).
bool dhcpv4RelayEnabled =
    builder
        .Configuration.GetSection(Dhcpv4RelayOptions.SectionName)
        .Get<Dhcpv4RelayOptions>()
        ?.Enable
    ?? false;

if (dhcpv4RelayEnabled)
{
    builder.Services.AddNinlilDhcpv4Relay(builder.Configuration);
}
else
{
    builder.Services.AddNinlilDhcpServerCore(builder.Configuration);

    // DHCP->DNS integration (registering a granted lease's own host name as a real A/AAAA
    // record, per DhcpScopeEntity.DnsZoneId) only makes sense alongside the local lease
    // allocation engine above -- a relay agent never grants a lease of its own, so it publishes
    // no DhcpLeaseChangedEvent for this feature to ever react to, and registering it in that
    // branch too would just be dead weight.
    builder.Services.AddNinlilDhcpDnsIntegration();
}

builder.Services.AddNinlilDhcpProtocolDhcpv4(builder.Configuration);

// The DHCPv6 UDP transport is registered unconditionally, independently of the DHCPv4
// relay-vs-local branch above -- Dhcpv6ProtocolOptions.Enable is checked once, at boot, by the
// thin Dhcpv6ListenerHostedService wrapper, mirroring Dhcpv4ListenerHostedService exactly, off by
// default for the same reason Dhcpv4ProtocolOptions.Enable is.
// Exactly which IDhcpv6RequestHandler/IDhcpv6RelayMessageHandler implementation answers a decoded
// message is its own genuine either/or choice, mirroring the DHCPv4 branch immediately above --
// this server can either answer a request itself from local scope/lease state
// (Ninlil.Dhcp.Server.Core) or forward it to an upstream DHCPv6 server as a single-hop RFC 8415
// relay agent (Ninlil.Dhcp.Server.Relay). Dhcpv6RelayOptions.Enable is therefore read directly
// here too, before dependency injection is built, for the exact same reason
// Dhcpv4RelayOptions.Enable already is -- this is a genuinely separate, independent choice from
// the DHCPv4 branch above, since the two protocols' own relay-vs-local decisions are not coupled
// to each other in any way.
bool dhcpv6RelayEnabled =
    builder
        .Configuration.GetSection(Dhcpv6RelayOptions.SectionName)
        .Get<Dhcpv6RelayOptions>()
        ?.Enable
    ?? false;

if (dhcpv6RelayEnabled)
{
    builder.Services.AddNinlilDhcpv6Relay(builder.Configuration);
}
else
{
    builder.Services.AddNinlilDhcpv6ServerCore(builder.Configuration);
}

builder.Services.AddNinlilDhcpProtocolDhcpv6(builder.Configuration);

// The one mechanism that ever detects a DHCP lease's own natural expiry (see its own doc comment)
// -- registered here, unconditionally, rather than nested inside the DHCPv4 relay-vs-local branch
// above, since it sweeps DHCPv6 leases too and must therefore run regardless of which specific
// DHCPv4 engine variant (if any) is currently active.
builder.Services.AddHostedService<Dhcpv4LeaseExpirySweepService>();

// The two protocol-agnostic, operator-facing DHCP services (lease force-release, scope
// statistics) -- registered unconditionally, right alongside the sweep service immediately
// above, for the identical reason: both operate purely against Ninlil.Dhcp.Data.Abstractions's
// own repositories, never against a specific engine instance, so neither depends on which of the
// two mutually exclusive DHCPv4/DHCPv6 engine variants (local or relay) is currently active for
// either protocol.
builder.Services.AddNinlilDhcpLeaseAdministration();

// This server's own bundled, read-only TFTP server -- registered unconditionally (it checks its
// own TftpServerOptions.Enable flag internally, the same "registered always, gated at runtime"
// precedent every other optional transport in this project already establishes), and completely
// independent of which of the two mutually exclusive DHCPv4 engines above is active: a relay
// agent forwarding DHCP elsewhere can still plausibly serve PXE boot files from this same box.
builder.Services.AddNinlilDhcpTftpServer(builder.Configuration);

// Router Advertisement -- registered unconditionally too, and independent of every DHCP branch
// above: unlike DHCPv4/DHCPv6, this has no per-client lease state and no relay-agent variant at
// all, so AddNinlilDhcpServerRa/AddNinlilDhcpProtocolRa are simply always the right registration,
// gated entirely by RaProtocolOptions.Enable inside Icmpv6RaListener itself, off by default for
// the same reason every other capability-expanding transport already is (an unwanted Router
// Advertisement genuinely reconfigures every other host's own default route/address
// autoconfiguration on the link).
builder.Services.AddNinlilDhcpServerRa(builder.Configuration);
builder.Services.AddNinlilDhcpProtocolRa(builder.Configuration);

// IClientIdentityResolver -- resolves a DNS query's own source address to its persistent client
// identifier (a MAC address or a DUID), consulted by per-client filtering/parental-control
// features and the persistent query log's own per-client activity view. This one call registers
// both the ARP/neighbor-table resolver and the manual static-address-mapping resolver, each
// unconditionally -- both work, or gracefully never resolve anything, regardless of whether this
// process's own DHCP engine is even enabled: the OS-level ARP cache is populated by the kernel for
// any device on a locally-connected network, DHCP or not, and a manual mapping is an operator's
// own standalone configuration with no DHCP dependency at all.
builder.Services.AddNinlilDnsServerClientIdentity(builder.Configuration);

// The manual static-address-mapping resolver is always tried first, regardless of which of the
// two automatic resolvers below is also active -- see StaticAddressMappingClientIdentityResolver's
// own doc comment for why an operator's own explicit pin must win over incidental ARP/DHCP data
// rather than being silently shadowed by it.
//
// A DHCP-lease-backed resolver is only ever meaningful alongside this server's own local DHCPv4
// lease-granting engine (dhcpv4RelayEnabled, read once above) -- a relay agent grants no lease of
// its own to look up, exactly the same precondition AddNinlilDhcpDnsIntegration's own call site
// above already establishes for the identical reason. When available, it is tried right after the
// manual resolver (faster and more authoritative when this server itself issued the lease); the
// ARP resolver is always the last fallback, since it alone covers a client whose lease came from
// an entirely different DHCP server. This selection is made explicitly, here, once -- the same
// "choose the active implementation before dependency injection is built" precedent already
// governing which message-broker transport, which OIDC scheme, and which DHCPv4/DHCPv6 engine
// variant is active.
if (!dhcpv4RelayEnabled)
{
    builder.Services.AddSingleton<IClientIdentityResolver>(
        provider => new CompositeClientIdentityResolver([
            provider.GetRequiredService<StaticAddressMappingClientIdentityResolver>(),
            provider.GetRequiredService<DhcpLeaseClientIdentityResolver>(),
            provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>(),
        ])
    );
}
else
{
    builder.Services.AddSingleton<IClientIdentityResolver>(
        provider => new CompositeClientIdentityResolver([
            provider.GetRequiredService<StaticAddressMappingClientIdentityResolver>(),
            provider.GetRequiredService<ArpNeighborTableClientIdentityResolver>(),
        ])
    );
}

// DnsOverHttpsTransportGate gives DNS-over-HTTPS -- mapped directly onto this host's own shared
// Kestrel below, after Build(), rather than self-hosting a second Kestrel of its own -- the same
// genuine hot start/stop lifecycle every socket-based transport already has, since a Minimal API
// route, once mapped, cannot itself be unmapped at runtime. Registered unconditionally: the GET/
// POST routes below are themselves always mapped too, regardless of DnsOverHttpsOptions.Enable's
// own value at this exact moment -- this gate is what decides whether a request reaching an
// already-mapped route is actually answered, so an enable toggle after startup (TransportSettingsController)
// stays genuinely live in both directions rather than only ever being able to turn this transport
// off. This way ManagedServiceRegistry below can always wrap it the same way it wraps every other
// transport, disabled or not.
builder.Services.AddSingleton<DnsOverHttpsTransportGate>();

// ManagedServiceRegistry is this project's own Service Lifecycle Management registry --
// ServicesController's one dependency, giving it a named, categorized handle onto every real
// transport listener and background maintenance loop this host constructs. Registered as a
// singleton built from a factory (rather than eagerly, here) so ManagedServiceRegistry.Build only
// ever runs after this host's own generic host has already started every wrapped service once --
// see that class's own doc comment for exactly why that ordering is what makes wrapping the real,
// already-running instances (not independent copies) safe.
builder.Services.AddSingleton<IManagedServiceRegistry>(serviceProvider =>
    ManagedServiceRegistry.Build(serviceProvider)
);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<PendingTwoFactorSignInCache>();

// Scoped, matching IEmailSender/UserManager<TUser>'s own scoped lifetimes -- shared by
// UsersController.ResetPassword's own EmailLink delivery mode and AuthController.ForgotPassword.
builder.Services.AddScoped<PasswordResetLinkSender>();

// DnsEventsHubBroadcaster is registered once, as a singleton, under its own concrete type and
// under each IDnsServerEventSubscriber<TEvent> interface it implements, pointing at the same
// instance -- the same "one implementation, several interface aliases" shape
// DnsServerEventPublisher itself already uses.
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

builder.Services.AddSignalR();

// The health-check /status endpoint -- nine checks, one per subsystem, each reusing
// that subsystem's own already-tracked live state rather than adding a second, redundant
// periodic-check layer on top of it. See Ninlil.Web.HealthChecks and
// EndpointRouteBuilderStatusExtension.MapNinlilDnsStatus for the full design record. "cluster"
// reuses the cluster's own already-tracked peer registry exactly the
// way "forwarders" already reuses ForwarderHealthTracker; "webhookDelivery" reuses the same
// persisted delivery-attempt history WebhookDeliveryRetrySweepService itself already reads,
// via a real, cheap, pushed-down query rather than a new tracking mechanism.
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

// NinlilExceptionHandler turns every exception in this project's own custom hierarchy (and,
// as a final fallback, a genuinely unhandled one) into a consistent RFC 7807 ProblemDetails
// response. AddProblemDetails() registers the IProblemDetailsService that handler writes through,
// and is also what ASP.NET Core's own automatic model-state validation ([ApiController]) and
// status-code pages already use, so every one of those three sources now produces the identical
// response shape.
builder.Services.AddExceptionHandler<NinlilExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Registers every class in this assembly implementing IExamplesProvider<T>/
// IMultipleExamplesProvider<T> (this project's own Examples/ folder) with dependency injection,
// so ExampleFilters() below can resolve and invoke whichever one a SwaggerRequestExample/
// SwaggerResponseExample action attribute names.
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

    // Applies every SwaggerRequestExample/SwaggerResponseExample action attribute this project's
    // own controllers carry, rendering the matching Examples/ provider's own hand-authored value
    // into the generated OpenAPI document's own "example"/"examples" fields.
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

// The NINLIL_ environment-variable provider registered above runs far too early in host startup
// (before any logging provider exists) to log anything about a variable it did not recognize --
// warn about one now, through the host's own real logger, so an operator who mistyped a NINLIL_
// name (or set one from a configuration class this registry has not been told about) sees it in
// the startup log rather than silently having that setting ignored. A framework-level variable
// (NINLIL_ASPNETCORE_ENVIRONMENT and similar) is handled entirely by
// FrameworkEnvironmentVariableBootstrap, not by this provider's own Options-class mapping table,
// so it is excluded here rather than misreported as a typo despite being spelled correctly.
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

// Registered as the very first middleware in this pipeline, ahead of even reverse-proxy trust
// gating, so it catches an exception thrown anywhere downstream -- including one thrown by a
// later middleware, not just a controller action. UseExceptionHandler() with no arguments
// dispatches to whichever IExceptionHandler instances AddExceptionHandler<T>() registered above
// (here, exactly one, NinlilExceptionHandler, which always handles what it sees).
app.UseExceptionHandler();

// Reverse-proxy trust gating runs before every other middleware in this pipeline -- including
// authentication, static-file serving, and every mapped endpoint below -- so that whichever
// remote address/scheme each of them observes (HttpContext.Connection.RemoteIpAddress and
// HttpContext.Request.Scheme, both overwritten in place by UseForwardedHeaders once a request is
// judged trusted) is already the real client's own, not merely the immediate TCP peer's.
if (reverseProxyOptions.Enable)
{
    app.UseMiddleware<ReverseProxyTrustGatingMiddleware>();
    app.UseForwardedHeaders();
}

// Rejects ordinary requests with a 503 for the brief window a live storage-engine switch
// (StorageController.Switch) is actually in progress -- see StorageSwitchGateMiddleware's own
// doc comment for exactly why that narrow window needs guarding at all.
app.UseMiddleware<StorageSwitchGateMiddleware>();

// Apply every pending relational migration automatically on startup -- a fresh installation
// works immediately, with no separate migration step, the same experience
// AddNinlilDnsEntityFrameworkCore's own auto-generated SQLite connection string already
// provides. Not applicable to MongoDB, which is schemaless and has no migrations at all.
if (storageOptions.Engine != StorageEngineEnum.MongoDb)
{
    await using AsyncServiceScope migrationScope = app.Services.CreateAsyncScope();
    NinlilDnsContext dbContext =
        migrationScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
    await dbContext.Database.MigrateAsync();
}
else
{
    // MongoDB has no migrations, so the one data reshaping a relational migration performs for
    // known clients -- moving each legacy device's own single identifier into its own address
    // document -- runs here instead, idempotently, on every startup. See
    // DnsClientAddressLegacyBackfill's own doc comment for exactly what it does.
    await using AsyncServiceScope backfillScope = app.Services.CreateAsyncScope();
    int backfilledClients = await DnsClientAddressLegacyBackfill.RunAsync(
        backfillScope.ServiceProvider.GetRequiredService<NinlilDnsMongoContext>()
    );
    if (backfilledClients > 0)
        app.Logger.LogInformation(
            "Moved {Count} legacy known-client identifiers into their own address documents.",
            backfilledClients
        );
}

await AdministratorBootstrapper.EnsureBootstrapAdministratorAsync(app.Services, app.Configuration);

// Warn -- but never block or change startup -- about a configured listener that binds a
// privileged port (1023 or below) without this process actually having the elevation needed to
// bind it. Runs once here, after the real host's own logger exists and every relevant transport's
// Options class has already been bound, but strictly before any transport's own BackgroundService
// gets a chance to attempt its own real bind -- so an operator who hits this sees a clear,
// actionable notice first, rather than only the raw SocketException that transport's own listener
// throws moments later once the generic host actually starts it. See
// PrivilegedPortStartupDiagnostics's own doc comment for the full reasoning, including why this
// uses a real, live bind probe rather than simply warning off the port number alone (a container
// or systemd unit can grant this process Linux's own CAP_NET_BIND_SERVICE capability, in which
// case a configured privileged port is entirely fine).
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

app.UseSwagger();
app.UseSwaggerUI();
app.MapGet("/documentation", () => Results.Content(ReDocPage.Html, "text/html"));

// Serves the Angular admin SPA (src/angular/) as static files from this project's own
// wwwroot/ directory -- populated by the NinlilBuildAngularAdminSpa MSBuild target in
// Ninlil.Web.csproj (which builds the Angular workspace and copies its production output
// here automatically as part of a plain `dotnet build`/`dotnet publish` of this project, so this
// stays true to the same "single combined host, dotnet run and it just works" philosophy this
// project already established for the DNS engine itself -- no separate front-end build step an
// operator needs to remember). Placed ahead of authentication/authorization: the SPA's own
// static assets (its compiled JS/CSS bundles, index.html) are public, exactly like Swagger UI's
// own schema already is (see the pre-existing UseSwaggerUI() call above) -- every actual data
// endpoint the SPA calls into still sits behind this host's own JWT/permission-claim policies
// regardless, and the SPA's own client-side route guards (not this middleware) are what gate a
// page's own content once the app has loaded.
// A single-file release binary carries the admin console inside the assembly itself (see
// Ninlil.Web.csproj's NinlilEmbedWebRoot property) rather than in a wwwroot/ directory beside
// the executable. When no physical index.html is present and the assembly holds an embedded-file
// manifest, serve the embedded copy instead; UseDefaultFiles, UseStaticFiles, and the SPA
// fallback below all read from this same web root provider, so nothing else has to change. A
// physical wwwroot/ always wins, so an operator can still replace the console on disk.
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

// The admin-login brute-force limiter's own policy (see
// ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName) is applied
// per-action via [EnableRateLimiting] on AuthController.Login/TwoFactorLogin, not globally --
// this middleware call is what actually evaluates that attribute for every mapped endpoint below.
app.UseRateLimiter();

app.MapControllers();
app.MapHub<DnsEventsHub>("/hubs/events");
app.MapNinlilDnsOverHttps();
app.MapNinlilDnsStatus();

// Falls back to the SPA's own index.html for any request that matches neither a real endpoint
// above nor an existing static file under wwwroot/ -- so a deep link (e.g. a browser navigating
// straight to /zones/123, not a click from within an already-loaded SPA) is handed to the
// Angular app's own client-side router instead of 404ing on this server, per this project's
// own confirmed "SPA-fallback hosting" requirement. The route pattern's own negative-lookahead
// regex excludes this host's other reserved top-level path prefixes (the REST API, Swagger/
// ReDoc, the SignalR hub, DNS-over-HTTPS's own conventional RFC 8484 path, and the health-status
// endpoint) -- each anchored to a full path segment (followed by "/" or end-of-string, not just
// a literal prefix match) so a genuine future SPA route that merely *starts with* one of these
// words (an "apiary" or "statuses" page, however unlikely) is never mistakenly swallowed by it.
// Without this exclusion, an unmatched /api/... request (a typo, a since-removed endpoint) would
// otherwise be handed this SPA's own HTML instead of a real 404 -- a real, deliberate tradeoff
// of SPA-fallback hosting generally, narrowed here as far as it reasonably can be. Note this
// list deliberately names the literal "dns-query" default rather than reading
// DnsOverHttpsOptions.Path at startup -- DoH's own MapGet/MapPost already register a real
// endpoint at whatever path an operator configures, so an exact request there is dispatched to
// DoH before this fallback is ever reached regardless; only an operator who *both* renames that
// path away from the RFC 8484 convention *and* has an unrelated SPA route colliding with the old
// literal "dns-query" name would ever notice the difference, an edge case not worth a dynamic
// regex for.
//
// A second negative lookahead excludes any request whose final path segment contains a dot --
// that is, one that looks like a static asset request (a JS/CSS/image/font file) rather than a
// client-side route. UseStaticFiles() above already runs ahead of this fallback and serves an
// asset that genuinely exists under wwwroot/ directly, so this fallback is only ever reached for
// an asset-shaped request when the file does not actually exist there -- most commonly a
// browser's own cached index.html still referencing a content-hashed bundle name from before a
// rebuild replaced wwwroot/'s contents. Without this second exclusion, that missing asset would
// be silently handed this SPA's own index.html with a 200 and a text/html content type, which
// the browser then rejects outright when it expected a script/stylesheet -- a genuinely missing
// static asset should surface as an ordinary 404, not a misleading 200 of the wrong content type.
// A real client-side route never has a dot in its own final segment (a resource identifier here
// is always a plain id/slug, never a dotted file name), so this exclusion cannot mistake one for
// a static asset.
app.MapFallbackToFile(
    "{*path:regex(^(?!(api|swagger|documentation|hubs|dns-query|status)(/|$))(?!.*\\.[^/]+$).*$)}",
    "index.html"
);

app.Run();

/// <summary>
///     This class holds the static ReDoc documentation page served at <c>/documentation</c> --
///     a plain HTML page loading ReDoc's own standalone script from its CDN and pointing it at
///     this project's generated OpenAPI document, rather than a separate NuGet package, since
///     ReDoc itself needs nothing more than that.
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
///     This partial class declaration exists only so <c>WebApplicationFactory&lt;Program&gt;</c>
///     can reference this top-level-statement program's own implicit entry point type from a
///     separate test project.
/// </summary>
public partial class Program;
