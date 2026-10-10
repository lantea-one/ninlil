// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:MongoDb</c> section (nested as
///     <see cref="StorageOptions.MongoDb" />, so it has no section constant of its own). It holds
///     the replica set settings that Ninlil validates and writes into the driver's client
///     settings. They apply only when <see cref="StorageOptions.Engine" /> is MongoDB. A value
///     that is also present in the connection string with a different value stops the start.
/// </summary>
public class StorageMongoDbOptions
{
    /// <summary>
    ///     This property contains the replica set name (<c>replicaSet</c>). It is empty for a
    ///     standalone server or when the connection string already names it.
    /// </summary>
    public string ReplicaSet { get; set; } = "";

    /// <summary>
    ///     This property contains the read preference for work that tolerates replica lag:
    ///     <c>Primary</c>, <c>PrimaryPreferred</c>, <c>Secondary</c>, <c>SecondaryPreferred</c> or
    ///     <c>Nearest</c>. Work that needs the primary always uses <c>Primary</c>. When it is empty,
    ///     <c>Primary</c> is used and nothing is routed to a secondary.
    /// </summary>
    public string ReadPreference { get; set; } = "";

    /// <summary>
    ///     This property contains the driver's <c>maxStalenessSeconds</c> for the read preference.
    ///     0 leaves it unset; any other value must be at least 90, the driver's minimum.
    /// </summary>
    public int MaxStalenessSeconds { get; set; }

    /// <summary>
    ///     This property contains the write concern: <c>Majority</c>, <c>Acknowledged</c> (w:1) or
    ///     <c>W2</c> to <c>W9</c>. When it is empty, <c>Majority</c> is used on a replica set and the
    ///     connection string's own write concern on a standalone server.
    /// </summary>
    public string WriteConcern { get; set; } = "";

    /// <summary>
    ///     This property contains the write concern's journal flag (<c>j</c>). When it is not set,
    ///     <c>true</c> is used with a write concern taken from <see cref="WriteConcern" /> or the
    ///     replica set default.
    /// </summary>
    public bool? Journal { get; set; }
}
