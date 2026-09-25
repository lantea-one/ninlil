using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This hosted service re-signs each locally signed zone
///     (<see cref="SignedZoneCache.CachedZoneIds" />) when its signatures are within
///     <see cref="ZoneSigningOptions.RefreshBeforeExpiration" /> of expiring. Signatures expire
///     even when a zone never changes, which <see cref="SignedZoneChangedEventSubscriber" /> alone
///     would not handle. It checks every <see cref="ZoneSigningOptions.PollInterval" />.
/// </summary>
/// <param name="signedZoneCache">The cache this service checks and refreshes.</param>
/// <param name="options">
///     The poll interval and refresh threshold, read through
///     <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> on every iteration.
///     <see cref="IOptions{TOptions}.Value" /> would return the startup value forever, however
///     often it is read.
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
                    // Not ReloadZoneAsync: nothing changed but the date, so the serial must be
                    // bumped for secondaries to fetch the new signatures.
                    await signedZoneCache.ResignForScheduledExpiryAsync(zoneId, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One zone's failure (a deleted key, say) must not stop the loop.
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
                // Expected on shutdown; the loop condition ends the loop.
            }
        }
    }
}
