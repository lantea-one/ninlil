using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Topology;

/// <summary>
///     This static class runs the checks on the storage cluster settings that do not depend on a
///     driver: which engine a setting belongs to, whether a replica connection is allowed, and
///     whether the read-your-own-writes windows fit together. The per-engine composers call it
///     first and then check their driver's own connection string keys. A refusal is a
///     <see cref="DatabaseStartupRefusedException" />, which <c>Program</c> turns into exit code 78
///     before any database work. Messages name settings and values, never connection strings.
/// </summary>
public static class StorageTopologyValidator
{
    /// <summary>
    ///     This constant is the shortest time-to-live of an in-memory cache that reloads from a
    ///     read replica: the client directory, rewrites, schedules and forwarders refresh every
    ///     60 seconds. A replica allowed to lag that far could answer a refresh with rows older
    ///     than what the cache loaded from the primary after a local save, so a change would
    ///     briefly revert. <c>MaxLagSeconds</c> must stay below it.
    /// </summary>
    public const int ShortestReplicaCacheTimeToLiveSeconds = 60;

    /// <summary>
    ///     This method checks <paramref name="options" /> and returns the warnings.
    /// </summary>
    /// <param name="options">The storage configuration to check.</param>
    /// <returns>Messages about settings that are ignored; empty when there are none.</returns>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     Thrown when a setting is not allowed for the engine, is out of range, or the replica
    ///     windows would break read-your-own-writes.
    /// </exception>
    public static List<string> Validate(StorageOptions options)
    {
        List<string> warnings = [];
        StorageReadReplicaOptions replica = options.ReadReplica ?? new StorageReadReplicaOptions();
        bool hasReplicaConnection = !string.IsNullOrWhiteSpace(replica.ConnectionString);

        if (hasReplicaConnection && options.Engine == StorageEngineEnum.Sqlite)
            throw Refusal(
                $"{Key(nameof(StorageOptions.ReadReplica), nameof(StorageReadReplicaOptions.ConnectionString))} "
                    + "is set, but SQLite is a single file and has no replica. Remove the setting."
            );

        if (hasReplicaConnection && options.Engine == StorageEngineEnum.MongoDb)
            throw Refusal(
                $"{Key(nameof(StorageOptions.ReadReplica), nameof(StorageReadReplicaOptions.ConnectionString))} "
                    + "is set, but MongoDB takes its members from the connection string and routes reads with "
                    + $"{Key(nameof(StorageOptions.MongoDb), nameof(StorageMongoDbOptions.ReadPreference))}. "
                    + "Remove the setting."
            );

        RequireAtLeast(
            replica.StickyAfterWriteSeconds,
            1,
            nameof(StorageReadReplicaOptions.StickyAfterWriteSeconds)
        );
        RequireAtLeast(replica.MaxLagSeconds, 0, nameof(StorageReadReplicaOptions.MaxLagSeconds));
        RequireAtLeast(
            replica.LagCheckIntervalSeconds,
            1,
            nameof(StorageReadReplicaOptions.LagCheckIntervalSeconds)
        );

        // A replica more than StickyAfterWriteSeconds behind could still receive a principal's
        // reads after its sticky window ends, so it might not show what that principal just saved.
        bool hasSplit =
            hasReplicaConnection
            || (
                options.Engine == StorageEngineEnum.MongoDb
                && !string.IsNullOrWhiteSpace(options.MongoDb?.ReadPreference)
                && !string.Equals(
                    options.MongoDb.ReadPreference.Trim(),
                    "Primary",
                    StringComparison.OrdinalIgnoreCase
                )
            );
        if (hasSplit && replica.MaxLagSeconds >= replica.StickyAfterWriteSeconds)
            throw Refusal(
                $"{Key(nameof(StorageOptions.ReadReplica), nameof(StorageReadReplicaOptions.MaxLagSeconds))} "
                    + $"({replica.MaxLagSeconds}) must be below "
                    + $"{Key(nameof(StorageOptions.ReadReplica), nameof(StorageReadReplicaOptions.StickyAfterWriteSeconds))} "
                    + $"({replica.StickyAfterWriteSeconds}); otherwise a user could read a replica that has "
                    + "not yet received their own save."
            );

        // A cache refresh by age reads the replica (see RefreshingSnapshot); a replica that is
        // allowed to be as old as the cache itself could hand back data older than the cache holds.
        if (hasSplit && replica.MaxLagSeconds >= ShortestReplicaCacheTimeToLiveSeconds)
            throw Refusal(
                $"{Key(nameof(StorageOptions.ReadReplica), nameof(StorageReadReplicaOptions.MaxLagSeconds))} "
                    + $"({replica.MaxLagSeconds}) must be below {ShortestReplicaCacheTimeToLiveSeconds}, "
                    + "the shortest refresh interval of the in-memory caches; otherwise a refresh could "
                    + "return data older than the cache already holds."
            );

        WarnForOtherEngines(options, warnings);
        return warnings;
    }

    /// <summary>
    ///     This method reports whether any read replica or cluster setting of the engine in use
    ///     is set. The storage switch refuses a request while this is true: the settings belong to
    ///     one engine and may come from environment variables Ninlil cannot change, so the
    ///     operator removes them first.
    /// </summary>
    /// <param name="options">The storage configuration in use.</param>
    /// <returns>
    ///     True when <c>Storage:ReadReplica:ConnectionString</c> is set, or when any setting of
    ///     the section for <see cref="StorageOptions.Engine" /> differs from its unset value.
    ///     Windows and intervals alone do not count, and sections of other engines are only
    ///     warnings at startup.
    /// </returns>
    public static bool HasClusterSettings(StorageOptions options) =>
        !string.IsNullOrWhiteSpace(options.ReadReplica?.ConnectionString)
        || options.Engine switch
        {
            StorageEngineEnum.PostgreSql => IsConfigured(options.PostgreSql),
            StorageEngineEnum.MySql => IsConfigured(options.MySql),
            StorageEngineEnum.SqlServer => IsConfigured(options.SqlServer),
            StorageEngineEnum.MongoDb => IsConfigured(options.MongoDb),
            _ => false,
        };

    /// <summary>
    ///     This method builds the configuration key of a nested setting, for messages.
    /// </summary>
    /// <param name="section">The nested section name.</param>
    /// <param name="setting">The setting name.</param>
    /// <returns>The key in the form <c>Storage:Section:Setting</c>.</returns>
    public static string Key(string section, string setting) =>
        $"{StorageOptions.SectionName}:{section}:{setting}";

    /// <summary>
    ///     This method creates the exception for a refused configuration.
    /// </summary>
    /// <param name="message">What is wrong and what to change.</param>
    /// <returns>The exception to throw.</returns>
    public static DatabaseStartupRefusedException Refusal(string message) =>
        new($"The storage settings are not valid: {message}");

    /// <summary>
    ///     This method throws when a number is below its lowest allowed value.
    /// </summary>
    private static void RequireAtLeast(int value, int minimum, string setting)
    {
        if (value < minimum)
            throw Refusal(
                $"{Key(nameof(StorageOptions.ReadReplica), setting)} is {value}, but must be at least {minimum}."
            );
    }

    /// <summary>
    ///     This method adds a warning for every engine section that is configured while another
    ///     engine is selected. The settings are ignored.
    /// </summary>
    private static void WarnForOtherEngines(StorageOptions options, List<string> warnings)
    {
        if (options.Engine != StorageEngineEnum.PostgreSql && IsConfigured(options.PostgreSql))
            warnings.Add(IgnoredMessage(nameof(StorageOptions.PostgreSql), options.Engine));
        if (options.Engine != StorageEngineEnum.MySql && IsConfigured(options.MySql))
            warnings.Add(IgnoredMessage(nameof(StorageOptions.MySql), options.Engine));
        if (options.Engine != StorageEngineEnum.SqlServer && IsConfigured(options.SqlServer))
            warnings.Add(IgnoredMessage(nameof(StorageOptions.SqlServer), options.Engine));
        if (options.Engine != StorageEngineEnum.MongoDb && IsConfigured(options.MongoDb))
            warnings.Add(IgnoredMessage(nameof(StorageOptions.MongoDb), options.Engine));
    }

    /// <summary>
    ///     This method creates the warning for an engine section that does not apply.
    /// </summary>
    private static string IgnoredMessage(string section, StorageEngineEnum engine) =>
        $"{StorageOptions.SectionName}:{section} settings are present but the storage engine is {engine}; they are ignored.";

    /// <summary>
    ///     This method reports whether any PostgreSQL setting differs from its unset value.
    /// </summary>
    private static bool IsConfigured(StoragePostgreSqlOptions options) =>
        options is not null
        && (
            !string.IsNullOrWhiteSpace(options.TargetSessionAttributes)
            || !string.IsNullOrWhiteSpace(options.ReplicaTargetSessionAttributes)
            || options.LoadBalanceHosts.HasValue
            || options.HostRecheckSeconds.HasValue
        );

    /// <summary>
    ///     This method reports whether any MySQL setting differs from its unset value.
    /// </summary>
    private static bool IsConfigured(StorageMySqlOptions options) =>
        options is not null
        && (
            !string.IsNullOrWhiteSpace(options.LoadBalance)
            || !string.IsNullOrWhiteSpace(options.ReplicaLoadBalance)
        );

    /// <summary>
    ///     This method reports whether any SQL Server setting differs from its unset value.
    /// </summary>
    private static bool IsConfigured(StorageSqlServerOptions options) =>
        options is not null && options.MultiSubnetFailover;

    /// <summary>
    ///     This method reports whether any MongoDB setting differs from its unset value.
    /// </summary>
    private static bool IsConfigured(StorageMongoDbOptions options) =>
        options is not null
        && (
            !string.IsNullOrWhiteSpace(options.ReplicaSet)
            || !string.IsNullOrWhiteSpace(options.ReadPreference)
            || options.MaxStalenessSeconds != 0
            || !string.IsNullOrWhiteSpace(options.WriteConcern)
            || options.Journal.HasValue
        );
}
