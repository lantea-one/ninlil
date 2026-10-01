using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Authoritative.Dnssec;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>dnssec</c> health check of <c>GET /status</c>. It reads
///     <see cref="SignedZoneData.SignatureExpiresAtUtc" /> for every zone in
///     <see cref="SignedZoneCache" />, the value <see cref="DnssecResigningBackgroundService" />
///     checks, so an operator sees a zone whose signatures are about to lapse, or have lapsed
///     because re-signing failed, before validating resolvers do. It does not re-sign anything.
///     An expired zone makes it Unhealthy and one inside the re-sign window Degraded; zones are
///     listed by id.
/// </summary>
/// <param name="signedZoneCache">Supplies each signed zone's signature expiry.</param>
/// <param name="zoneSigningOptions">
///     The re-sign threshold (<c>RefreshBeforeExpiration</c>), also used as the warning window
///     so one setting controls both. Read from the settings loaded at startup.
/// </param>
public sealed class DnssecSignatureExpiryHealthCheck(
    SignedZoneCache signedZoneCache,
    IOptions<ZoneSigningOptions> zoneSigningOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyCollection<Guid> cachedZoneIds = signedZoneCache.CachedZoneIds;

        if (cachedZoneIds.Count == 0)
            return Task.FromResult(
                HealthCheckResult.Healthy("No DNSSEC-signed zones are currently cached.")
            );

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<Guid> expired = [];
        List<Guid> approachingExpiry = [];

        foreach (Guid zoneId in cachedZoneIds)
        {
            SignedZoneData signedData = signedZoneCache.GetSignedData(zoneId);

            if (signedData is null)
                continue;

            if (signedData.SignatureExpiresAtUtc <= now)
                expired.Add(zoneId);
            else if (
                signedData.SignatureExpiresAtUtc - now
                <= zoneSigningOptions.Value.RefreshBeforeExpiration
            )
                approachingExpiry.Add(zoneId);
        }

        if (expired.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"{expired.Count} signed zone(s) have signatures that have already expired: "
                        + string.Join(", ", expired)
                        + "."
                )
            );

        if (approachingExpiry.Count > 0)
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    $"{approachingExpiry.Count} signed zone(s) are due for a proactive re-sign soon: "
                        + string.Join(", ", approachingExpiry)
                        + "."
                )
            );

        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"All {cachedZoneIds.Count} DNSSEC-signed zone(s) have signatures well within their validity period."
            )
        );
    }
}
