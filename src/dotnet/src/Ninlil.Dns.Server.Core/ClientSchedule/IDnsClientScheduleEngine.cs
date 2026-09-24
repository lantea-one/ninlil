using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This interface is <c>Server.Core</c>'s own Data.Abstractions-free contract for time-of-day
///     parental-control scheduling and instant-pause enforcement -- the same "define the
///     abstraction here, implement it in a dedicated project that may depend on storage" shape
///     <see cref="Filtering.IDnsFilterEngine" />/<see cref="RateLimiting.IDnsRateLimiter" />/
///     <see cref="AccessControl.IDnsAccessControlEngine" /> already establish, kept consistent so
///     <c>Server.Core</c> itself never needs a repository reference. Consulted by
///     <see cref="Pipeline.DnsQueryProcessingPipeline" /> to answer one question: "is this query's
///     resolved client currently blocked by an active bedtime-style schedule, or by a one-click
///     instant pause?" -- both are temporary, fully-blocking states this one engine evaluates
///     together, with an instant pause always checked first and always taking priority (see the
///     real implementation's own doc comment for why).
/// </summary>
public interface IDnsClientScheduleEngine
{
    /// <summary>
    ///     This method checks whether a querying client is currently blocked -- by an instant
    ///     pause (its own direct pause, or its resolved client group's pause, checked first and
    ///     unconditionally overriding everything else) or, failing that, by an active time-of-day
    ///     schedule (again, its own direct schedule or its resolved client group's schedule, an
    ///     independent union of the two). Resolves the client the exact same way
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c> does (via
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> and
    ///     <c>Ninlil.Dns.Data.Abstractions.Repository.IDnsClientRepository</c>) -- an unresolvable
    ///     address, or an address with neither a pause nor a schedule at all, is never blocked.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="nowUtc">
    ///     The current instant, in UTC -- passed explicitly (rather than read internally via
    ///     <see cref="DateTimeOffset.UtcNow" />) so a caller can evaluate against a fixed,
    ///     reproducible instant, the same testability reasoning most of this project's own
    ///     time-sensitive engines already follow.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The resulting block/allow decision.</returns>
    ValueTask<DnsClientScheduleDecision> EvaluateAsync(
        IPAddress remoteAddress,
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    );
}
