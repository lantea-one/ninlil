// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This interface computes a zone's complete DNSSEC content from its stored records and keys.
///     <see cref="SignedZoneCache" /> calls it when a zone changes and on the re-signing
///     schedule; nothing is signed per query. The implementation is
///     <see cref="ZoneSigningService" />.
/// </summary>
public interface IZoneSigningService
{
    /// <summary>
    ///     This method signs a zone from its current stored records.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to sign.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The zone's signed content, or <see langword="null" /> when the zone is marked signed
    ///     but no key can sign (every key uses an algorithm prohibited for signing, or none is
    ///     Active or in DoubleSignature). The zone is then served unsigned.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when no zone with <paramref name="zoneId" /> exists, the zone is not marked
    ///     <c>IsDnssecSigned</c>, or the zone has no active DNSSEC key at all.
    /// </exception>
    Task<SignedZoneData> SignZoneAsync(Guid zoneId, CancellationToken stoppingToken = default);
}
