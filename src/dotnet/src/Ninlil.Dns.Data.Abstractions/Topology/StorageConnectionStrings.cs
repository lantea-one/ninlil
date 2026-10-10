using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Topology;

/// <summary>
///     This class holds the connection strings that Ninlil actually uses for one storage
///     configuration: the operator's <see cref="StorageOptions.ConnectionString" /> and
///     <see cref="StorageReadReplicaOptions.ConnectionString" /> with the cluster settings of
///     <c>Storage:PostgreSql</c>, <c>Storage:MySql</c>, <c>Storage:SqlServer</c> and
///     <c>Storage:MongoDb</c> applied. The per-engine composers
///     (<c>StorageConnectionStringComposer</c> for the relational engines,
///     <c>MongoClientSettingsComposer</c> for MongoDB) build it and refuse a configuration that
///     contradicts itself. When no cluster setting applies, <see cref="Primary" /> is the
///     operator's string unchanged.
/// </summary>
/// <param name="engine">The engine the strings were composed for.</param>
/// <param name="sourceConnectionString">The operator's connection string the composition started from.</param>
/// <param name="primary">The connection string for writes, migrations and snapshots.</param>
/// <param name="replica">The read-only connection string, or null when there is no replica connection.</param>
/// <param name="warnings">Messages about settings that are ignored or have no effect; they never contain secrets.</param>
public sealed class StorageConnectionStrings(
    StorageEngineEnum engine,
    string sourceConnectionString,
    string primary,
    string replica,
    IReadOnlyList<string> warnings
)
{
    /// <summary>
    ///     This property contains the engine the strings were composed for.
    /// </summary>
    public StorageEngineEnum Engine { get; } = engine;

    /// <summary>
    ///     This property contains the operator's connection string the composition started from.
    ///     It is kept so a holder can tell whether <see cref="StorageOptions" /> has changed since.
    /// </summary>
    public string SourceConnectionString { get; } = sourceConnectionString;

    /// <summary>
    ///     This property contains the connection string every write, migration and snapshot uses.
    /// </summary>
    public string Primary { get; } = primary;

    /// <summary>
    ///     This property contains the read-only connection string, or null when none is
    ///     configured. It is always null for SQLite and MongoDB.
    /// </summary>
    public string Replica { get; } = replica;

    /// <summary>
    ///     This property contains the messages about settings that are ignored or have no effect.
    ///     They name settings and hosts, never passwords.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; } = warnings;

    /// <summary>
    ///     This method reports whether these strings were composed from the engine and connection
    ///     string that <paramref name="options" /> holds now. The storage switch changes both in
    ///     place at startup, so a holder uses this to know when to compose again.
    /// </summary>
    /// <param name="options">The storage configuration to compare with.</param>
    /// <returns>True when engine and connection string are the ones composed from.</returns>
    public bool IsComposedFrom(StorageOptions options) =>
        options.Engine == Engine
        && string.Equals(
            options.ConnectionString,
            SourceConnectionString,
            StringComparison.Ordinal
        );
}
