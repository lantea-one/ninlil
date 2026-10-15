using System.Collections.Concurrent;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Refresh;

/// <summary>
///     This class is the in-memory view of which secondary zones have expired (RFC 1035 §4.3.5:
///     when a secondary cannot reach any primary for the SOA Expire interval, it stops answering
///     for the zone). <see cref="SecondaryZoneRefreshService" /> fills and updates it from the
///     persisted refresh state; <see cref="Resolution.AuthoritativeQueryResolver" /> reads it on
///     every query and <see cref="Transfer.ZoneTransferService" /> on every transfer, so the read
///     path takes no lock and touches no database. A zone with no entry never expires: that holds
///     for Primary and Stub zones, for a secondary with no local SOA, and for SOA Expire 0.
/// </summary>
/// <param name="timeProvider">
///     The clock the expiry time is compared with; tests inject a manual one.
/// </param>
public sealed class SecondaryZoneExpiryTracker(TimeProvider timeProvider)
{
    /// <summary>
    ///     This field records, per zone, the UTC time from which the zone counts as expired.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _expiresAtUtc = new();

    /// <summary>
    ///     This method tells whether a zone has expired: an expiry time is set and has been reached.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    /// <returns>
    ///     <see langword="true" /> when the zone must answer SERVFAIL instead of from its copy.
    /// </returns>
    public bool IsExpired(Guid zoneId) =>
        _expiresAtUtc.TryGetValue(zoneId, out DateTimeOffset expiresAtUtc)
        && timeProvider.GetUtcNow() >= expiresAtUtc;

    /// <summary>
    ///     This method sets when a zone expires, replacing an earlier time.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    /// <param name="expiresAtUtc">
    ///     The time from which the zone counts as expired, or <see langword="null" /> when it
    ///     never expires.
    /// </param>
    internal void SetExpiresAt(Guid zoneId, DateTimeOffset? expiresAtUtc)
    {
        if (expiresAtUtc is { } value)
            _expiresAtUtc[zoneId] = value;
        else
            _expiresAtUtc.TryRemove(zoneId, out _);
    }

    /// <summary>
    ///     This method forgets a zone, for example after it is deleted or is no longer a secondary.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    internal void Remove(Guid zoneId) => _expiresAtUtc.TryRemove(zoneId, out _);

    /// <summary>
    ///     This method reads when a zone expires.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    /// <returns>The expiry time, or <see langword="null" /> when the zone never expires.</returns>
    internal DateTimeOffset? GetExpiresAt(Guid zoneId) =>
        _expiresAtUtc.TryGetValue(zoneId, out DateTimeOffset expiresAtUtc) ? expiresAtUtc : null;
}
