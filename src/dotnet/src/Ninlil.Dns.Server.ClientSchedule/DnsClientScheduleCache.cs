using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule;

/// <summary>
///     This class holds the data <see cref="DnsClientScheduleEngine" /> decides on, in memory, so
///     a DNS query reads no database rows while the copy is fresh: the enabled time-of-day
///     schedules of every client and every client group, and the instant-pause time of every group.
///     (A client's own pause and group membership come from <see cref="DnsClientDirectory" />.)
///     It is one <see cref="RefreshingSnapshot{T}" /> of a <see cref="DnsScheduleSet" />. A
///     schedule's time zone is resolved once per load, so a query does no time zone lookup.
///     <para>
///         <strong>Expiry.</strong> The cache subscribes to <see cref="IEntityChangeNotifier" /> and
///         expires the snapshot when a <see cref="DnsClientScheduleEntity" />,
///         <see cref="DnsClientGroupScheduleEntity" /> or <see cref="DnsClientGroupEntity" /> is
///         saved, so a schedule or a group pause set through the API applies to the very next query
///         in this process. A change made elsewhere (another cluster node, a direct database edit)
///         shows after <see cref="SnapshotTimeToLive" />. A failed reload keeps the last good copy
///         and is logged; if the first load fails the copy is empty (nothing is blocked) until the
///         next time-to-live expiry.
///     </para>
///     <para>
///         <strong>Memory.</strong> A schedule is a few fields; a thousand take well under a
///         megabyte.
///     </para>
///     <para>
///         <strong>Storage.</strong> When a read replica is configured, a reload that only follows
///         the age of the copy reads the schedules from the replica
///         (<see cref="SnapshotLoadReasonEnum.TimeToLive" />). The first load and every reload after a
///         local change read the primary, so a change saved here is never lost to a replica that
///         has not received it yet (see <see cref="SnapshotLoadScopeExtension" />).
///     </para>
/// </summary>
public sealed class DnsClientScheduleCache : IDisposable
{
    /// <summary>
    ///     This field is how long a copy is used before a background refresh starts. It also
    ///     bounds how long a change made outside this process takes to apply.
    /// </summary>
    public static readonly TimeSpan SnapshotTimeToLive = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     This field is the in-memory copy of the schedules and group pauses.
    /// </summary>
    private readonly RefreshingSnapshot<DnsScheduleSet> _snapshot;

    /// <summary>
    ///     This field is the change-notification subscription, removed in <see cref="Dispose" />.
    /// </summary>
    private readonly IDisposable _subscription;

    /// <summary>
    ///     This constructor builds the cache and subscribes it to schedule and group changes.
    /// </summary>
    /// <param name="scopeFactory">The factory used to reach the repositories, once per load.</param>
    /// <param name="notifier">The signal that tells this cache when schedules or groups were saved.</param>
    /// <param name="logger">The logger load failures and unusable schedules are recorded to.</param>
    /// <param name="timeProvider">The clock for the copy's age; the system clock when <see langword="null" />.</param>
    public DnsClientScheduleCache(
        IServiceScopeFactory scopeFactory,
        IEntityChangeNotifier notifier,
        ILogger<DnsClientScheduleCache> logger,
        TimeProvider timeProvider = null
    )
    {
        _snapshot = new RefreshingSnapshot<DnsScheduleSet>(
            (reason, token) => LoadAsync(scopeFactory, logger, reason, token),
            DnsScheduleSet.Empty,
            SnapshotTimeToLive,
            timeProvider ?? TimeProvider.System,
            logger,
            "client schedules"
        );

        // The callback only marks the copy out of date; the next read reloads it.
        _subscription = notifier.Subscribe(
            [
                typeof(DnsClientScheduleEntity),
                typeof(DnsClientGroupScheduleEntity),
                typeof(DnsClientGroupEntity),
            ],
            _ => _snapshot.Expire()
        );
    }

    /// <summary>
    ///     This method returns the current schedules. The first call waits for the first load.
    /// </summary>
    /// <param name="stoppingToken">The token used to stop waiting for a load.</param>
    public ValueTask<DnsScheduleSet> GetAsync(CancellationToken stoppingToken = default) =>
        _snapshot.GetAsync(stoppingToken);

    /// <summary>
    ///     This method marks the copy out of date; the next read waits for a reload. Code that
    ///     changes schedules without a repository save calls it.
    /// </summary>
    public void Expire() => _snapshot.Expire();

    /// <summary>
    ///     This method removes the change-notification subscription.
    /// </summary>
    public void Dispose() => _subscription.Dispose();

    /// <summary>
    ///     This method reads the schedules and group pauses in a short-lived scope and builds the
    ///     set. A repository that is not registered contributes nothing.
    /// </summary>
    private static async Task<DnsScheduleSet> LoadAsync(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        SnapshotLoadReasonEnum reason,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateLoadScope(reason);
        IServiceProvider services = scope.ServiceProvider;

        IReadOnlyList<DnsClientScheduleEntity> clientRows =
            services.GetService<IDnsClientScheduleRepository>() is { } clientRepository
                ? await clientRepository.ListAsync(null, stoppingToken)
                : [];
        IReadOnlyList<DnsClientGroupScheduleEntity> groupRows =
            services.GetService<IDnsClientGroupScheduleRepository>() is { } groupRepository
                ? await groupRepository.ListAsync(null, stoppingToken)
                : [];
        IReadOnlyList<DnsClientGroupEntity> groups =
            services.GetService<IDnsClientGroupRepository>() is { } pauseRepository
                ? await pauseRepository.ListAsync(null, stoppingToken)
                : [];

        Dictionary<Guid, DnsScheduleWindow[]> clientSchedules = clientRows
            .Where(row => !row.IsDisabled)
            .Select(row =>
                (
                    row.ClientId,
                    Window: ToWindow(
                        logger,
                        row.DaysOfWeek,
                        row.StartTime,
                        row.EndTime,
                        row.TimeZoneId
                    )
                )
            )
            .Where(item => item.Window is not null)
            .GroupBy(item => item.ClientId)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.Select(item => item.Window).ToArray()
            );

        Dictionary<Guid, DnsScheduleWindow[]> groupSchedules = groupRows
            .Where(row => !row.IsDisabled)
            .Select(row =>
                (
                    row.ClientGroupId,
                    Window: ToWindow(
                        logger,
                        row.DaysOfWeek,
                        row.StartTime,
                        row.EndTime,
                        row.TimeZoneId
                    )
                )
            )
            .Where(item => item.Window is not null)
            .GroupBy(item => item.ClientGroupId)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping.Select(item => item.Window).ToArray()
            );

        Dictionary<Guid, DateTimeOffset> groupPauses = groups
            .Where(group => group.PausedUntilUtc is not null)
            .ToDictionary(group => group.Id, group => group.PausedUntilUtc.Value);

        return new DnsScheduleSet(clientSchedules, groupSchedules, groupPauses);
    }

    /// <summary>
    ///     This method builds one window. The schedule controllers reject unknown time zone ids on
    ///     write, so a failure here means the host's time zone database no longer knows the id. The
    ///     schedule is then dropped (never active) rather than failing the load, and a warning is
    ///     logged on every load that hits it.
    /// </summary>
    private static DnsScheduleWindow ToWindow(
        ILogger logger,
        DnsScheduleDayOfWeekEnum daysOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        string timeZoneId
    )
    {
        try
        {
            return new DnsScheduleWindow(
                daysOfWeek,
                startTime,
                endTime,
                TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)
            );
        }
        catch (Exception exception)
            when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                exception,
                "A client schedule's own time zone id '{TimeZoneId}' could not be resolved on this host; treating its schedule as never active.",
                timeZoneId
            );
            return null;
        }
    }
}

/// <summary>
///     This class is one immutable copy of the schedule data: the enabled schedules whose time zone
///     resolves, by client and by group, and the groups that have an instant pause.
/// </summary>
/// <param name="ClientSchedules">The enabled schedules assigned directly to a client, by client id.</param>
/// <param name="GroupSchedules">The enabled schedules assigned to a group, by group id.</param>
/// <param name="GroupPauses">The end of each group's instant pause, by group id; groups without one are absent.</param>
public sealed record DnsScheduleSet(
    IReadOnlyDictionary<Guid, DnsScheduleWindow[]> ClientSchedules,
    IReadOnlyDictionary<Guid, DnsScheduleWindow[]> GroupSchedules,
    IReadOnlyDictionary<Guid, DateTimeOffset> GroupPauses
)
{
    /// <summary>
    ///     This field is the set used before the first load succeeds: nothing is scheduled or paused.
    /// </summary>
    public static readonly DnsScheduleSet Empty = new(
        new Dictionary<Guid, DnsScheduleWindow[]>(),
        new Dictionary<Guid, DnsScheduleWindow[]>(),
        new Dictionary<Guid, DateTimeOffset>()
    );
}

/// <summary>
///     This record is one enabled blocking window with its time zone resolved.
/// </summary>
/// <param name="DaysOfWeek">The days the window applies to.</param>
/// <param name="StartTime">The local start time.</param>
/// <param name="EndTime">The local end time; a window may cross midnight.</param>
/// <param name="TimeZone">The time zone the times are local to.</param>
public sealed record DnsScheduleWindow(
    DnsScheduleDayOfWeekEnum DaysOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    TimeZoneInfo TimeZone
);
