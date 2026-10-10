using Microsoft.Data.SqlClient;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Topology;

/// <summary>
///     This static class applies <see cref="StorageSqlServerOptions" /> to the SQL Server
///     connection strings with <see cref="SqlConnectionStringBuilder" />. The read connection
///     always gets <c>ApplicationIntent=ReadOnly</c>, which makes an availability group listener
///     route it to a readable secondary. A string that needs no change is returned exactly as the
///     operator wrote it. A setting that the string also holds with another value is refused,
///     naming both values.
/// </summary>
internal static class SqlServerConnectionStringComposer
{
    private const string MultiSubnetFailoverKey = "MultiSubnetFailover";
    private const string ApplicationIntentKey = "ApplicationIntent";

    /// <summary>
    ///     This method composes the primary and replica strings.
    /// </summary>
    /// <param name="primarySource">The operator's primary connection string.</param>
    /// <param name="replicaSource">The operator's read connection string, or null.</param>
    /// <param name="options">The SQL Server cluster settings.</param>
    /// <param name="warnings">Receives warnings.</param>
    /// <returns>The composed primary and replica (null without a replica connection).</returns>
    internal static (string Primary, string Replica) Compose(
        string primarySource,
        string replicaSource,
        StorageSqlServerOptions options,
        List<string> warnings
    )
    {
        string primary = ComposePrimary(primarySource, options);
        string replica = replicaSource is null
            ? null
            : ComposeReplica(replicaSource, options, primarySource, warnings);
        return (primary, replica);
    }

    private static string ComposePrimary(string source, StorageSqlServerOptions options)
    {
        if (!options.MultiSubnetFailover)
            return source;

        SqlConnectionStringBuilder builder = Parse(source, "Storage:ConnectionString", true);
        if (ApplyMultiSubnetFailover(builder))
            return builder.ConnectionString;
        return source;
    }

    private static string ComposeReplica(
        string source,
        StorageSqlServerOptions options,
        string primarySource,
        List<string> warnings
    )
    {
        SqlConnectionStringBuilder builder = Parse(
            source,
            "Storage:ReadReplica:ConnectionString",
            true
        );

        if (
            builder.ShouldSerialize(ApplicationIntentKey)
            && builder.ApplicationIntent != ApplicationIntent.ReadOnly
        )
            throw StorageTopologyValidator.Refusal(
                "Storage:ReadReplica:ConnectionString has ApplicationIntent "
                    + $"'{builder.ApplicationIntent}', but the read connection must use ReadOnly "
                    + "so that the availability group listener routes it to a secondary."
            );
        builder.ApplicationIntent = ApplicationIntent.ReadOnly;

        if (options.MultiSubnetFailover)
            ApplyMultiSubnetFailover(builder);

        SqlConnectionStringBuilder primary = Parse(
            primarySource,
            "Storage:ConnectionString",
            false
        );
        if (
            primary is not null
            && string.Equals(
                primary.DataSource,
                builder.DataSource,
                StringComparison.OrdinalIgnoreCase
            )
            && string.Equals(
                primary.InitialCatalog,
                builder.InitialCatalog,
                StringComparison.OrdinalIgnoreCase
            )
        )
            warnings.Add(
                "Storage:ReadReplica:ConnectionString names the same server and database as "
                    + "Storage:ConnectionString; with ApplicationIntent=ReadOnly a listener still "
                    + "routes it to a secondary, but a plain server gains nothing from the split."
            );

        return builder.ConnectionString;
    }

    /// <summary>
    ///     This method turns <c>MultiSubnetFailover</c> on and reports whether it changed the
    ///     builder. A string that says false is refused.
    /// </summary>
    private static bool ApplyMultiSubnetFailover(SqlConnectionStringBuilder builder)
    {
        if (builder.ShouldSerialize(MultiSubnetFailoverKey))
        {
            if (builder.MultiSubnetFailover)
                return false;
            throw StorageTopologyValidator.Refusal(
                $"{StorageTopologyValidator.Key(nameof(StorageOptions.SqlServer), nameof(StorageSqlServerOptions.MultiSubnetFailover))} "
                    + "is 'true', but a connection string says 'false'. Set it in one place only."
            );
        }

        builder.MultiSubnetFailover = true;
        return true;
    }

    /// <summary>
    ///     This method parses a connection string. A string that cannot be parsed is refused when
    ///     <paramref name="required" /> is true; otherwise null is returned.
    /// </summary>
    private static SqlConnectionStringBuilder Parse(string source, string key, bool required)
    {
        try
        {
            return new SqlConnectionStringBuilder(source);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            if (required)
                throw StorageTopologyValidator.Refusal(
                    $"{key} is not a valid SQL Server connection string."
                );
            return null;
        }
    }
}
