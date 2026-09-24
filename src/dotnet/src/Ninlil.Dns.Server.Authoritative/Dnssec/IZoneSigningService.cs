// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This interface computes a signed zone's complete DNSSEC content -- the offline signing
///     pipeline this project deliberately runs on zone change and on a schedule, never live
///     per query.
/// </summary>
public interface IZoneSigningService
{
    /// <summary>
    ///     This method (re)signs a zone from its current, stored records.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to sign.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The zone's freshly computed signed content.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no zone with <paramref name="zoneId" /> exists, the zone is not marked
    ///     <c>IsDnssecSigned</c>, or the zone has no active DNSSEC key at all.
    /// </exception>
    Task<SignedZoneData> SignZoneAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
