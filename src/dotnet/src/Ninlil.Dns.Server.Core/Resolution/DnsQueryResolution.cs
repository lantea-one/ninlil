using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This class carries the result of one <see cref="IDnsQueryResolver" />: either it claims
///     the query and returns a complete answer, or it declines and the dispatcher tries the next
///     resolver.
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
    ///     This constructor is private; use <see cref="NotHandled" /> or <see cref="Handled" />.
    /// </summary>
    private DnsQueryResolution(bool isHandled, DnsMessage response)
    {
        IsHandled = isHandled;
        Response = response;
    }

    /// <summary>
    ///     This property contains the shared result a resolver returns to decline a query.
    /// </summary>
    public static DnsQueryResolution NotHandled { get; } = new(false, null);

    /// <summary>
    ///     This method builds the result a resolver returns when it has fully answered a query.
    /// </summary>
    /// <param name="response">The complete answer.</param>
    /// <returns>A <see cref="DnsQueryResolution" /> carrying <paramref name="response" />.</returns>
    public static DnsQueryResolution Handled(DnsMessage response) => new(true, response);
}
