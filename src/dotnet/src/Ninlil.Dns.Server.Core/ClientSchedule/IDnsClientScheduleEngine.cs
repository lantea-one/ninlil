using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This interface decides whether a client is blocked right now by an instant pause or a
///     time-of-day (parental control) schedule. <see cref="Pipeline.DnsQueryProcessingPipeline" />
///     calls it for every query before the domain filter. It is defined here, without storage
///     types, and implemented by <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c>,
///     so <c>Server.Core</c> needs no repository reference.
/// </summary>
public interface IDnsClientScheduleEngine
{
    /// <summary>
    ///     This method checks whether a client is blocked: first by a pause on the client or its
    ///     client group, which wins over everything, then by an active schedule on the client or
    ///     its group. The client is resolved the same way the filter engine does it, through
    ///     <see cref="ClientIdentity.IClientIdentityResolver" /> and the client repository. An
    ///     address that cannot be resolved, or has no pause or schedule, is not blocked.
    /// </summary>
    /// <param name="remoteAddress">The client's source address.</param>
    /// <param name="nowUtc">
    ///     The current time in UTC, passed in so tests can use a fixed instant.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The resulting block/allow decision.</returns>
    ValueTask<DnsClientScheduleDecision> EvaluateAsync(
        IPAddress remoteAddress,
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    );
}
