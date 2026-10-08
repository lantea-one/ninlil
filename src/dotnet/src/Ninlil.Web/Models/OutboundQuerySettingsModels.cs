// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the outbound query settings (<c>OutboundQueryOptions</c>)
///     that <c>GET /api/outbound-query-settings</c> returns. A <c>PATCH</c> to the same endpoint
///     accepts the same fields.
/// </summary>
/// <param name="EnableSourcePortPool">Whether outbound UDP queries reuse a fixed pool of sockets rather than a new socket on a random port each time. Defaults to <see langword="false" /> because a new socket per query gets a fresh random source port.</param>
/// <param name="PoolSizePerAddressFamily">How many sockets, and so source ports, the pool keeps for each of IPv4 and IPv6. Defaults to 128.</param>
/// <param name="ExcludedSourcePorts">Local source ports outbound sockets must not use, such as one another local service listens on. Empty by default.</param>
public record OutboundQuerySettingsDetail(
    bool EnableSourcePortPool,
    int PoolSizePerAddressFamily,
    IReadOnlyList<int> ExcludedSourcePorts
);
