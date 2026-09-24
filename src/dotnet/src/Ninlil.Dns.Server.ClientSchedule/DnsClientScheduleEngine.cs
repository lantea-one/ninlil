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
///     This class implements <see cref="IDnsClientScheduleEngine" />: resolves a query's own
///     client identity, then checks -- in order -- that client's own instant pause, its resolved
///     client group's own instant pause, that client's own direct time-of-day schedule, and its
///     resolved client group's own schedule; any one of the four currently active blocks the
///     query.
///     <para>
///         <strong>Instant pause and time-of-day scheduling share this one engine</strong> rather
///         than each getting its own: both answer the identical question -- "is this client (or
///         its group) currently in a temporary, fully-blocking state?" -- against the identical
///         resolved client, so splitting them would only duplicate the resolution logic below for
///         no real separation of concerns. Pause is checked first, strictly ahead of every
///         schedule window, since it is this whole system's most decisive override: an active
///         pause blocks a query regardless of what any schedule, filter list, or rewrite would
///         otherwise decide, so there is no reason to ever evaluate a schedule window once a pause
///         is already known to apply.
///     </para>
///     <para>
///         <strong>Client resolution</strong> mirrors
///         <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own resolution exactly: a
///         query's source address is resolved via <see cref="IClientIdentityResolver" /> and
///         looked up in <see cref="IDnsClientRepository" />; a disabled client resolves to
///         nothing at all (contributes neither its own schedule nor its group's). This is a
///         second, independent implementation of that same handful of lines rather than a shared
///         extraction out of the already-shipped, already-tested <c>DnsFilterEngine</c> -- a
///         deliberate choice to avoid risking a regression in that engine's own hot path just to
///         save a small amount of duplicated resolution logic.
///     </para>
///     <para>
///         <strong>Both <see cref="IClientIdentityResolver" /> and the two repositories below are
///         optional</strong>: the resolver is a constructor-injected, <see langword="null" />-
///         defaulted parameter (this engine is registered as a DI singleton, so a
///         constructor-injected <em>scoped</em> repository would be an invalid captive dependency);
///         the two client-repository
///         registrations are instead resolved per-check via <c>IServiceScopeFactory</c>, mirroring
///         <c>DnsFilterEngine</c>'s own identical reasoning. Absent any of the three, this whole
///         engine simply never blocks anything -- see <see cref="EvaluateAsync" />.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach every scoped repository this engine needs, once per check.</param>
/// <param name="logger">The logger this engine records nothing routine to today, kept for parity with every other engine's own constructor shape and future diagnostic use.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver used to derive a query's own persistent client identifier from its
///     source address. <see langword="null" /> (the default) means this engine never blocks
///     anything -- the identical "cross-cutting infrastructure, singleton-safe, every pre-existing
///     caller keeps compiling unchanged" shape <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />'s
///     own optional dependencies already establish.
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

        // An instant pause is checked before anything else this engine evaluates -- see this
        // class's own doc comment for why it always takes priority over a schedule window. A
        // client's own pause is checked first; its resolved group's own pause (if any) is
        // checked next, independently of whether the client itself is paused.
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
    ///     This method checks whether an instant-pause timestamp is currently in effect -- shared
    ///     by both the client-scoped and group-scoped checks in <see cref="EvaluateAsync" />. A
    ///     <see langword="null" /> value, or one that has already passed, means "not paused"; no
    ///     time-zone conversion is needed at all here, unlike a schedule window, since a pause is
    ///     always expressed and compared directly in UTC.
    /// </summary>
    /// <param name="pausedUntilUtc">The stored pause timestamp, in UTC, or <see langword="null" />.</param>
    /// <param name="nowUtc">The current instant, in UTC, to compare against.</param>
    private static bool IsCurrentlyPaused(DateTimeOffset? pausedUntilUtc, DateTimeOffset nowUtc) =>
        pausedUntilUtc is { } until && until > nowUtc;

    /// <summary>
    ///     This method checks a single schedule's own window against the current instant --
    ///     shared by both the client-scoped and group-scoped loops in <see cref="EvaluateAsync" />
    ///     via the two small overloads below, so the actual window math
    ///     (<see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" />) and the
    ///     "an unrecognized/invalid time zone id never blocks, it is only ever rejected up front
    ///     at write time" defensive posture live in exactly one place.
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
    ///     This method resolves a stored time zone id, never throwing -- a schedule's own
    ///     <c>TimeZoneId</c> is already validated as a real, recognized zone at write time (by
    ///     whichever controller created/updated it), so a lookup failure here can only mean the
    ///     underlying operating system's own time zone database changed after the row was saved;
    ///     treated as "this schedule never blocks" rather than letting the whole query-answering
    ///     path fail, logged once per occurrence for operator visibility.
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
    ///     This method resolves a query's own source address to its known, non-disabled
    ///     <see cref="DnsClientEntity" /> registry row, mirroring
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own identically shaped
    ///     private helper -- see this class's own doc comment for why this is a second,
    ///     independent implementation rather than a shared one.
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
