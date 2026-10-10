using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests the process-level behaviour of a refused startup: <c>Ninlil.Web</c> exits
///     with code 78 (EX_CONFIG) and prints the reason when the database holds a migration this
///     version does not know, or when the storage cluster settings contradict themselves. It starts the built <c>Ninlil.Web.dll</c> as a child process with
///     <c>dotnet</c>, which is the only way to observe an exit code. The class deliberately does
///     not join <c>WebTestCollection</c>: it does not use the shared host, and the shared host
///     sets process-wide environment variables this test does not want. The child gets its own
///     environment, working directory and content root in a temporary directory, so it writes
///     nothing into the build output.
/// </summary>
public sealed class StartupRefusalProcessTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-startup-refusal-{Guid.NewGuid():N}"
    );

    /// <summary>
    ///     This constructor creates the temporary directory the child process works in.
    /// </summary>
    public StartupRefusalProcessTests() => Directory.CreateDirectory(_directory);

    /// <summary>
    ///     This method deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    /// <summary>
    ///     A SQLite database whose history holds a migration from a newer version stops the
    ///     process with exit code 78 and a message naming the unknown migration.
    /// </summary>
    [Fact]
    public async Task Startup_WithAMigrationFromANewerVersion_ExitsWith78AndPrintsTheReason()
    {
        string databasePath = Path.Combine(_directory, "future.sqlite3");
        using (SqliteConnection connection = new($"Data Source={databasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL "
                + "PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL); "
                + "INSERT INTO \"__EFMigrationsHistory\" VALUES "
                + "('20991231000000_FromTheFuture', '99.0.0');";
            command.ExecuteNonQuery();
        }

        (int exitCode, string text) = await RunUntilExitAsync(
            databasePath,
            new Dictionary<string, string>()
        );
        Assert.True(exitCode == 78, $"Exit code {exitCode}. Output: {text}");
        Assert.Contains("20991231000000_FromTheFuture", text);
        Assert.Contains("upgraded by a newer Ninlil version", text);
    }

    /// <summary>
    ///     A read replica connection string on SQLite (which has no replica) stops the process
    ///     with exit code 78 before the database is touched, and the message names the setting
    ///     without printing the connection string.
    /// </summary>
    [Fact]
    public async Task Startup_WithAReplicaConnectionStringOnSqlite_ExitsWith78WithoutTheSecret()
    {
        (int exitCode, string text) = await RunUntilExitAsync(
            Path.Combine(_directory, "unused.sqlite3"),
            new Dictionary<string, string>
            {
                ["NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING"] =
                    "Host=replica;Password=hunter2-secret",
            }
        );

        Assert.True(exitCode == 78, $"Exit code {exitCode}. Output: {text}");
        Assert.Contains("Storage:ReadReplica:ConnectionString", text);
        Assert.DoesNotContain("hunter2", text);
    }

    /// <summary>
    ///     This method starts the built host as a child process on a SQLite database, waits for it
    ///     to exit and returns the exit code with everything it printed.
    /// </summary>
    /// <param name="databasePath">The SQLite file the child is pointed at.</param>
    /// <param name="extraEnvironment">Further environment variables for the child.</param>
    private async Task<(int ExitCode, string Output)> RunUntilExitAsync(
        string databasePath,
        IReadOnlyDictionary<string, string> extraEnvironment
    )
    {
        string webAssembly = Path.Combine(AppContext.BaseDirectory, "Ninlil.Web.dll");
        Assert.True(File.Exists(webAssembly), "Ninlil.Web.dll is missing from the test output.");

        ProcessStartInfo startInfo = new("dotnet")
        {
            WorkingDirectory = _directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(webAssembly);
        startInfo.ArgumentList.Add($"--contentRoot={_directory}");
        startInfo.ArgumentList.Add($"--webroot={Path.Combine(_directory, "wwwroot")}");
        startInfo.Environment["NINLIL_STORAGE_ENGINE"] = "Sqlite";
        startInfo.Environment["NINLIL_STORAGE_CONNECTION_STRING"] = $"Data Source={databasePath}";
        foreach ((string name, string value) in extraEnvironment)
            startInfo.Environment[name] = value;
        startInfo.Environment["NINLIL_JWT_SIGNING_KEY"] = Convert.ToBase64String(
            RandomNumberGenerator.GetBytes(64)
        );
        startInfo.Environment["NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT"] = GetFreePort().ToString();
        startInfo.Environment["NINLIL_DNS_SERVER_WEB_HTTP_PORT"] = GetFreePort().ToString();
        startInfo.Environment["NINLIL_DNS_SERVER_WEB_HTTPS_PORT"] = GetFreePort().ToString();
        startInfo.Environment["NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH"] =
            Path.Combine(_directory, "runtime.json");
        startInfo.Environment["NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE"] = "false";
        startInfo.Environment["NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_ENABLE_AUTOMATIC_REFRESH"] =
            "false";
        startInfo.Environment["NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_DATA_FILE_PATH"] = Path.Combine(
            _directory,
            "mac-vendors.snapshot.br"
        );

        using Process process = Process.Start(startInfo);
        StringBuilder output = new();
        void Collect(object sender, DataReceivedEventArgs received)
        {
            lock (output)
                output.AppendLine(received.Data);
        }
        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(60));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("The process did not exit within 60 seconds. Output: " + output);
        }

        // Flush the redirected streams before reading them.
        process.WaitForExit();

        string text;
        lock (output)
            text = output.ToString();
        return (process.ExitCode, text);
    }

    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
