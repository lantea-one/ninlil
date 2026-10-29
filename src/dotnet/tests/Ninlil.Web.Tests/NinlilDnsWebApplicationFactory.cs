using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class hosts <c>Ninlil.Web</c> in process for the integration tests in this project.
///     HTTP requests go through <c>TestServer</c>'s in-memory transport, while the DNS listeners
///     open real sockets on free ports. Storage is SQLite in a temporary file, so a test run never
///     touches an installation's data directory or another run's database. The host runs in the
///     Development environment and seeds a bootstrap administrator (<see cref="AdminUserName" />,
///     <see cref="AdminPassword" />) that holds every permission; most test classes sign in as it.
///     <para>
///         Every override is set as a <c>NINLIL_</c> environment variable in the constructor, before
///         the host is built. <c>ConfigureAppConfiguration</c> does not work here: the top-level
///         statements in <c>Program.cs</c> read several keys (the JWT signing key among them,
///         which is generated and saved when missing) before <c>WebApplicationBuilder.Build()</c>
///         merges such overrides, so authenticated requests then fail with "the signature key was
///         not found". <c>WebApplicationBuilder.CreateBuilder(args)</c> reads environment variables
///         up front, so they have no ordering problem.
///     </para>
///     <para>
///         Environment variables are process-wide, so this only works because every test class
///         shares one instance of this factory through <see cref="WebTestCollection" />.
///     </para>
/// </summary>
public sealed class NinlilDnsWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    ///     This constant contains the bootstrap administrator's user name, fixed for every test
    ///     run.
    /// </summary>
    public const string AdminUserName = "testadmin";

    /// <summary>
    ///     This constant contains the bootstrap administrator's initial password,
    ///     fixed for every test run via <see cref="Startup.AdministratorBootstrapper.PasswordConfigurationKey" />.
    /// </summary>
    public const string AdminPassword = "Test-Password-1234!";

    private readonly string _sqlitePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-{Guid.NewGuid():N}.sqlite3"
    );

    /// <summary>
    ///     This field contains a temporary JSON file that the runtime settings controllers and
    ///     <c>PresetsController</c> merge their writes into for this test run. Without it, a test
    ///     that patches a setting or applies a preset would change the shared configuration file
    ///     in the build output.
    /// </summary>
    private readonly string _runtimeConfigurationFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-runtime-settings-{Guid.NewGuid():N}.json"
    );

    /// <summary>
    ///     This field contains a temporary path for saved MAC vendor data, so a test that triggers
    ///     an on-demand refresh never writes into the build output.
    /// </summary>
    private readonly string _macVendorDataFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-web-tests-mac-vendors-{Guid.NewGuid():N}.snapshot.br"
    );

    /// <summary>
    ///     This constructor sets every environment variable the test host needs before the host
    ///     is built.
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

        // Plain DNS always starts (it has no Enable flag), and its default port 53, like the
        // default web ports 80 and 443, needs root. Ports the operating system reports as free
        // keep these listeners clear of services on the test machine and of another copy of
        // this suite running alongside, without disabling anything.
        PlainDnsPort = GetFreePort();
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT",
            PlainDnsPort.ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_WEB_HTTP_PORT",
            GetFreePort().ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_WEB_HTTPS_PORT",
            GetFreePort().ToString()
        );

        // DNSCrypt is off at startup, but a test that enables it through the transport settings
        // API starts its UDP and TCP listeners on the startup port, which is 5443 by default and
        // can be taken by a service on the test machine or by another copy of this suite. A free
        // port keeps that test off it. DNS-over-HTTPS shares the web ports above; the other
        // transports are never started by a Web test.
        DnsCryptPort = GetFreePort();
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PORT",
            DnsCryptPort.ToString()
        );
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH",
            _runtimeConfigurationFilePath
        );

        // The admin-login limiter must stay off in this shared host. TestServer leaves
        // HttpContext.Connection.RemoteIpAddress null (see ReverseProxyTrustGatingMiddlewareTests),
        // so the limiter's per-IP partitioning puts every login from every test class into one
        // partition, and the default five attempts per five minutes would soon answer 429 to
        // unrelated tests. AdminLoginRateLimitingTests in Ninlil.Dns.Identity.Tests covers the
        // lockout on a dedicated host.
        Environment.SetEnvironmentVariable(
            "NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE",
            "false"
        );

        // Automatic MAC vendor refresh is on by default and downloads from the IEEE. No test may
        // depend on external services, and the embedded snapshot answers every lookup the tests
        // need. Every registry URL also points at a local port nothing listens on, so an
        // on-demand refresh fails fast instead of reaching the IEEE server.
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
    ///     This property contains the temporary JSON file the runtime settings controllers write
    ///     into, so a test can read back what was written. The file is not a configuration source
    ///     of the test host, so a settings write never changes what the host reads; a GET after a
    ///     PATCH still returns the old values. <see cref="AppearanceOptionsHotReloadTests" /> shows
    ///     the reload a deployment gets, where the file is <c>appsettings.json</c>.
    /// </summary>
    public string RuntimeConfigurationFilePath => _runtimeConfigurationFilePath;

    /// <summary>
    ///     This property contains the SQLite database file the host starts on. A test that needs
    ///     to tell the host's database apart from another file uses it.
    /// </summary>
    public string SqlitePath => _sqlitePath;

    /// <summary>
    ///     This property contains the UDP and TCP port the Plain DNS listener binds to. Plain DNS
    ///     listens on a <see cref="System.Net.Sockets.Socket" />, not through <c>TestServer</c>,
    ///     so a test that sends a DNS query needs this port.
    /// </summary>
    public int PlainDnsPort { get; }

    /// <summary>
    ///     This property contains the UDP and TCP port the DNSCrypt listeners bind to when a test
    ///     enables DNSCrypt. It is the startup value of <c>DnsServer:Protocol:DnsCrypt:Port</c>,
    ///     which a settings GET reports.
    /// </summary>
    public int DnsCryptPort { get; }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        try
        {
            TemporaryFiles.DeleteSqliteDatabase(_sqlitePath);
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temporary file is harmless.
        }

        try
        {
            File.Delete(_runtimeConfigurationFilePath);
            File.Delete(_macVendorDataFilePath);
            File.Delete(_macVendorDataFilePath + ".state.json");
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leftover temporary file is harmless.
        }
    }

    /// <summary>
    ///     This method builds an additional host next to the shared one, with its own free DNS
    ///     listener ports. A host built with plain <c>WithWebHostBuilder</c> reads the same
    ///     environment variables, so it binds the shared host's Plain DNS and DNSCrypt ports; the
    ///     bind fails with "address already in use", the hosted listener's exception stops that
    ///     host (<c>BackgroundServiceExceptionBehavior.StopHost</c>), and its service provider is
    ///     disposed before the first request, which then throws
    ///     <see cref="ObjectDisposedException" />. The ports are environment variables, so they
    ///     are replaced only while the new host is built and restored afterwards. The host shares
    ///     the SQLite file and the runtime settings file with the shared host. Callers dispose the
    ///     returned factory and must not run concurrently with another test, which the shared
    ///     <see cref="WebTestCollection" /> guarantees.
    /// </summary>
    /// <param name="configure">Extra configuration for the new host, for example test services.</param>
    /// <returns>A started second factory whose host has its own listener ports.</returns>
    public WebApplicationFactory<Program> CreateSecondHost(Action<IWebHostBuilder> configure)
    {
        string[] portVariables =
        [
            "NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT",
            "NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PORT",
            "NINLIL_DNS_SERVER_WEB_HTTP_PORT",
            "NINLIL_DNS_SERVER_WEB_HTTPS_PORT",
        ];
        string[] previous = portVariables.Select(Environment.GetEnvironmentVariable).ToArray();
        try
        {
            foreach (string name in portVariables)
                Environment.SetEnvironmentVariable(name, GetFreePort().ToString());

            WebApplicationFactory<Program> second = WithWebHostBuilder(configure);

            // The host is built on first access to Server, and that read of the environment is
            // the one that must see the new ports.
            _ = second.Server;
            return second;
        }
        finally
        {
            for (int i = 0; i < portVariables.Length; i++)
                Environment.SetEnvironmentVariable(portVariables[i], previous[i]);
        }
    }

    /// <summary>
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
