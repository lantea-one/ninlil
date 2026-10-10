using MySqlConnector;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Topology;

/// <summary>
///     This static class applies <see cref="StorageMySqlOptions" /> to the MySQL connection
///     strings with <see cref="MySqlConnectionStringBuilder" />. A string that needs no change is
///     returned exactly as the operator wrote it. A setting that the string also holds with
///     another value is refused, naming both values.
///     <para>
///         MySqlConnector cannot tell a writable server from a read-only one. A primary
///         connection with several hosts therefore uses <c>FailOver</c> only, which is safe behind
///         an external failover that leaves exactly one host writable.
///     </para>
/// </summary>
internal static class MySqlConnectionStringComposer
{
    private const string LoadBalanceKey = "Load Balance";

    /// <summary>
    ///     This method composes the primary and replica strings.
    /// </summary>
    /// <param name="primarySource">The operator's primary connection string.</param>
    /// <param name="replicaSource">The operator's read connection string, or null.</param>
    /// <param name="options">The MySQL cluster settings.</param>
    /// <param name="warnings">Receives warnings.</param>
    /// <returns>The composed primary and replica (null without a replica connection).</returns>
    internal static (string Primary, string Replica) Compose(
        string primarySource,
        string replicaSource,
        StorageMySqlOptions options,
        List<string> warnings
    )
    {
        string primary = ComposePrimary(primarySource, options);
        string replica = replicaSource is null
            ? null
            : ComposeReplica(replicaSource, options, primarySource, warnings);
        return (primary, replica);
    }

    private static string ComposePrimary(string source, StorageMySqlOptions options)
    {
        MySqlLoadBalance? optionMode = Parse(
            options.LoadBalance,
            nameof(StorageMySqlOptions.LoadBalance)
        );
        if (optionMode.HasValue && optionMode.Value != MySqlLoadBalance.FailOver)
            throw Refusal(
                nameof(StorageMySqlOptions.LoadBalance),
                $"is '{options.LoadBalance}', but the primary connection allows FailOver only: "
                    + "a load balanced write could reach a read-only server."
            );

        MySqlConnectionStringBuilder builder = ParseConnection(
            source,
            "Storage:ConnectionString",
            optionMode.HasValue
        );
        if (builder is null)
            return source;

        bool stringHasMode = builder.ShouldSerialize(LoadBalanceKey);
        if (stringHasMode)
        {
            if (optionMode.HasValue && builder.LoadBalance != optionMode.Value)
                throw Refusal(
                    nameof(StorageMySqlOptions.LoadBalance),
                    $"is '{optionMode.Value}', but the connection string says '{builder.LoadBalance}'. "
                        + "Set it in one place only."
                );
            if (Hosts(builder).Count > 1 && builder.LoadBalance != MySqlLoadBalance.FailOver)
                throw Refusal(
                    nameof(StorageMySqlOptions.LoadBalance),
                    $"the primary connection string lists several hosts with LoadBalance "
                        + $"'{builder.LoadBalance}', but the primary allows FailOver only."
                );
            return source;
        }

        if (!optionMode.HasValue)
            return source;
        builder.LoadBalance = optionMode.Value;
        return builder.ConnectionString;
    }

    private static string ComposeReplica(
        string source,
        StorageMySqlOptions options,
        string primarySource,
        List<string> warnings
    )
    {
        MySqlLoadBalance? optionMode = Parse(
            options.ReplicaLoadBalance,
            nameof(StorageMySqlOptions.ReplicaLoadBalance)
        );
        MySqlConnectionStringBuilder builder = ParseConnection(
            source,
            "Storage:ReadReplica:ConnectionString",
            true
        );

        if (builder.ShouldSerialize(LoadBalanceKey))
        {
            if (optionMode.HasValue && builder.LoadBalance != optionMode.Value)
                throw Refusal(
                    nameof(StorageMySqlOptions.ReplicaLoadBalance),
                    $"is '{optionMode.Value}', but the read connection string says "
                        + $"'{builder.LoadBalance}'. Set it in one place only."
                );
        }
        else if (optionMode.HasValue)
            builder.LoadBalance = optionMode.Value;
        else if (Hosts(builder).Count > 1)
            builder.LoadBalance = MySqlLoadBalance.RoundRobin;

        MySqlConnectionStringBuilder primary = ParseConnection(
            primarySource,
            "Storage:ConnectionString",
            false
        );
        if (primary is not null && SameServer(primary, builder))
            warnings.Add(
                "Storage:ReadReplica:ConnectionString names the same hosts and database as "
                    + "Storage:ConnectionString; reads gain nothing from the split."
            );

        return builder.ConnectionString;
    }

    /// <summary>
    ///     This method parses a mode name, or returns null for an empty value.
    /// </summary>
    private static MySqlLoadBalance? Parse(string value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (
            Enum.TryParse(value.Trim(), true, out MySqlLoadBalance mode)
            && Enum.IsDefined(mode)
            && !int.TryParse(value, out _)
        )
            return mode;
        throw Refusal(
            setting,
            $"'{value}' is not one of RoundRobin, Random, LeastConnections or FailOver."
        );
    }

    /// <summary>
    ///     This method parses a connection string. A string that cannot be parsed is refused when
    ///     <paramref name="required" /> is true; otherwise the caller keeps it unchanged.
    /// </summary>
    private static MySqlConnectionStringBuilder ParseConnection(
        string source,
        string key,
        bool required
    )
    {
        try
        {
            return new MySqlConnectionStringBuilder(source);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            if (required)
                throw StorageTopologyValidator.Refusal(
                    $"{key} is not a valid MySQL connection string."
                );
            return null;
        }
    }

    private static List<string> Hosts(MySqlConnectionStringBuilder builder) =>
        (builder.Server ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(host => $"{host.ToLowerInvariant()}:{builder.Port}")
            .ToList();

    private static bool SameServer(
        MySqlConnectionStringBuilder a,
        MySqlConnectionStringBuilder b
    ) =>
        string.Equals(a.Database, b.Database, StringComparison.OrdinalIgnoreCase)
        && Hosts(a).Order().SequenceEqual(Hosts(b).Order());

    private static DatabaseStartupRefusedException Refusal(string setting, string message) =>
        StorageTopologyValidator.Refusal(
            $"{StorageTopologyValidator.Key(nameof(StorageOptions.MySql), setting)} {message}"
        );
}
