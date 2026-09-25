using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.SelfUpdate;

/// <summary>
///     This class periodically checks <see cref="SelfUpdateOptions.ReleaseFeedUrl" /> for a
///     newer release than the one running and records the result in
///     <see cref="ISelfUpdateStatusCache" />. It never applies anything (see
///     <see cref="SelfUpdateOptions" /> for why).
///     <para>
///         <b>A check runs at startup</b>, if enabled, so an operator who just restarted onto a
///         new build sees an accurate notice at once instead of after up to a day. After that the
///         service wakes every <see cref="TickInterval" /> and reads
///         <see cref="SelfUpdateOptions.EnableAutomaticCheck" /> and
///         <see cref="SelfUpdateOptions.CheckInterval" /> again each time, like
///         <c>DnsFilterListExternalRefreshService</c>, so a change saved through
///         <c>Controllers.SelfUpdateSettingsController.Patch</c> (turning checking on, or
///         shortening the interval) applies on the next tick without a restart.
///     </para>
/// </summary>
/// <param name="httpClientFactory">The factory this service fetches the release feed document through.</param>
/// <param name="options">The self-update settings, re-read on every tick.</param>
/// <param name="statusCache">The shared cache this service records each check's outcome in.</param>
/// <param name="currentVersionProvider">The provider this service asks for the running version.</param>
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
    ///     This field contains how often the service wakes to see whether a check is due under
    ///     the current <see cref="SelfUpdateOptions.CheckInterval" />. It is much shorter than the
    ///     24-hour default, so a shortened interval takes effect promptly instead of after the
    ///     previous interval has run out.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This constant contains the name of the <see cref="HttpClient" /> this service gets from
    ///     <see cref="IHttpClientFactory" />.
    /// </summary>
    public const string HttpClientName = "Ninlil.Dns.Server.SelfUpdate";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check once at startup, before the first tick (see the class summary).
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
    ///     This method runs a check now, whether or not automatic checking is enabled. The
    ///     periodic loop and the manual check in <c>SelfUpdateController</c> both call it, so both
    ///     record their result in <see cref="ISelfUpdateStatusCache" /> the same way. A failure is
    ///     recorded in the returned status, not thrown; only cancellation propagates.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The status this check recorded.</returns>
    public async Task<SelfUpdateStatus> CheckNowAsync(CancellationToken stoppingToken = default)
    {
        Version currentVersion = currentVersionProvider.GetCurrentVersion();
        string releaseFeedUrl = options.CurrentValue.ReleaseFeedUrl;

        if (string.IsNullOrWhiteSpace(releaseFeedUrl))
        {
            // No feed configured: record an "unconfigured" status, not a failure (see
            // SelfUpdateOptions.ReleaseFeedUrl).
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
            // A failed check must not stop the service or discard the last good result. Only
            // LastCheckedAtUtc, LastCheckError and the current version change; the previous
            // LatestKnownVersion, ReleaseUrl, notes and UpdateAvailable notice stay visible
            // through a temporary outage.
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
    ///     This method runs <see cref="CheckNowAsync" /> if checking is enabled and, unless
    ///     <paramref name="force" /> is set, a full <see cref="SelfUpdateOptions.CheckInterval" />
    ///     has passed since the last recorded check, successful or not.
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
