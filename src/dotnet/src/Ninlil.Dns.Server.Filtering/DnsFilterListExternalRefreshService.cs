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
///     This class keeps every enabled <see cref="DnsFilterListSourceEnum.Subscribed" /> list
///     current. Once a minute, while filtering is enabled, it refetches each list whose
///     <see cref="DnsFilterListEntity.RefreshInterval" /> has elapsed since the last successful
///     fetch, parses it with <see cref="DnsFilterListParser" /> (hosts-file, bare-domain and
///     adblock syntax) and replaces the list's entries. A list with no
///     <see cref="DnsFilterListEntity.RefreshInterval" />, or never fetched, counts as due on
///     every tick. The replace raises the entry-change signal when it commits
///     (<see cref="Ninlil.Dns.Data.Abstractions.Repository.IEntityChangeNotifier" />), which
///     expires <see cref="DnsFilterConfigurationCache" />, so the next query uses the new entries.
/// </summary>
/// <param name="scopeFactory">The factory used to reach the scoped repositories, once per tick and once per refreshed list.</param>
/// <param name="httpClientFactory">The factory used to fetch a list's subscription URL.</param>
/// <param name="options">
///     The <c>Enable</c> switch shared with <see cref="DnsFilterEngine" />. It is checked on
///     every tick rather than once before the loop, so turning filtering on through
///     <c>FilteringSettingsController.Patch</c> resumes refreshes without a restart.
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
    ///     This field contains how often this service checks whether any subscribed list is due.
    ///     Each list's <see cref="DnsFilterListEntity.RefreshInterval" /> decides whether it is.
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
                    // A failed tick must not stop the service; the next tick retries.
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
    ///     This method finds every enabled, subscribed list that is due and refreshes each in
    ///     turn.
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
                // One list's failure (unreachable or malformed URL, an error status) must not stop
                // the others. Its LastFetchedAtUtc is not updated, so it is retried next tick.
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
    ///     This method fetches and parses one list's subscription URL, replaces its entries with
    ///     the parsed set and records the fetch time.
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

        // On the relational engines the replace changes the entries at once, so the replace,
        // the list update and the save run in one transaction: a failure rolls back and leaves
        // the old entries. MongoDB registers no factory; its staged replace runs at save time
        // (OD-B11-d).
        IUnitOfWorkTransactionFactory transactionFactory =
            scope.ServiceProvider.GetService<IUnitOfWorkTransactionFactory>();
        await using IUnitOfWorkTransaction transaction = transactionFactory is null
            ? null
            : await transactionFactory.BeginAsync(stoppingToken);

        await entryRepository.ReplaceEntriesAsync(list.Id, parsed.Entries, stoppingToken);

        DnsFilterListEntity tracked = await listRepository.GetByIdAsync(list.Id, stoppingToken);

        if (tracked is not null)
        {
            tracked.LastFetchedAtUtc = DateTimeOffset.UtcNow;
            listRepository.Update(tracked);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        if (transaction is not null)
            await transaction.CommitAsync(stoppingToken);

        // The skipped count matters: a list of mostly cosmetic or browser-only rules (such as
        // EasyList) would otherwise look fine while blocking far less than its size suggests.
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
