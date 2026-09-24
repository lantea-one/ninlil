using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class proactively re-signs a zone before its signatures actually expire, even when
///     nothing about the zone's own data has changed at all -- the other half of the offline
///     signing design that <see cref="SignedZoneChangedEventSubscriber" /> alone cannot cover, since a
///     signature's own validity period runs out on the calendar regardless of whether
///     <c>DnsZoneChangedEvent</c> ever fires again.
/// </summary>
/// <param name="signedZoneCache">The cache this service checks and refreshes.</param>
/// <param name="options">
///     This service's own polling and refresh-threshold configuration, read fresh
///     (<see cref="IOptionsMonitor{TOptions}.CurrentValue" />) on every loop iteration rather than
///     snapshotted once. This service is a singleton hosted service whose <see cref="ExecuteAsync" />
///     loop already re-reads <c>options.Value</c> on every iteration -- but the plain
///     <see cref="IOptions{TOptions}" /> that expression read from is itself computed once, lazily,
///     and cached for the life of the process regardless of how many times <c>.Value</c> is
///     accessed, so a configuration-file write changing <see cref="ZoneSigningOptions.PollInterval" />/
///     <see cref="ZoneSigningOptions.RefreshBeforeExpiration" /> would never actually have been
///     observed by this already-running loop at all, despite superficially looking like it
///     would be -- the same <see cref="IOptions{TOptions}" />-vs-<see cref="IOptionsMonitor{TOptions}" />
///     staleness trap several other hot-reloadable options classes in this codebase guard
///     against the same way.
/// </param>
/// <param name="logger">The logger this service records re-signing activity to.</param>
public class DnssecResigningBackgroundService(
    SignedZoneCache signedZoneCache,
    IOptionsMonitor<ZoneSigningOptions> options,
    ILogger<DnssecResigningBackgroundService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (Guid zoneId in signedZoneCache.CachedZoneIds)
            {
                stoppingToken.ThrowIfCancellationRequested();

                SignedZoneData signedData = signedZoneCache.GetSignedData(zoneId);

                if (signedData is null)
                    continue;

                DateTimeOffset refreshAt =
                    signedData.SignatureExpiresAtUtc - options.CurrentValue.RefreshBeforeExpiration;

                if (DateTimeOffset.UtcNow < refreshAt)
                    continue;

                try
                {
                    // ResignForScheduledExpiryAsync -- not the plain ReloadZoneAsync every other
                    // caller in this project uses -- since this specific call site is the one
                    // genuine "nothing about this zone's own data changed, only the calendar did"
                    // trigger: it also bumps the zone's own SOA serial first, so a secondary that
                    // already believes itself current notices a fresh signature actually exists
                    // to pull, per this project's own confirmed decision that a re-sign is a real
                    // content change.
                    await signedZoneCache.ResignForScheduledExpiryAsync(zoneId, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A single zone's signing failure (a key that was deleted out from under it,
                    // for example) must not take down the loop checking every other zone.
                    logger.LogError(
                        exception,
                        "Failed to proactively re-sign zone '{ZoneId}' before its signatures expired.",
                        zoneId
                    );
                }
            }

            try
            {
                await Task.Delay(options.CurrentValue.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown -- the loop condition above ends the loop cleanly.
            }
        }
    }
}
