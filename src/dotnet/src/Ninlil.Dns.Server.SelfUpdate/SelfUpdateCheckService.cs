using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class periodically checks <see cref="SelfUpdateOptions.ReleaseFeedUrl" /> for a
///     newer release of this server than the one currently running, recording the result into
///     <see cref="ISelfUpdateStatusCache" /> -- never applying anything itself. See
///     <see cref="SelfUpdateOptions" />'s own doc comment for the full "check-only, forever"
///     reasoning.
///     <para>
///         <b>A real check runs immediately at startup</b> (rather than only after the first full
///         <see cref="SelfUpdateOptions.CheckInterval" /> elapses), so an operator who just
///         restarted onto a brand-new build sees an accurate notice right away instead of waiting
///         up to a full day for the first tick. From then on, this service wakes every
///         <see cref="TickInterval" /> (independent of, and shorter than, any configured
///         <see cref="SelfUpdateOptions.CheckInterval" />) and re-checks
///         <see cref="SelfUpdateOptions.EnableAutomaticCheck" />/
///         <see cref="SelfUpdateOptions.CheckInterval" /> fresh on every tick -- the same "checked live, every iteration, not just once before a loop that only
///         ever runs once per process" shape <c>DnsFilterListExternalRefreshService</c> already
///         establishes for an identical periodic-external-fetch scenario, so a live
///         <c>Controllers.SelfUpdateSettingsController.Patch</c> write (turning checking on, or
///         shortening the interval) is honored on the very next tick, no restart required.
///     </para>
/// </summary>
/// <param name="httpClientFactory">The factory this service fetches the release feed document through.</param>
/// <param name="options">This service's own live, hot-reloadable settings.</param>
/// <param name="statusCache">The shared cache this service records every check's own outcome into.</param>
/// <param name="currentVersionProvider">The provider this service asks for the running process's own current version.</param>
/// <param name="logger">The logger this service records a failed check to.</param>
public sealed class SelfUpdateCheckService(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<SelfUpdateOptions> options,
    ISelfUpdateStatusCache statusCache,
    ICurrentAssemblyVersionProvider currentVersionProvider,
    ILogger<SelfUpdateCheckService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service wakes up to decide whether a real check
    ///     is actually due, per the currently-configured <see cref="SelfUpdateOptions.CheckInterval" />
    ///     -- independent of, and deliberately shorter than, that interval's own usual (24-hour)
    ///     default, so a shortened interval written through a live settings patch takes effect
    ///     promptly rather than only after whatever the *previous* interval happened to be.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This constant contains the name this service registers its own <see cref="HttpClient" />
    ///     under via <see cref="IHttpClientFactory" />.
    /// </summary>
    public const string HttpClientName = "Ninlil.Dns.Server.SelfUpdate";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check once, immediately, before ever waiting out a full tick -- see this class's own
        // doc comment for why.
        await RunOneCheckIfDueAsync(force: true, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunOneCheckIfDueAsync(force: false, stoppingToken);
        }
    }

    /// <summary>
    ///     This method runs a real check now, unconditionally -- the shared logic behind both
    ///     this service's own periodic loop and a manual "check now" REST action, so both paths
    ///     observe and produce identical results through <see cref="ISelfUpdateStatusCache" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The fresh status snapshot this check produced.</returns>
    public async Task<SelfUpdateStatus> CheckNowAsync(CancellationToken stoppingToken = default)
    {
        Version currentVersion = currentVersionProvider.GetCurrentVersion();
        string releaseFeedUrl = options.CurrentValue.ReleaseFeedUrl;

        if (string.IsNullOrWhiteSpace(releaseFeedUrl))
        {
            // No feed configured yet -- an honest, quiet "unconfigured" snapshot, not a failure.
            // See SelfUpdateOptions.ReleaseFeedUrl's own doc comment.
            SelfUpdateStatus unconfigured = SelfUpdateStatus.Empty(currentVersion) with
            {
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
            };
            statusCache.Update(unconfigured);
            return unconfigured;
        }

        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            SelfUpdateReleaseFeedDocument document =
                await client.GetFromJsonAsync<SelfUpdateReleaseFeedDocument>(
                    releaseFeedUrl,
                    stoppingToken
                );

            if (document is null || string.IsNullOrWhiteSpace(document.Version))
                throw new InvalidOperationException(
                    "The release feed document is missing a 'version' field."
                );

            if (!Version.TryParse(document.Version, out Version latestVersion))
                throw new InvalidOperationException(
                    $"'{document.Version}' is not a valid dotted version string."
                );

            SelfUpdateStatus status = new(
                true,
                currentVersion,
                latestVersion,
                latestVersion > currentVersion,
                document.ReleaseUrl,
                document.Notes,
                DateTimeOffset.UtcNow,
                null
            );
            statusCache.Update(status);
            return status;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A failed check must never take this background service down, and must never
            // silently discard the last genuinely known-good result -- only LastCheckedAtUtc and
            // LastCheckError change; a previously discovered LatestKnownVersion/ReleaseUrl/Notes
            // (and the UpdateAvailable notice built from them) stay visible to an operator through
            // a transient outage rather than flickering back to "nothing known" on every failed
            // tick.
            logger.LogWarning(
                exception,
                "Self-update check against '{ReleaseFeedUrl}' failed.",
                releaseFeedUrl
            );

            SelfUpdateStatus previous = statusCache.Current;
            SelfUpdateStatus status = previous with
            {
                Configured = true,
                CurrentVersion = currentVersion,
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
                LastCheckError = exception.Message,
            };
            statusCache.Update(status);
            return status;
        }
    }

    /// <summary>
    ///     This method runs <see cref="CheckNowAsync" /> only if checking is currently enabled
    ///     and (unless <paramref name="force" />) a full <see cref="SelfUpdateOptions.CheckInterval" />
    ///     has actually elapsed since the last recorded check.
    /// </summary>
    private async Task RunOneCheckIfDueAsync(bool force, CancellationToken stoppingToken)
    {
        if (!options.CurrentValue.EnableAutomaticCheck)
            return;

        if (!force)
        {
            DateTimeOffset? lastCheckedAtUtc = statusCache.Current.LastCheckedAtUtc;
            bool isDue =
                lastCheckedAtUtc is null
                || DateTimeOffset.UtcNow - lastCheckedAtUtc.Value
                    >= options.CurrentValue.CheckInterval;

            if (!isDue)
                return;
        }

        try
        {
            await CheckNowAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
    }
}
