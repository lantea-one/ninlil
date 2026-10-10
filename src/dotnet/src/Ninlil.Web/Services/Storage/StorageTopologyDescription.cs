using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.MongoDb.Topology;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This static class builds the one-line description of the storage topology that
///     <c>Program.cs</c> logs at startup, after the migration and any storage switch: the engine,
///     the primary hosts, the read replica hosts or the MongoDB read preference, and the two
///     read-your-own-writes limits. It lists host names and ports through
///     <see cref="StorageHostLister" />, so a user name, password or database name from a
///     connection string never reaches the log.
/// </summary>
public static class StorageTopologyDescription
{
    /// <summary>
    ///     This method describes the storage in use.
    /// </summary>
    /// <param name="options">The storage settings in use.</param>
    /// <param name="strings">The composed connection strings for those settings.</param>
    /// <returns>A sentence without secrets.</returns>
    public static string Describe(StorageOptions options, StorageConnectionStrings strings)
    {
        string primary = HostText(options.Engine, strings.Primary);

        string replica;
        if (options.Engine == StorageEngineEnum.MongoDb)
        {
            string mode = MongoClientSettingsComposer
                .ComposeReplicaReadPreference(options)
                ?.ReadPreferenceMode.ToString();
            replica = mode is null ? null : $"read preference {mode}";
        }
        else
            replica = strings.Replica is null
                ? null
                : $"read replica {HostText(options.Engine, strings.Replica)}";

        string limits = replica is null
            ? ""
            : $"; a user who saved reads the primary for {options.ReadReplica.StickyAfterWriteSeconds} s, "
                + $"a replica more than {options.ReadReplica.MaxLagSeconds} s behind gets no reads";

        return $"Storage: {options.Engine}, primary {primary}, "
            + (replica ?? "no read replica")
            + limits
            + ".";
    }

    /// <summary>
    ///     This method joins the hosts of a connection string, or names a file database.
    /// </summary>
    private static string HostText(StorageEngineEnum engine, string connectionString)
    {
        IReadOnlyList<string> hosts = StorageHostLister.List(engine, connectionString);
        if (hosts.Count > 0)
            return string.Join(", ", hosts);

        return engine == StorageEngineEnum.Sqlite ? "a local file" : "(hosts not readable)";
    }
}
