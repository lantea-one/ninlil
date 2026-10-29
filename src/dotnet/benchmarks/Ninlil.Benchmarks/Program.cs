using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Microsoft.Data.SqlClient;
using MongoDB.Driver;
using MySqlConnector;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.EngineHosts;
using Npgsql;

// Define our namespace.
namespace Ninlil.Benchmarks;

/// <summary>
///     This class is the entry point of the benchmark program. It prepares the databases, then
///     hands the rest of the command line to BenchmarkDotNet.
///     <para>
///         BenchmarkDotNet runs each benchmark in a child process, so the databases are started
///         and seeded here first, before the children exist, and the children receive the
///         connection strings in <c>BENCHMARK_*</c> environment variables
///         (<see cref="BenchmarkEnvironment" />). Options of this program, removed from the
///         command line before BenchmarkDotNet sees it:
///     </para>
///     <list type="bullet">
///         <item>
///             <description>
///                 <c>--engines Sqlite,PostgreSql,MySql,SqlServer,MongoDb</c>: the engines to run
///                 (default: all). A container engine that cannot start (no Docker) is skipped
///                 with a message.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>--connection Engine="connection string"</c>: use an existing server
///                 instead of a container; the database name is replaced by the data set's. May be
///                 repeated.
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>--scale Small|Large</c>: the data set size (default <c>Small</c>).
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>--job short|default</c>: BenchmarkDotNet's job (default <c>short</c>: one
///                 launch, 3 warm-ups, 10 iterations).
///             </description>
///         </item>
///         <item>
///             <description>
///                 <c>--data-directory path</c>: where the SQLite files live (default
///                 <c>BenchmarkDotNet.Artifacts/data</c>); a seeded file is reused.
///             </description>
///         </item>
///     </list>
///     Every other argument (for example <c>--filter '*QueryLog*'</c> or <c>--list flat</c>) goes
///     to BenchmarkDotNet.
/// </summary>
public static class Program
{
    private static readonly StorageEngineEnum[] AllEngines =
    [
        StorageEngineEnum.Sqlite,
        StorageEngineEnum.PostgreSql,
        StorageEngineEnum.MySql,
        StorageEngineEnum.SqlServer,
        StorageEngineEnum.MongoDb,
    ];

    /// <summary>
    ///     This method runs the program.
    /// </summary>
    public static async Task<int> Main(string[] args)
    {
        // A child process BenchmarkDotNet starts runs the benchmark class directly.
        if (args.Any(arg => arg == "--anonymousPipes" || arg == "--benchmarkName"))
            return Run(args);

        List<string> remaining = [];
        List<StorageEngineEnum> engines = [.. AllEngines];
        Dictionary<StorageEngineEnum, string> connections = [];
        BenchmarkScaleEnum scale = BenchmarkScaleEnum.Small;
        string job = "short";
        string dataDirectory = Path.GetFullPath(Path.Combine("BenchmarkDotNet.Artifacts", "data"));

        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--engines":
                    engines =
                    [
                        .. args[++index]
                            .Split(
                                ',',
                                StringSplitOptions.RemoveEmptyEntries
                                    | StringSplitOptions.TrimEntries
                            )
                            .Select(Enum.Parse<StorageEngineEnum>),
                    ];
                    break;
                case "--connection":
                    string[] pair = args[++index].Split('=', 2);
                    connections[Enum.Parse<StorageEngineEnum>(pair[0])] = pair[1];
                    break;
                case "--scale":
                    scale = Enum.Parse<BenchmarkScaleEnum>(args[++index], ignoreCase: true);
                    break;
                case "--job":
                    job = args[++index];
                    break;
                case "--data-directory":
                    dataDirectory = Path.GetFullPath(args[++index]);
                    break;
                default:
                    remaining.Add(args[index]);
                    break;
            }
        }

        // Listing benchmarks needs no database.
        bool listOnly = remaining.Any(arg => arg is "--list" or "--info");
        List<IAsyncDisposable> hosts = [];
        List<StorageEngineEnum> prepared = [];

        try
        {
            if (!listOnly)
            {
                foreach (StorageEngineEnum engine in engines)
                {
                    if (
                        await PrepareAsync(engine, connections, scale, dataDirectory, hosts) is
                        { } connection
                    )
                    {
                        Environment.SetEnvironmentVariable(
                            BenchmarkEnvironment.ConnectionVariable(engine),
                            connection
                        );
                        prepared.Add(engine);
                    }
                }

                if (prepared.Count == 0)
                {
                    await Console.Error.WriteLineAsync("No engine is available; nothing to run.");
                    return 1;
                }
            }

            Environment.SetEnvironmentVariable(
                BenchmarkEnvironment.EnginesVariable,
                string.Join(',', listOnly ? engines : prepared)
            );
            Environment.SetEnvironmentVariable(
                BenchmarkEnvironment.ScaleVariable,
                scale.ToString()
            );
            Environment.SetEnvironmentVariable(StorageConfig.JobVariable, job);
            Environment.SetEnvironmentVariable(
                BenchmarkEnvironment.DataDirectoryVariable,
                dataDirectory
            );

            return Run([.. remaining]);
        }
        finally
        {
            foreach (IAsyncDisposable host in hosts)
                await host.DisposeAsync();
        }
    }

    private static int Run(string[] args)
    {
        // The jobs and diagnosers come from each benchmark class's [Config] (StorageConfig or
        // MonitoredConfig); the default global configuration adds only loggers, columns and exporters.
        BenchmarkSwitcher
            .FromAssembly(typeof(Program).Assembly)
            .Run(args, ManualConfig.Create(DefaultConfig.Instance));
        return 0;
    }

    /// <summary>
    ///     This method makes the database of <paramref name="engine" /> ready (host started,
    ///     schema created, data seeded) and returns its connection string, or null when the
    ///     engine is skipped.
    /// </summary>
    private static async Task<string> PrepareAsync(
        StorageEngineEnum engine,
        Dictionary<StorageEngineEnum, string> connections,
        BenchmarkScaleEnum scale,
        string dataDirectory,
        List<IAsyncDisposable> hosts
    )
    {
        string databaseName = BenchmarkDataSet.DatabaseName(scale);
        await Console.Out.WriteLineAsync($"[{engine}] preparing");

        try
        {
            string connectionString;

            if (connections.TryGetValue(engine, out string given))
            {
                connectionString = WithDatabase(engine, given, databaseName);
            }
            else
            {
                EngineHost host = engine switch
                {
                    StorageEngineEnum.Sqlite => new SqliteEngineHost(dataDirectory),
                    StorageEngineEnum.PostgreSql => new PostgreSqlEngineHost(),
                    StorageEngineEnum.MySql => new MySqlEngineHost(),
                    StorageEngineEnum.SqlServer => new SqlServerEngineHost(),
                    StorageEngineEnum.MongoDb => new MongoDbEngineHost(),
                    _ => throw new NotSupportedException(engine.ToString()),
                };
                hosts.Add(host);
                await host.StartAsync();
                connectionString = host.BuildConnectionString(databaseName);
            }

            await using EngineDatabase database = await EngineHost.BuildDatabaseAsync(
                engine,
                connectionString,
                createSchema: false
            );
            await BenchmarkDataSet.EnsureSeededAsync(database, scale, Console.Out);
            return connectionString;
        }
        catch (Exception exception)
        {
            // The innermost exception names the real cause (a bad value, no Docker, ...).
            Exception root = exception;
            while (root.InnerException != null)
                root = root.InnerException;

            await Console.Error.WriteLineAsync(
                $"[{engine}] skipped: {exception.GetType().Name}: {exception.Message} "
                    + $"({root.GetType().Name}: {root.Message})"
            );
            return null;
        }
    }

    private static string WithDatabase(
        StorageEngineEnum engine,
        string connectionString,
        string databaseName
    ) =>
        engine switch
        {
            StorageEngineEnum.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString)
            {
                Database = databaseName,
            }.ConnectionString,
            StorageEngineEnum.MySql => new MySqlConnectionStringBuilder(connectionString)
            {
                Database = databaseName,
            }.ConnectionString,
            StorageEngineEnum.SqlServer => new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = databaseName,
            }.ConnectionString,
            StorageEngineEnum.MongoDb => new MongoUrlBuilder(connectionString)
            {
                DatabaseName = databaseName,
            }
                .ToMongoUrl()
                .ToString(),
            _ => throw new NotSupportedException($"--connection is not supported for {engine}."),
        };
}
