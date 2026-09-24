using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class keeps every <see cref="DnsFilterListSourceEnum.Subscribed" /> list's own
///     entries current by periodically refetching its <see cref="DnsFilterListEntity.SubscriptionUrl" />
///     -- parsed by <see cref="DnsFilterListParser" /> (hosts-file, bare-domain and adblock
///     syntax alike) -- the refresh analogue of <c>CloudDnsZoneSyncService</c>'s own
///     REST-polling refresh loop, per-item due-checked against that list's own
///     <see cref="DnsFilterListEntity.RefreshInterval" /> rather than one fixed interval for
///     every list.
/// </summary>
/// <param name="scopeFactory">The factory used to reach every scoped repository this service needs, once per tick.</param>
/// <param name="httpClientFactory">The factory used to fetch a list's own subscription URL.</param>
/// <param name="options">
///     This service's own <c>Enable</c> gate (shared with <see cref="DnsFilterEngine" />), read
///     live via <see cref="IOptionsMonitor{TOptions}" /> -- checked fresh on every tick, not just
///     once before this service's own loop starts, so a live <c>FilteringSettingsController.Patch</c>
///     write turning filtering on genuinely resumes refreshing subscribed lists with no restart
///     (the same per-iteration-check fix <c>TrustAnchorRolloverService.ExecuteAsync</c> already
///     established for the identical "checked once before a loop that only ever runs once per
///     process" shape).
/// </param>
/// <param name="logger">The logger this service records per-list fetch failures to.</param>
public sealed class DnsFilterListExternalRefreshService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterListExternalRefreshService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service wakes up to check whether any
    ///     subscribed list is actually due for a refresh -- independent of, and shorter than,
    ///     any single list's own <see cref="DnsFilterListEntity.RefreshInterval" />.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enable)
            {
                try
                {
                    await RefreshDueListsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // One failed tick must never take this whole background service down --
                    // there is always another tick TickInterval later.
                    logger.LogWarning(
                        exception,
                        "A block/allow list refresh tick failed unexpectedly."
                    );
                }
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method finds every enabled, subscribed list that is actually due, and refreshes
    ///     each in turn.
    /// </summary>
    private async Task RefreshDueListsAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsFilterListRepository listRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsFilterListEntity> lists = await listRepository.ListAsync(
            list => !list.IsDisabled && list.Source == DnsFilterListSourceEnum.Subscribed,
            stoppingToken
        );

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DnsFilterListEntity list in lists)
        {
            bool isDue =
                list.LastFetchedAtUtc is null
                || list.RefreshInterval is null
                || now - list.LastFetchedAtUtc.Value >= list.RefreshInterval.Value;

            if (!isDue)
                continue;

            try
            {
                await RefreshListAsync(list, stoppingToken);
            }
            catch (Exception exception)
            {
                // A single list's own fetch failure (the URL is unreachable, malformed, or
                // returns an error) must never block every other list's own refresh.
                logger.LogWarning(
                    exception,
                    "Failed to refresh block/allow list '{ListName}' from '{Url}'.",
                    list.Name,
                    list.SubscriptionUrl
                );
            }
        }
    }

    /// <summary>
    ///     This method fetches and parses a single list's own subscription URL, and replaces its
    ///     entries wholesale with the freshly parsed set.
    /// </summary>
    private async Task RefreshListAsync(DnsFilterListEntity list, CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(list.SubscriptionUrl))
            return;

        HttpClient client = httpClientFactory.CreateClient(
            nameof(DnsFilterListExternalRefreshService)
        );
        string content = await client.GetStringAsync(list.SubscriptionUrl, stoppingToken);
        DnsFilterListParseResult parsed = DnsFilterListParser.Parse(
            content,
            list.Kind,
            list.Action
        );

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsFilterListEntryRepository entryRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        IDnsFilterListRepository listRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await entryRepository.ReplaceEntriesAsync(list.Id, parsed.Entries, stoppingToken);

        DnsFilterListEntity tracked = await listRepository.GetByIdAsync(list.Id, stoppingToken);

        if (tracked is not null)
        {
            tracked.LastFetchedAtUtc = DateTimeOffset.UtcNow;
            listRepository.Update(tracked);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        // The skipped count is worth surfacing: a list whose rules are mostly cosmetic or
        // browser-only (an EasyList meant for a browser extension, say) otherwise looks like it
        // refreshed fine while blocking far less than its line count suggests.
        logger.LogInformation(
            "Refreshed block/allow list '{ListName}' ({Count} entries, {ExceptionCount} of them exceptions; {SkippedCount} unsupported rules skipped) from '{Url}'.",
            list.Name,
            parsed.Entries.Count,
            parsed.ExceptionCount,
            parsed.SkippedRuleCount,
            list.SubscriptionUrl
        );
    }
}
