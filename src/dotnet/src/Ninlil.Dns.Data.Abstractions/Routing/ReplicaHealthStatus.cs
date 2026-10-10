// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Routing;

/// <summary>
///     This class is the result of one replica health check, as the
///     <see cref="ReplicaHealthMonitor" /> publishes it to the <see cref="IStorageReadRouter" />.
///     It is immutable. The router sends reads to the replica only while
///     <see cref="IsEligible" /> is true; the storage status API and the health check read the
///     other members.
/// </summary>
/// <param name="isEligible">Whether the replica may serve reads.</param>
/// <param name="lagSeconds">The measured lag in seconds, or null when it is unknown.</param>
/// <param name="schemaCurrent">Whether the replica has the primary's schema version.</param>
/// <param name="checkedAtUtc">When the check finished, or null before the first check.</param>
/// <param name="reason">
///     Why the replica is not eligible, in plain words, or an empty string when it is. It never
///     contains a connection string.
/// </param>
public sealed class ReplicaHealthStatus(
    bool isEligible,
    double? lagSeconds,
    bool schemaCurrent,
    DateTimeOffset? checkedAtUtc,
    string reason
)
{
    /// <summary>
    ///     This property contains the status before the first check: not eligible, so a
    ///     just-migrated primary is never read through a replica that has not been looked at.
    /// </summary>
    public static ReplicaHealthStatus NotChecked { get; } =
        new(false, null, false, null, "The replica has not been checked yet.");

    /// <summary>
    ///     This property reports whether the replica may serve reads.
    /// </summary>
    public bool IsEligible { get; } = isEligible;

    /// <summary>
    ///     This property contains the measured lag in seconds, or null when it is unknown.
    /// </summary>
    public double? LagSeconds { get; } = lagSeconds;

    /// <summary>
    ///     This property reports whether the replica has the primary's schema version.
    /// </summary>
    public bool SchemaCurrent { get; } = schemaCurrent;

    /// <summary>
    ///     This property contains when the check finished, or null before the first check.
    /// </summary>
    public DateTimeOffset? CheckedAtUtc { get; } = checkedAtUtc;

    /// <summary>
    ///     This property contains why the replica is not eligible, or an empty string when it is.
    /// </summary>
    public string Reason { get; } = reason;
}
