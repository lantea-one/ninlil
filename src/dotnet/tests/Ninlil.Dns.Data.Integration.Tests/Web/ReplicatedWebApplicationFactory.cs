using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Web;

/// <summary>
///     This class hosts the real <c>Ninlil.Web</c> application in process on a database that has
///     a read replica, for the read-your-own-writes tests. HTTP requests go through
///     <c>TestServer</c>; the DNS listeners open real sockets on free ports. The settings are
///     <c>NINLIL_*</c> environment variables set before the host is built (the way
///     <c>Ninlil.Web.Tests</c> does it, because <c>Program.cs</c> reads keys before the web host
///     builder merges any other source) and removed again when the factory is disposed. Because
///     environment variables are process-wide, <see cref="ReplicaWebHostTestsBase" /> lets only
///     one factory exist at a time.
/// </summary>
public sealed class ReplicatedWebApplicationFactory : WebApplicationFactory<Program>
{
    /// <summary>
    ///     This constant contains the bootstrap administrator's user name.
    /// </summary>
    public const string AdminUserName = "replicaadmin";

    /// <summary>
    ///     This constant contains the bootstrap administrator's password.
    /// </summary>
    public const string AdminPassword = "Replica-Admin-1234!";

    private readonly Dictionary<string, string> previous = [];
    private readonly string runtimeConfigurationFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-replica-web-settings-{Guid.NewGuid():N}.json"
    );
    private readonly string macVendorDataFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-replica-web-mac-vendors-{Guid.NewGuid():N}.snapshot.br"
    );

    /// <summary>
    ///     This constructor sets the environment variables of the host.
    /// </summary>
    /// <param name="storageSettings">
    ///     The storage variables (engine, connection strings, replica settings), by variable name.
    /// </param>
    public ReplicatedWebApplicationFactory(IReadOnlyDictionary<string, string> storageSettings)
    {
        Dictionary<string, string> values = new(storageSettings)
        {
            ["NINLIL_JWT_SIGNING_KEY"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            ["NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_USER_NAME"] = AdminUserName,
            ["NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD"] = AdminPassword,
            ["NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT"] = FreePort().ToString(),

            // Loopback is all the test needs; the machine may run other services on its interfaces.
            ["NINLIL_DNS_SERVER_PROTOCOL_PLAIN_LISTEN_ADDRESSES"] = "127.0.0.1",
            ["NINLIL_DNS_SERVER_WEB_HTTP_PORT"] = FreePort().ToString(),
            ["NINLIL_DNS_SERVER_WEB_HTTPS_PORT"] = FreePort().ToString(),
            ["NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH"] =
                runtimeConfigurationFilePath,

            // The test host has no client address, which would put every login into one rate
            // limit partition.
            ["NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE"] = "false",

            // No test may reach an external service.
            ["NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_ENABLE_AUTOMATIC_REFRESH"] = "false",
            ["NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_DATA_FILE_PATH"] = macVendorDataFilePath,
        };
        foreach (string registry in new[] { "MA_L", "MA_M", "MA_S" })
            values[$"NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_{registry}_REGISTRY_URL"] =
                "http://127.0.0.1:9/registry.csv";

        foreach ((string name, string value) in values)
        {
            previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseEnvironment("Development");

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        foreach ((string name, string value) in previous)
            Environment.SetEnvironmentVariable(name, value);

        foreach (string file in new[] { runtimeConfigurationFilePath, macVendorDataFilePath })
            try
            {
                File.Delete(file);
                File.Delete(file + ".state.json");
            }
            catch (IOException)
            {
                // A leftover temporary file is harmless.
            }
    }

    private static int FreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
