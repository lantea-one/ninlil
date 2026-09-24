using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class hosts a real, in-process instance of <c>Ninlil.Web</c> for these
///     integration tests, against an isolated, throwaway SQLite database file rather than the
///     application's own default (which would otherwise collide with a real installation's data
///     directory, or with another test run's).
///     <para>
///         Every override below is applied via environment variables, set in the constructor,
///         before the host is ever built. Tried <see cref="WebApplicationFactory{TEntryPoint}" />'s
///         own <c>ConfigureAppConfiguration</c> hook first, since that is the more usual pattern
///         -- but confirmed, by an actual failing test ("the signature key was not found" on
///         every authenticated request, despite a genuinely valid token), that overrides
///         registered that way are not visible to <c>Program.cs</c>'s own top-level statements,
///         which read several of these same configuration keys directly, before
///         <c>WebApplicationBuilder.Build()</c> is ever called -- by the time <c>Build()</c>
///         folds a <c>ConfigureAppConfiguration</c> override into the final <c>IConfiguration</c>,
///         that earlier code has already read (and, for the JWT signing key, generated and
///         persisted) its own different value. Environment variables have no such ordering
///         hazard, since <c>WebApplicationBuilder.CreateBuilder(args)</c> itself already reads
///         them synchronously as one of its default configuration sources.
///     </para>
///     <para>
///         Every variable below uses this project's own real, single supported
///         <c>NINLIL_</c>-prefixed naming scheme -- the old double-underscore form this class used
///         before that scheme shipped is now genuinely inert, exactly like any other real
///         deployment's environment.
///     </para>
///     <para>
///         This only stays correct because every test class in this project shares exactly one
///         instance of this factory (see <see cref="WebTestCollection" />) -- these environment
///         variables are process-wide, and a second, concurrently-building factory instance
///         would race this one to set them, which is exactly the failure mode that made this
///         project switch to a shared collection fixture in the first place.
///     </para>
/// </summary>
public sealed class NinlilDnsWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    ///     This constant contains the bootstrap administrator account's own user name, fixed for
    ///     every test run.
    /// </summary>
    public const string AdminUserName = "testadmin";

    /// <summary>
    ///     This constant contains the bootstrap administrator account's own initial password,
    ///     fixed for every test run via <see cref="Startup.AdministratorBootstrapper.PasswordConfigurationKey" />.
    /// </summary>
    public const string AdminPassword = "Test-Password-1234!";

    private readonly string _sqlitePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-{Guid.NewGuid():N}.sqlite3"
    );

    /// <summary>
    ///     This field contains an isolated, throwaway JSON file every runtime settings controller
    ///     (<c>PresetsController</c>, <c>EventBrokerSettingsController</c>,
    ///     <c>RateLimitingSettingsController</c>, <c>DnsCookieSettingsController</c>) merges a
    ///     write into for this test run -- without this, a test applying a preset or patching a
    ///     setting would mutate this project's own real, shared <c>appsettings.json</c> build
    ///     output, which every test class in this collection reuses via one factory instance (see
    ///     this class's own doc comment for why exactly one instance ever exists).
    /// </summary>
    private readonly string _runtimeConfigurationFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-runtime-settings-{Guid.NewGuid():N}.json"
    );

    /// <summary>
    ///     This field contains an isolated, throwaway path for persisted MAC vendor data, so a
    ///     test exercising an on-demand refresh never writes into the shared build output.
    /// </summary>
    private readonly string _macVendorDataFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-mac-vendors-{Guid.NewGuid():N}.snapshot.br"
    );

    /// <summary>
    ///     This constructor sets every environment variable this test host's own configuration
    ///     needs before the host is ever built.
    /// </summary>
    public NinlilDnsWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("NINLIL_STORAGE_ENGINE", "Sqlite");
        Environment.SetEnvironmentVariable(
            "NINLIL_STORAGE_CONNECTION_STRING",
            $"Data Source={_sqlitePath}"
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_JWT_SIGNING_KEY",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_USER_NAME",
            AdminUserName
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD",
            AdminPassword
        );

        // Plain DNS starts automatically by default (it has no Enable flag of its own), and this
        // host's own Kestrel binds real HTTP/HTTPS ports too -- both would otherwise try to bind
        // privileged ports (53, 80, 443) real Kestrel/socket listeners under WebApplicationFactory
        // actually attempt to open (unlike the ones that only exist through TestServer's
        // in-memory abstraction), which would fail without root and isn't what any of these tests
        // are actually exercising. Random high ports keep every real listener this host starts
        // out of the way without disabling anything.
        PlainDnsPort = Random.Shared.Next(20000, 25000);
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT",
            PlainDnsPort.ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_WEB_HTTP_PORT",
            Random.Shared.Next(25000, 30000).ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_WEB_HTTPS_PORT",
            Random.Shared.Next(30000, 35000).ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH",
            _runtimeConfigurationFilePath
        );

        // The admin-login brute-force limiter is disabled for this shared fixture, deliberately
        // -- confirmed necessary, not merely convenient, by first checking TestServer's own
        // actual behavior directly (the same check ReverseProxyTrustGatingMiddlewareTests's own
        // doc comment already made for an unrelated middleware): its in-memory transport leaves
        // HttpContext.Connection.RemoteIpAddress null for every request, so this limiter's own
        // per-client-IP partitioning would collapse onto a single "unknown" partition shared by
        // every one of the dozens of test classes in this collection that call POST
        // /api/auth/login -- tripping the default five-attempts-per-five-minutes threshold almost
        // immediately and then 429-ing every unrelated test's own login for the rest of that
        // window. AdminLoginRateLimitingTests (Ninlil.Dns.Identity.Tests) is where this limiter's
        // own real lockout behavior is actually proven, against a minimal, dedicated host built
        // specifically for that -- not through this shared, general-purpose one.
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE",
            "false"
        );

        // Automatic MAC vendor refresh is on by default and downloads from the IEEE; no test in
        // this shared host may depend on (or load) live external infrastructure, and the
        // embedded snapshot already answers every lookup a test needs. Every registry URL is
        // also pointed at a local port nothing listens on, so even an on-demand refresh a test
        // triggers fails fast and locally instead of reaching the real IEEE server.
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_ENABLE_AUTOMATIC_REFRESH",
            "false"
        );
        foreach (string registry in new[] { "MA_L", "MA_M", "MA_S" })
            Environment.SetEnvironmentVariable(
                $"NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_{registry}_REGISTRY_URL",
                UnreachableMacVendorRegistryUrl
            );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_DATA_FILE_PATH",
            _macVendorDataFilePath
        );
    }

    /// <summary>
    ///     This constant contains the registry URL every MAC vendor registry is pointed at in this
    ///     test host: a loopback port (discard, 9) nothing listens on.
    /// </summary>
    public const string UnreachableMacVendorRegistryUrl = "http://127.0.0.1:9/registry.csv";

    /// <summary>
    ///     This property contains the isolated, throwaway JSON file this test run's own runtime
    ///     settings controllers merge a write into -- exposed so a test can read back exactly what
    ///     was written.
    /// </summary>
    public string RuntimeConfigurationFilePath => _runtimeConfigurationFilePath;

    /// <summary>
    ///     This property contains this test run's own original SQLite database file path -- exposed
    ///     so a test exercising <c>StorageController.Switch</c> (which mutates this same shared
    ///     factory's own live storage engine for the remainder of this test collection's run) can
    ///     switch back onto it once finished, the same "restore what you changed before returning"
    ///     convention <c>ServicesControllerCrudTests</c> already applies to a stopped service.
    /// </summary>
    public string SqlitePath => _sqlitePath;

    /// <summary>
    ///     This property contains the real, ephemeral UDP/TCP port this test run's own Plain DNS
    ///     listener actually binds to -- Plain DNS runs a genuine <see cref="System.Net.Sockets.Socket" />-based
    ///     listener, not something that only exists through <c>TestServer</c>'s in-memory HTTP
    ///     abstraction, so a test exercising it needs the real port to send a real query to.
    /// </summary>
    public int PlainDnsPort { get; }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            File.Delete(_sqlitePath);
        }
        catch (IOException)
        {
            // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
        }

        try
        {
            File.Delete(_runtimeConfigurationFilePath);
            File.Delete(_macVendorDataFilePath);
            File.Delete(_macVendorDataFilePath + ".state.json");
        }
        catch (IOException)
        {
            // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
        }
    }
}
