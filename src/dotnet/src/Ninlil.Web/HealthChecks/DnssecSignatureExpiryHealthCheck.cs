using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Authoritative.Dnssec;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "DNSSEC key expiry / upcoming re-sign due dates"
///     health check, reusing -- rather than duplicating -- <see cref="SignedZoneCache" />'s own
///     already-tracked <see cref="SignedZoneData.SignatureExpiresAtUtc" /> for every currently
///     signed zone, the exact same value <see cref="DnssecResigningBackgroundService" /> itself
///     already checks on its own poll loop. This check exists to surface a zone whose signature
///     is close to lapsing (or, in a genuine failure, has already lapsed) to an operator before a
///     validating client ever notices, not to duplicate that background service's own
///     re-signing logic.
/// </summary>
/// <param name="signedZoneCache">The cache this check reads every signed zone's own signature expiry from.</param>
/// <param name="zoneSigningOptions">
///     This host's own configured re-sign threshold -- reused as this check's own "approaching
///     expiry" warning window, so an operator only ever tunes one setting for both.
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
