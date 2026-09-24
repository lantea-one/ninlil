using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This class carries the outcome of asking a single <see cref="IDnsQueryResolver" /> to
///     handle a query: either it claims the query and hands back a complete answer, or it
///     declines, telling the dispatcher to move on and offer the query to the next resolver in
///     line.
/// </summary>
public sealed class DnsQueryResolution
{
    /// <summary>
    ///     This property indicates whether the resolver claimed the query.
    /// </summary>
    public bool IsHandled { get; }

    /// <summary>
    ///     This property contains the resolver's answer, if <see cref="IsHandled" /> is
    ///     <see langword="true" />; otherwise <see langword="null" />.
    /// </summary>
    public DnsMessage Response { get; }

    /// <summary>
    ///     This constructor is private; construct an instance through <see cref="NotHandled" /> or
    ///     <see cref="Handled" /> instead.
    /// </summary>
    private DnsQueryResolution(bool isHandled, DnsMessage response)
    {
        IsHandled = isHandled;
        Response = response;
    }

    /// <summary>
    ///     This property contains the shared result a resolver returns to decline a query,
    ///     telling the dispatcher to offer it to the next resolver in line instead.
    /// </summary>
    public static DnsQueryResolution NotHandled { get; } = new(false, null);

    /// <summary>
    ///     This method builds the result a resolver returns when it has fully answered a query.
    /// </summary>
    /// <param name="response">The complete answer.</param>
    /// <returns>A <see cref="DnsQueryResolution" /> carrying <paramref name="response" />.</returns>
    public static DnsQueryResolution Handled(DnsMessage response) => new(true, response);
}
