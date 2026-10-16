using System.Text;
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
///     current. Once a minute, while filtering is enabled, it refetches each list that is due,
///     parses it with <see cref="DnsFilterListParser" /> (hosts-file, bare-domain and adblock
///     syntax) and replaces the list's entries.
///     A list is due when it was never fetched, or when its interval has elapsed since the last
///     successful fetch. A list without a <see cref="DnsFilterListEntity.RefreshInterval" /> uses
///     <see cref="DefaultRefreshInterval" />, and a stored interval below
///     <see cref="MinimumRefreshInterval" /> counts as that minimum, so a typo or an old row cannot
///     make a large list download every minute. The service logs the raise once per list.
///     A failing list backs off in memory: after the n-th failure in a row the next attempt waits
///     <c>min(2^(n-1) minutes, 1 hour)</c>, which never exceeds the interval because the minimum
///     is one hour. The first failure of a streak is a warning, later ones are debug entries, and
///     the first success after failures is an information entry. A restart forgets the streaks.
///     A download is limited to <see cref="MaxDownloadOctets" />. A larger <c>Content-Length</c>
///     is refused before the body is read, and a body without a length is abandoned once it passes
///     the limit. Any failure leaves the list's entries and fetch time unchanged.
///     The replace raises the entry-change signal when it commits
///     (<see cref="Ninlil.Dns.Data.Abstractions.Repository.IEntityChangeNotifier" />), which
///     would expire every list's index in <see cref="DnsFilterConfigurationCache" />. The service
///     silences that signal for its own replace and, after the commit, expires only the refreshed
///     list (<see cref="DnsFilterConfigurationCache.ExpireList" />), so the next query rebuilds one
///     index and the other lists stay loaded.
/// </summary>
/// <param name="scopeFactory">The factory used to reach the scoped repositories, once per tick and once per refreshed list.</param>
/// <param name="httpClientFactory">The factory used to fetch a list's subscription URL.</param>
/// <param name="options">
///     The <c>Enable</c> switch shared with <see cref="DnsFilterEngine" />. It is checked on
///     every tick rather than once before the loop, so turning filtering on through
///     <c>FilteringSettingsController.Patch</c> resumes refreshes without a restart.
/// </param>
/// <param name="logger">The logger this service records per-list fetch results to.</param>
/// <param name="cache">
///     The cache whose list index is expired after a refresh. With <see langword="null" /> (the
///     default) the generic entry signal is left on and expires every list.
/// </param>
/// <param name="timeProvider">
///     The clock for due times, backoff and the one-minute wait. With <see langword="null" /> (the
///     default) it is <see cref="TimeProvider.System" />; tests pass a manual clock.
/// </param>
public sealed class DnsFilterListExternalRefreshService(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterListExternalRefreshService> logger,
    DnsFilterConfigurationCache cache = null,
    TimeProvider timeProvider = null
) : BackgroundService
{
    /// <summary>
    ///     This field is how often a subscribed list is fetched when it has no refresh interval of
    ///     its own. It matches the interval the presets and blocked services use.
    /// </summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This field is the shortest refresh interval a subscribed list uses. List publishers ask
    ///     for infrequent fetches, and a shorter interval is nearly always a typo. The REST API
    ///     refuses a smaller value, and this service raises one that is already stored.
    /// </summary>
    public static readonly TimeSpan MinimumRefreshInterval = TimeSpan.FromHours(1);

    /// <summary>
    ///     This constant is the largest list download this service accepts, in octets. It stops a
    ///     hostile or broken URL from filling memory once per refresh.
    /// </summary>
    public const long MaxDownloadOctets = 128L * 1024 * 1024;

    /// <summary>
    ///     This field is the longest wait between two attempts on a failing list.
    /// </summary>
    private static readonly TimeSpan MaximumBackoff = TimeSpan.FromHours(1);

    /// <summary>
    ///     This field contains how often this service checks whether any subscribed list is due.
    ///     Each list's <see cref="DnsFilterListEntity.RefreshInterval" /> decides whether it is.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     This field is the clock; <see cref="TimeProvider.System" /> unless a test supplied one.
    /// </summary>
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This field holds the failure streak of each list that is failing, by list id. Only the
    ///     tick loop touches it (one tick at a time), so it needs no lock.
    /// </summary>
    private readonly Dictionary<Guid, FailureStreak> failures = [];

    /// <summary>
    ///     This field holds the ids of the lists whose too-small interval was already logged, so
    ///     the information entry appears once per list and not once per tick.
    /// </summary>
    private readonly HashSet<Guid> raisedIntervalLogged = [];

    /// <summary>
    ///     This record is the backoff state of one failing list: how many attempts failed in a row
    ///     and the earliest time of the next attempt.
    /// </summary>
    private sealed record FailureStreak(int Count, DateTimeOffset NextAttemptUtc);

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
                await Task.Delay(TickInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method finds every enabled, subscribed list that is due and not backing off, and
    ///     refreshes each in turn. It is one tick of the loop and the seam the schedule tests
    ///     call with a manual clock.
    /// </summary>
    internal async Task RefreshDueListsAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsFilterListRepository listRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsFilterListEntity> lists = await listRepository.ListAsync(
            list => !list.IsDisabled && list.Source == DnsFilterListSourceEnum.Subscribed,
            stoppingToken
        );

        DateTimeOffset now = clock.GetUtcNow();

        // A deleted or disabled list must not leave a streak behind that a new list could not
        // reuse (ids are unique) but that would sit in memory forever.
        HashSet<Guid> presentIds = [.. lists.Select(list => list.Id)];
        foreach (Guid id in failures.Keys.Where(id => !presentIds.Contains(id)).ToList())
            failures.Remove(id);

        foreach (DnsFilterListEntity list in lists)
        {
            if (
                failures.TryGetValue(list.Id, out FailureStreak streak)
                && now < streak.NextAttemptUtc
            )
                continue;

            TimeSpan interval = EffectiveInterval(list);
            bool isDue =
                list.LastFetchedAtUtc is null || now - list.LastFetchedAtUtc.Value >= interval;

            if (!isDue)
                continue;

            try
            {
                if (await RefreshListAsync(list, stoppingToken))
                    RecordSuccess(list);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // One list's failure (unreachable or malformed URL, an error status, an oversized
                // body) must not stop the others. Its LastFetchedAtUtc is not updated and its
                // entries stay; the backoff decides when it is tried again.
                RecordFailure(list, exception, now);
            }
        }
    }

    /// <summary>
    ///     This method returns the interval a list is refreshed at: the default when it has none,
    ///     and never less than <see cref="MinimumRefreshInterval" />.
    /// </summary>
    private TimeSpan EffectiveInterval(DnsFilterListEntity list)
    {
        if (list.RefreshInterval is null)
            return DefaultRefreshInterval;

        if (list.RefreshInterval.Value >= MinimumRefreshInterval)
            return list.RefreshInterval.Value;

        if (raisedIntervalLogged.Add(list.Id))
            logger.LogInformation(
                "Block/allow list '{ListName}' has a refresh interval of {Interval}, which is below the minimum; it is refreshed every {Minimum} instead.",
                list.Name,
                list.RefreshInterval.Value,
                MinimumRefreshInterval
            );

        return MinimumRefreshInterval;
    }

    /// <summary>
    ///     This method ends a failure streak and reports the recovery when there was one.
    /// </summary>
    private void RecordSuccess(DnsFilterListEntity list)
    {
        if (!failures.Remove(list.Id, out FailureStreak streak))
            return;

        logger.LogInformation(
            "Block/allow list '{ListName}' refreshed again after {FailureCount} failed attempts.",
            list.Name,
            streak.Count
        );
    }

    /// <summary>
    ///     This method extends a list's failure streak and schedules its next attempt. The wait is
    ///     2^(n-1) minutes after the n-th failure in a row (1, 2, 4, ... minutes), capped at one
    ///     hour. The cap is also below the shortest interval, so the backoff never delays a list
    ///     past the time it would be due anyway.
    /// </summary>
    private void RecordFailure(DnsFilterListEntity list, Exception exception, DateTimeOffset now)
    {
        int count = failures.TryGetValue(list.Id, out FailureStreak previous)
            ? previous.Count + 1
            : 1;

        // Past 2^6 minutes the cap applies anyway; the guard keeps the shift from overflowing.
        TimeSpan wait = count > 7 ? MaximumBackoff : TimeSpan.FromMinutes(1L << (count - 1));
        if (wait > MaximumBackoff)
            wait = MaximumBackoff;

        failures[list.Id] = new FailureStreak(count, now + wait);

        if (count == 1)
            logger.LogWarning(
                exception,
                "Failed to refresh block/allow list '{ListName}' from '{Url}'; it is retried with a growing delay.",
                list.Name,
                list.SubscriptionUrl
            );
        else
            logger.LogDebug(
                exception,
                "Block/allow list '{ListName}' failed again ({FailureCount} in a row); the next attempt is in {Wait}.",
                list.Name,
                count,
                wait
            );
    }

    /// <summary>
    ///     This method downloads one list's body, refusing one over <see cref="MaxDownloadOctets" />.
    ///     It asks for the headers first, so a declared length over the limit is refused without
    ///     reading the body, and it counts the octets of a body that has no declared length.
    /// </summary>
    /// <exception cref="InvalidDataException">The body is larger than the limit.</exception>
    private async Task<string> DownloadAsync(string url, CancellationToken stoppingToken)
    {
        HttpClient client = httpClientFactory.CreateClient(
            nameof(DnsFilterListExternalRefreshService)
        );
        using HttpResponseMessage response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            stoppingToken
        );
        response.EnsureSuccessStatusCode();

        long? declaredLength = response.Content.Headers.ContentLength;
        if (declaredLength > MaxDownloadOctets)
            throw new InvalidDataException(
                $"The list is {declaredLength} octets, over the limit of {MaxDownloadOctets}."
            );

        await using Stream body = await response.Content.ReadAsStreamAsync(stoppingToken);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;

        while ((read = await body.ReadAsync(chunk, stoppingToken)) > 0)
        {
            // A server can omit or understate Content-Length, so the running total is the check
            // that holds.
            if (buffer.Length + read > MaxDownloadOctets)
                throw new InvalidDataException(
                    $"The list is larger than the limit of {MaxDownloadOctets} octets."
                );

            buffer.Write(chunk, 0, read);
        }

        return DecodeBody(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), response);
    }

    /// <summary>
    ///     This method decodes a body with the charset its response names, UTF-8 when it names
    ///     none or an unknown one.
    /// </summary>
    private static string DecodeBody(ReadOnlySpan<byte> body, HttpResponseMessage response)
    {
        Encoding encoding = Encoding.UTF8;
        string charset = response.Content.Headers.ContentType?.CharSet;

        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // An unknown charset name: UTF-8 is what list publishers use.
            }
        }

        return encoding.GetString(body);
    }

    /// <summary>
    ///     This method fetches and parses one list's subscription URL, replaces its entries with
    ///     the parsed set and records the fetch time. It returns <see langword="true" /> when the
    ///     entries were replaced, and <see langword="false" /> when there was nothing to fetch or
    ///     the list was deleted during the fetch (neither is a failure).
    /// </summary>
    private async Task<bool> RefreshListAsync(
        DnsFilterListEntity list,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(list.SubscriptionUrl))
            return false;

        string content = await DownloadAsync(list.SubscriptionUrl, stoppingToken);
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

        // The entry signal carries no list id and would reload every list's index. This refresh
        // changes one list, so the signal is silenced and that list is expired by id after the
        // commit instead. The scope must enclose the commit, where the relational engines raise
        // the signal.
        using IDisposable quiet = cache is null
            ? null
            : EntityChangeSuppression.Begin(typeof(DnsFilterListEntryEntity));

        await entryRepository.ReplaceEntriesAsync(list.Id, parsed.Entries, stoppingToken);

        DnsFilterListEntity tracked = await listRepository.GetByIdAsync(list.Id, stoppingToken);

        if (tracked is null)
        {
            // The list was deleted while the fetch ran. Returning without a save or a commit
            // discards the replace (the transaction rolls back on the relational engines, the
            // staged operations die with the scope on MongoDB), so no entries are left for an id
            // that no longer exists.
            logger.LogInformation(
                "Block/allow list '{ListName}' was deleted during its refresh; the fetched entries are discarded.",
                list.Name
            );
            return false;
        }

        tracked.LastFetchedAtUtc = clock.GetUtcNow();
        listRepository.Update(tracked);

        await unitOfWork.SaveChangesAsync(stoppingToken);

        if (transaction is not null)
            await transaction.CommitAsync(stoppingToken);

        cache?.ExpireList(list.Id);

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

        return true;
    }
}
