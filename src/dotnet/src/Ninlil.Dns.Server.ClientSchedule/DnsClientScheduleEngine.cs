using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientSchedule;

/// <summary>
///     This class implements <see cref="IDnsClientScheduleEngine" />, the parental-control check
///     that <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> runs for
///     each query (see that class). It resolves the query's client, then checks in order: the
///     client's instant pause, its group's instant pause, the client's time-of-day schedules and
///     its group's schedules. Any one that is active blocks the query.
///     <para>
///         <strong>Pause and schedules share one engine</strong> because both answer the same
///         question ("is this client or its group in a blocking period now?") for the same
///         resolved client. A pause is checked first: it overrides every schedule, filter list and
///         rewrite, so there is no need to look at schedules once a pause applies.
///     </para>
///     <para>
///         <strong>Client resolution</strong> works as in
///         <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>: the source address is resolved
///         with <see cref="IClientIdentityResolver" /> and looked up in
///         <see cref="IDnsClientRepository" />. A disabled client resolves to nothing, so neither
///         its schedules nor its group's apply. The group's <c>IsDisabled</c> flag is not checked,
///         since it only affects filter lists. The few lines of resolution are duplicated here
///         rather than shared, to keep this engine independent of the filter engine's hot path.
///     </para>
///     <para>
///         <strong>Dependencies are optional.</strong> The resolver is a constructor parameter
///         that defaults to <see langword="null" />; the repositories are scoped, so they are
///         resolved per check through <c>IServiceScopeFactory</c> (the engine is a singleton). With
///         no resolver or no client repository nothing is blocked; a missing group or schedule
///         repository skips only that check.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to open a scope for the repositories on each check.</param>
/// <param name="logger">The logger used to report a schedule time zone this host cannot resolve.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver that turns a source address into a persistent client identifier.
///     With <see langword="null" /> (the default) the engine never blocks anything.
/// </param>
public sealed class DnsClientScheduleEngine(
    IServiceScopeFactory scopeFactory,
    ILogger<DnsClientScheduleEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsClientScheduleEngine
{
    /// <inheritdoc />
    public async ValueTask<DnsClientScheduleDecision> EvaluateAsync(
        IPAddress remoteAddress,
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    )
    {
        if (clientIdentityResolver is null)
            return DnsClientScheduleDecision.Allowed;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DnsClientEntity client = await ResolveClientAsync(scope, remoteAddress, stoppingToken);

        if (client is null)
            return DnsClientScheduleDecision.Allowed;

        // Pauses come before schedules (see the class summary): the client's pause first, then
        // its group's.
        if (IsCurrentlyPaused(client.PausedUntilUtc, nowUtc))
            return DnsClientScheduleDecision.Blocked(
                $"client '{client.Id}' is instantly paused until {client.PausedUntilUtc:O}"
            );

        if (client.ClientGroupId is { } pausedGroupCandidateId)
        {
            IDnsClientGroupRepository groupRepository =
                scope.ServiceProvider.GetService<IDnsClientGroupRepository>();

            DnsClientGroupEntity pauseCandidateGroup = groupRepository is null
                ? null
                : await groupRepository.GetByIdAsync(pausedGroupCandidateId, stoppingToken);

            if (
                pauseCandidateGroup is not null
                && IsCurrentlyPaused(pauseCandidateGroup.PausedUntilUtc, nowUtc)
            )
                return DnsClientScheduleDecision.Blocked(
                    $"client group '{pauseCandidateGroup.Id}' is instantly paused until {pauseCandidateGroup.PausedUntilUtc:O}"
                );
        }

        IDnsClientScheduleRepository clientScheduleRepository =
            scope.ServiceProvider.GetService<IDnsClientScheduleRepository>();

        if (clientScheduleRepository is not null)
        {
            IReadOnlyList<DnsClientScheduleEntity> clientSchedules =
                await clientScheduleRepository.ListByClientIdAsync(client.Id, stoppingToken);

            foreach (DnsClientScheduleEntity schedule in clientSchedules)
            {
                if (IsActive(schedule.IsDisabled, schedule, nowUtc))
                    return DnsClientScheduleDecision.Blocked(
                        $"an active schedule assigned directly to client '{client.Id}'"
                    );
            }
        }

        if (client.ClientGroupId is not { } clientGroupId)
            return DnsClientScheduleDecision.Allowed;

        IDnsClientGroupScheduleRepository groupScheduleRepository =
            scope.ServiceProvider.GetService<IDnsClientGroupScheduleRepository>();

        if (groupScheduleRepository is null)
            return DnsClientScheduleDecision.Allowed;

        IReadOnlyList<DnsClientGroupScheduleEntity> groupSchedules =
            await groupScheduleRepository.ListByClientGroupIdAsync(clientGroupId, stoppingToken);

        foreach (DnsClientGroupScheduleEntity schedule in groupSchedules)
        {
            if (IsActive(schedule.IsDisabled, schedule, nowUtc))
                return DnsClientScheduleDecision.Blocked(
                    $"an active schedule assigned to client group '{clientGroupId}'"
                );
        }

        return DnsClientScheduleDecision.Allowed;
    }

    /// <summary>
    ///     This method checks whether a pause timestamp is in effect, for both the client and the
    ///     group check in <see cref="EvaluateAsync" />. A <see langword="null" /> or past value means
    ///     not paused. Unlike a schedule window, a pause is stored and compared in UTC, so no time
    ///     zone is involved.
    /// </summary>
    /// <param name="pausedUntilUtc">The stored pause timestamp, in UTC, or <see langword="null" />.</param>
    /// <param name="nowUtc">The current instant, in UTC, to compare against.</param>
    private static bool IsCurrentlyPaused(DateTimeOffset? pausedUntilUtc, DateTimeOffset nowUtc) =>
        pausedUntilUtc is { } until && until > nowUtc;

    /// <summary>
    ///     This method checks whether one schedule is enabled and its window covers the current
    ///     instant. There is one overload per schedule entity type; both use
    ///     <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" /> for the window and
    ///     <see cref="TryResolveTimeZone" />, so a time zone that cannot be resolved never
    ///     blocks.
    /// </summary>
    private bool IsActive(
        bool isDisabled,
        DnsClientScheduleEntity schedule,
        DateTimeOffset nowUtc
    ) =>
        !isDisabled
        && TryResolveTimeZone(schedule.TimeZoneId, out TimeZoneInfo timeZone)
        && DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            schedule.DaysOfWeek,
            schedule.StartTime,
            schedule.EndTime,
            timeZone,
            nowUtc
        );

    /// <inheritdoc cref="IsActive(bool, DnsClientScheduleEntity, DateTimeOffset)" />
    private bool IsActive(
        bool isDisabled,
        DnsClientGroupScheduleEntity schedule,
        DateTimeOffset nowUtc
    ) =>
        !isDisabled
        && TryResolveTimeZone(schedule.TimeZoneId, out TimeZoneInfo timeZone)
        && DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            schedule.DaysOfWeek,
            schedule.StartTime,
            schedule.EndTime,
            timeZone,
            nowUtc
        );

    /// <summary>
    ///     This method resolves a stored time zone id without throwing. The schedule controllers
    ///     reject unknown ids on write, so a failure here means the host's time zone database no
    ///     longer knows the id. The schedule is then treated as never active rather than failing
    ///     the query, and a warning is logged on every check that hits it.
    /// </summary>
    private bool TryResolveTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (Exception exception)
            when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning(
                exception,
                "A client schedule's own time zone id '{TimeZoneId}' could not be resolved on this host; treating its schedule as never active.",
                timeZoneId
            );
            timeZone = null;
            return false;
        }
    }

    /// <summary>
    ///     This method resolves a query's source address to its enabled
    ///     <see cref="DnsClientEntity" />, or <see langword="null" />. It matches the helper in
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c> (see the class summary for why it is
    ///     not shared).
    /// </summary>
    private async Task<DnsClientEntity> ResolveClientAsync(
        AsyncServiceScope scope,
        IPAddress remoteAddress,
        CancellationToken stoppingToken
    )
    {
        ClientIdentityResolution resolution = await clientIdentityResolver.ResolveAsync(
            remoteAddress,
            stoppingToken
        );

        if (resolution is null)
            return null;

        IDnsClientRepository clientRepository =
            scope.ServiceProvider.GetService<IDnsClientRepository>();

        if (clientRepository is null)
            return null;

        DnsClientEntity client = await clientRepository.GetByClientIdentifierAsync(
            resolution.ClientIdentifier,
            resolution.ProtocolVersion,
            stoppingToken
        );

        return client is { IsDisabled: false } ? client : null;
    }
}
