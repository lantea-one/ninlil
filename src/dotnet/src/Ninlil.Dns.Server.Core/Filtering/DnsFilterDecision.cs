// Define our namespace.
namespace Ninlil.Dns.Server.Core.Filtering;

/// <summary>
///     This class carries the result of one <see cref="IDnsFilterEngine" /> check: denied, with a
///     reason, or allowed. An allow-list match and no match at all both return
///     <see cref="Allowed" />.
/// </summary>
public sealed class DnsFilterDecision
{
    /// <summary>
    ///     This property indicates whether the client, query or response is denied.
    /// </summary>
    public bool IsDenied { get; }

    /// <summary>
    ///     This property contains a short reason for the decision (such as the matching list's
    ///     name), for logs only; the client never sees it.
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
    /// <param name="reason">A short reason, such as the matching list's name.</param>
    public static DnsFilterDecision Denied(string reason) => new(true, reason);
}
