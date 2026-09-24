// Define our namespace.
namespace Ninlil.Dns.Server.Core.Filtering;

/// <summary>
///     This class carries the outcome of a single <see cref="IDnsFilterEngine" /> check: allow,
///     deny, or "no list matched at all" (which behaves as allow, but is kept distinct so a
///     caller can tell "explicitly allowed" apart from "nothing said anything").
/// </summary>
public sealed class DnsFilterDecision
{
    /// <summary>
    ///     This property indicates whether the query/response/client is denied.
    /// </summary>
    public bool IsDenied { get; }

    /// <summary>
    ///     This property contains a short, human-readable reason for this decision (which list
    ///     matched, and its own name) -- used for logging, never shown to the querying client.
    /// </summary>
    public string Reason { get; }

    private DnsFilterDecision(bool isDenied, string reason)
    {
        IsDenied = isDenied;
        Reason = reason;
    }

    /// <summary>
    ///     This property contains the shared result for "nothing denied this."
    /// </summary>
    public static DnsFilterDecision Allowed { get; } = new(false, null);

    /// <summary>
    ///     This method builds the result for "a block list matched, and no allow list overrode
    ///     it."
    /// </summary>
    /// <param name="reason">A short, human-readable reason (the matching list's own name).</param>
    public static DnsFilterDecision Denied(string reason) => new(true, reason);
}
