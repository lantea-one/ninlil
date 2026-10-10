using System.Net;
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
///         <see cref="DnsClientDirectory" />. A disabled client resolves to nothing, so neither
///         its schedules nor its group's apply. The group's <c>IsDisabled</c> flag is not checked,
///         since it only affects filter lists. The few lines of resolution are duplicated here
///         rather than shared, to keep this engine independent of the filter engine's hot path.
///     </para>
///     <para>
///         <strong>Cached data.</strong> The client comes from <see cref="DnsClientDirectory" />
///         (with its own pause time) and the group pauses and schedules from
///         <see cref="DnsClientScheduleCache" />, both in-memory copies that a saved change
///         refreshes at once. A query therefore opens no scope and reads no database rows while
///         the copies are fresh. The resolver and the directory are constructor parameters that
///         default to <see langword="null" />; with either missing nothing is blocked.
///     </para>
/// </summary>
/// <param name="cache">The in-memory copy of the schedules and group pauses.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver that turns a source address into a persistent client identifier.
///     With <see langword="null" /> (the default) the engine never blocks anything.
/// </param>
/// <param name="clientDirectory">
///     The optional in-memory client lookup that turns the identifier into a registered client.
///     With <see langword="null" /> (the default) the engine never blocks anything.
/// </param>
public sealed class DnsClientScheduleEngine(
    DnsClientScheduleCache cache,
    IClientIdentityResolver clientIdentityResolver = null,
    DnsClientDirectory clientDirectory = null
) : IDnsClientScheduleEngine
{
    /// <inheritdoc />
    public async ValueTask<DnsClientScheduleDecision> EvaluateAsync(
        IPAddress remoteAddress,
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    )
    {
        if (clientIdentityResolver is null || clientDirectory is null)
            return DnsClientScheduleDecision.Allowed;

        DnsClientInfo client = await ResolveClientAsync(remoteAddress, stoppingToken);

        if (client is null)
            return DnsClientScheduleDecision.Allowed;

        // Pauses come before schedules (see the class summary): the client's pause first, then
        // its group's.
        if (IsCurrentlyPaused(client.PausedUntilUtc, nowUtc))
            return DnsClientScheduleDecision.Blocked(
                $"client '{client.ClientId}' is instantly paused until {client.PausedUntilUtc:O}"
            );

        DnsScheduleSet schedules = await cache.GetAsync(stoppingToken);

        if (
            client.ClientGroupId is { } pausedGroupCandidateId
            && schedules.GroupPauses.TryGetValue(
                pausedGroupCandidateId,
                out DateTimeOffset groupPause
            )
            && IsCurrentlyPaused(groupPause, nowUtc)
        )
            return DnsClientScheduleDecision.Blocked(
                $"client group '{pausedGroupCandidateId}' is instantly paused until {groupPause:O}"
            );

        if (
            schedules.ClientSchedules.TryGetValue(client.ClientId, out DnsScheduleWindow[] own)
            && own.Any(window => IsActive(window, nowUtc))
        )
            return DnsClientScheduleDecision.Blocked(
                $"an active schedule assigned directly to client '{client.ClientId}'"
            );

        if (client.ClientGroupId is not { } clientGroupId)
            return DnsClientScheduleDecision.Allowed;

        if (
            schedules.GroupSchedules.TryGetValue(clientGroupId, out DnsScheduleWindow[] shared)
            && shared.Any(window => IsActive(window, nowUtc))
        )
            return DnsClientScheduleDecision.Blocked(
                $"an active schedule assigned to client group '{clientGroupId}'"
            );

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
    ///     This method checks whether one schedule's window covers the current instant, using
    ///     <see cref="DnsScheduleWindowEvaluator.IsWithinBlockingWindow" />. The cache holds only
    ///     enabled schedules whose time zone resolves.
    /// </summary>
    private static bool IsActive(DnsScheduleWindow window, DateTimeOffset nowUtc) =>
        DnsScheduleWindowEvaluator.IsWithinBlockingWindow(
            window.DaysOfWeek,
            window.StartTime,
            window.EndTime,
            window.TimeZone,
            nowUtc
        );

    /// <summary>
    ///     This method resolves a query's source address to its registered, enabled client, or
    ///     <see langword="null" />. It matches the helper in
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c> (see the class summary for why it is
    ///     not shared).
    /// </summary>
    private async Task<DnsClientInfo> ResolveClientAsync(
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

        DnsClientInfo client = await clientDirectory.FindAsync(
            resolution.ClientIdentifier,
            resolution.ProtocolVersion,
            stoppingToken
        );

        return client is { IsDisabled: false } ? client : null;
    }
}
