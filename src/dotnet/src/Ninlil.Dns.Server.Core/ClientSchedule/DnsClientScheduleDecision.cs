// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientSchedule;

/// <summary>
///     This class carries the result of one <see cref="IDnsClientScheduleEngine.EvaluateAsync" />
///     check. It has the same shape as <see cref="Filtering.DnsFilterDecision" />; a schedule
///     block is answered with the same denial response as a filter-list block (see
///     <see cref="Pipeline.DnsQueryProcessingPipeline" />).
/// </summary>
public sealed class DnsClientScheduleDecision
{
    /// <summary>
    ///     This property indicates whether the client is blocked by a pause or an active schedule.
    /// </summary>
    public bool IsBlocked { get; }

    /// <summary>
    ///     This property contains a short reason for the decision, for logs only; the client never
    ///     sees it.
    /// </summary>
    public string Reason { get; }

    private DnsClientScheduleDecision(bool isBlocked, string reason)
    {
        IsBlocked = isBlocked;
        Reason = reason;
    }

    /// <summary>
    ///     This property contains the shared result for "not blocked".
    /// </summary>
    public static DnsClientScheduleDecision Allowed { get; } = new(false, null);

    /// <summary>
    ///     This method builds the result for "blocked by a pause or schedule".
    /// </summary>
    /// <param name="reason">A short reason, such as which pause or schedule matched.</param>
    public static DnsClientScheduleDecision Blocked(string reason) => new(true, reason);
}
