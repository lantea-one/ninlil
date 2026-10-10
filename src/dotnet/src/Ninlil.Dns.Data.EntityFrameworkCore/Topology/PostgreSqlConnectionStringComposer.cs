using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Topology;
using Npgsql;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Topology;

/// <summary>
///     This static class applies <see cref="StoragePostgreSqlOptions" /> to the PostgreSQL
///     connection strings with <see cref="NpgsqlConnectionStringBuilder" />. A string that needs
///     no change is returned exactly as the operator wrote it. A setting that the string also
///     holds with another value is refused, naming both values.
/// </summary>
internal static class PostgreSqlConnectionStringComposer
{
    private const string TargetSessionAttributesKey = "Target Session Attributes";
    private const string LoadBalanceHostsKey = "Load Balance Hosts";
    private const string HostRecheckSecondsKey = "Host Recheck Seconds";

    /// <summary>
    ///     This field maps every spelling Ninlil accepts (compared without case, dashes and
    ///     underscores) to the token Npgsql writes into a connection string.
    /// </summary>
    private static readonly Dictionary<string, string> SessionAttributes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["any"] = "any",
        ["readwrite"] = "read-write",
        ["readonly"] = "read-only",
        ["primary"] = "primary",
        ["standby"] = "standby",
        ["preferprimary"] = "prefer-primary",
        ["preferstandby"] = "prefer-standby",
    };

    /// <summary>
    ///     This method composes the primary and replica strings.
    /// </summary>
    /// <param name="primarySource">The operator's primary connection string.</param>
    /// <param name="replicaSource">The operator's read connection string, or null.</param>
    /// <param name="options">The PostgreSQL cluster settings.</param>
    /// <param name="warnings">Receives warnings.</param>
    /// <returns>The composed primary and replica (null without a replica connection).</returns>
    internal static (string Primary, string Replica) Compose(
        string primarySource,
        string replicaSource,
        StoragePostgreSqlOptions options,
        List<string> warnings
    )
    {
        string primary = ComposePrimary(primarySource, options);
        string replica = replicaSource is null
            ? null
            : ComposeReplica(replicaSource, options, primarySource, warnings);
        return (primary, replica);
    }

    private static string ComposePrimary(string source, StoragePostgreSqlOptions options)
    {
        string optionAttributes = Normalize(
            options.TargetSessionAttributes,
            nameof(StoragePostgreSqlOptions.TargetSessionAttributes)
        );
        if (optionAttributes is not null && optionAttributes is not ("read-write" or "primary"))
            throw Refusal(
                nameof(StoragePostgreSqlOptions.TargetSessionAttributes),
                "must be ReadWrite or Primary for the primary connection; "
                    + $"it is '{options.TargetSessionAttributes}'."
            );

        bool anyOption =
            optionAttributes is not null
            || options.LoadBalanceHosts.HasValue
            || options.HostRecheckSeconds.HasValue;
        NpgsqlConnectionStringBuilder builder = Parse(
            source,
            "Storage:ConnectionString",
            anyOption
        );
        if (builder is null)
            return source;

        bool changed = false;
        bool multiHost = Hosts(builder).Count > 1;

        string stringAttributes = Has(builder, TargetSessionAttributesKey)
            ? Normalize(builder.TargetSessionAttributes, null)
            : null;
        RequireSame(
            nameof(StoragePostgreSqlOptions.TargetSessionAttributes),
            optionAttributes,
            stringAttributes
        );
        string attributes = optionAttributes ?? stringAttributes;

        // A failover only finds the new primary when the driver skips hosts that are not
        // writable, so a primary connection with several hosts needs read-write.
        if (attributes is null && multiHost)
            attributes = "read-write";
        if (
            attributes is not null
            && (optionAttributes is not null || multiHost)
            && attributes is not ("read-write" or "primary")
        )
            throw Refusal(
                nameof(StoragePostgreSqlOptions.TargetSessionAttributes),
                $"the primary connection has Target Session Attributes '{attributes}', but it must be "
                    + "read-write or primary so that writes never reach a standby."
            );
        if (attributes is not null && !Has(builder, TargetSessionAttributesKey))
        {
            builder.TargetSessionAttributes = attributes;
            changed = true;
        }

        changed |= ApplyShared(builder, options);
        return changed ? builder.ConnectionString : source;
    }

    private static string ComposeReplica(
        string source,
        StoragePostgreSqlOptions options,
        string primarySource,
        List<string> warnings
    )
    {
        NpgsqlConnectionStringBuilder builder = Parse(
            source,
            "Storage:ReadReplica:ConnectionString",
            true
        );

        string optionAttributes = Normalize(
            options.ReplicaTargetSessionAttributes,
            nameof(StoragePostgreSqlOptions.ReplicaTargetSessionAttributes)
        );
        string stringAttributes = Has(builder, TargetSessionAttributesKey)
            ? Normalize(builder.TargetSessionAttributes, null)
            : null;
        RequireSame(
            nameof(StoragePostgreSqlOptions.ReplicaTargetSessionAttributes),
            optionAttributes,
            stringAttributes
        );
        string attributes = optionAttributes ?? stringAttributes ?? "prefer-standby";
        if (attributes is "read-write" or "primary")
            throw Refusal(
                nameof(StoragePostgreSqlOptions.ReplicaTargetSessionAttributes),
                $"the read connection has Target Session Attributes '{attributes}', which would "
                    + "send reads to the primary only. Use PreferStandby, Standby or ReadOnly."
            );
        // Npgsql refuses any Target Session Attributes other than Any on a single host, so the
        // defaults below belong to a read connection that lists several hosts. A single host is
        // the standby itself.
        bool multiHost = Hosts(builder).Count > 1;
        if (
            !Has(builder, TargetSessionAttributesKey) && (multiHost || optionAttributes is not null)
        )
            builder.TargetSessionAttributes = attributes;

        // Balancing across the standbys is the point of a multi-host read connection.
        if (multiHost && !options.LoadBalanceHosts.HasValue && !Has(builder, LoadBalanceHostsKey))
            builder.LoadBalanceHosts = true;
        ApplyShared(builder, options);

        NpgsqlConnectionStringBuilder primary = Parse(
            primarySource,
            "Storage:ConnectionString",
            false
        );
        if (
            primary is not null
            && SameServer(Hosts(primary), primary.Database, Hosts(builder), builder.Database)
        )
            warnings.Add(
                "Storage:ReadReplica:ConnectionString names the same hosts and database as "
                    + "Storage:ConnectionString; reads gain nothing from the split."
            );

        return builder.ConnectionString;
    }

    /// <summary>
    ///     This method applies the settings that both connections share and reports whether it
    ///     changed the builder.
    /// </summary>
    private static bool ApplyShared(
        NpgsqlConnectionStringBuilder builder,
        StoragePostgreSqlOptions options
    )
    {
        bool changed = false;

        if (options.LoadBalanceHosts.HasValue)
        {
            if (Has(builder, LoadBalanceHostsKey))
                RequireSame(
                    nameof(StoragePostgreSqlOptions.LoadBalanceHosts),
                    options.LoadBalanceHosts.Value.ToString().ToLowerInvariant(),
                    builder.LoadBalanceHosts.ToString().ToLowerInvariant()
                );
            else
            {
                builder.LoadBalanceHosts = options.LoadBalanceHosts.Value;
                changed = true;
            }
        }

        if (options.HostRecheckSeconds.HasValue)
        {
            if (options.HostRecheckSeconds.Value < 0)
                throw Refusal(
                    nameof(StoragePostgreSqlOptions.HostRecheckSeconds),
                    $"is {options.HostRecheckSeconds.Value}, but must not be negative."
                );
            if (Has(builder, HostRecheckSecondsKey))
                RequireSame(
                    nameof(StoragePostgreSqlOptions.HostRecheckSeconds),
                    options.HostRecheckSeconds.Value.ToString(),
                    builder.HostRecheckSeconds.ToString()
                );
            else
            {
                builder.HostRecheckSeconds = options.HostRecheckSeconds.Value;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    ///     This method parses a connection string. A string that cannot be parsed is refused when
    ///     <paramref name="required" /> is true; otherwise the caller keeps it unchanged and the
    ///     failure shows when the driver connects, as it did before.
    /// </summary>
    private static NpgsqlConnectionStringBuilder Parse(string source, string key, bool required)
    {
        try
        {
            return new NpgsqlConnectionStringBuilder(source);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            if (required)
                throw StorageTopologyValidator.Refusal(
                    $"{key} is not a valid PostgreSQL connection string."
                );
            return null;
        }
    }

    private static bool Has(NpgsqlConnectionStringBuilder builder, string key) =>
        builder.ShouldSerialize(key);

    /// <summary>
    ///     This method turns a value into Npgsql's token, or null for an empty value. A value
    ///     that comes from a connection string (<paramref name="setting" /> is null) is returned
    ///     lowercased when unknown, so the driver, not Ninlil, reports it.
    /// </summary>
    private static string Normalize(string value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string compact = value.Replace("-", "").Replace("_", "").Replace(" ", "");
        if (SessionAttributes.TryGetValue(compact, out string token))
            return token;
        if (setting is null)
            return value.ToLowerInvariant();
        throw Refusal(
            setting,
            $"'{value}' is not one of ReadWrite, ReadOnly, Primary, Standby, PreferPrimary, "
                + "PreferStandby or Any."
        );
    }

    /// <summary>
    ///     This method refuses a setting that disagrees with the same key in the connection
    ///     string, naming both values.
    /// </summary>
    private static void RequireSame(string setting, string optionValue, string stringValue)
    {
        if (
            optionValue is not null
            && stringValue is not null
            && !string.Equals(optionValue, stringValue, StringComparison.OrdinalIgnoreCase)
        )
            throw Refusal(
                setting,
                $"is '{optionValue}', but the connection string says '{stringValue}'. "
                    + "Set it in one place only."
            );
    }

    /// <summary>
    ///     This method lists the hosts of the string as lowercase <c>host:port</c> entries. An
    ///     entry without a port gets the string's <c>Port</c>, so two <c>localhost</c> strings on
    ///     different ports are different servers.
    /// </summary>
    private static List<string> Hosts(NpgsqlConnectionStringBuilder builder) =>
        (builder.Host ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(host =>
            {
                // One colon is "host:port"; more is an IPv6 address, which carries a port only
                // after a closing bracket.
                bool hasPort =
                    host.Count(character => character == ':') == 1
                    || (host.StartsWith('[') && host.Contains("]:", StringComparison.Ordinal));
                return (hasPort ? host : $"{host}:{builder.Port}").ToLowerInvariant();
            })
            .ToList();

    private static bool SameServer(
        List<string> hostsA,
        string databaseA,
        List<string> hostsB,
        string databaseB
    ) =>
        string.Equals(databaseA, databaseB, StringComparison.OrdinalIgnoreCase)
        && hostsA.Order().SequenceEqual(hostsB.Order());

    private static DatabaseStartupRefusedException Refusal(string setting, string message) =>
        StorageTopologyValidator.Refusal(
            $"{StorageTopologyValidator.Key(nameof(StorageOptions.PostgreSql), setting)} {message}"
        );
}
