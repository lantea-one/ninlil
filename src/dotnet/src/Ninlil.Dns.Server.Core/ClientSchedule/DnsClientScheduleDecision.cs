// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This class carries the outcome of a single <see cref="IDnsClientScheduleEngine.EvaluateAsync" />
///     check -- mirroring <see cref="Filtering.DnsFilterDecision" /> exactly, since a schedule
///     block is meant to look identical, on the wire, to a filter-list block (see
///     <see cref="Pipeline.DnsQueryProcessingPipeline" />'s own consultation of this engine for
///     the shared response-building path both denials go through).
/// </summary>
public sealed class DnsClientScheduleDecision
{
    /// <summary>
    ///     This property indicates whether the querying client is currently blocked by an active
    ///     schedule.
    /// </summary>
    public bool IsBlocked { get; }

    /// <summary>
    ///     This property contains a short, human-readable reason for this decision -- used for
    ///     logging, never shown to the querying client.
    /// </summary>
    public string Reason { get; }

    private DnsClientScheduleDecision(bool isBlocked, string reason)
    {
        IsBlocked = isBlocked;
        Reason = reason;
    }

    /// <summary>
    ///     This property contains the shared result for "no active schedule blocks this client."
    /// </summary>
    public static DnsClientScheduleDecision Allowed { get; } = new(false, null);

    /// <summary>
    ///     This method builds the result for "an active schedule blocks this client."
    /// </summary>
    /// <param name="reason">A short, human-readable reason (which schedule matched, and why).</param>
    public static DnsClientScheduleDecision Blocked(string reason) => new(true, reason);
}
