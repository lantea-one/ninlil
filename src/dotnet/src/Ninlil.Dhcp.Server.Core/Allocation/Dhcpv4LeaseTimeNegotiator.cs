using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class chooses the lease duration to grant a DHCPv4 client. A client may ask for
///     a duration with the IP Address Lease Time option (RFC 2132 Section 9.2, tag 51), and RFC
///     2131 Section 4.3.1 leaves the final choice to the server. This one grants the requested
///     time, capped at <see cref="DhcpScopeEntity.MaxLeaseDuration" />.
/// </summary>
internal static class Dhcpv4LeaseTimeNegotiator
{
    /// <summary>
    ///     This method computes the lease duration to grant for a request in a scope.
    /// </summary>
    /// <param name="scope">The scope the lease is being drawn from.</param>
    /// <param name="request">The client's DISCOVER or REQUEST message.</param>
    /// <returns>
    ///     The requested duration, or <see cref="DhcpScopeEntity.DefaultLeaseDuration" /> when the
    ///     client asked for none or for zero, capped at <see cref="DhcpScopeEntity.MaxLeaseDuration" />.
    /// </returns>
    public static TimeSpan Negotiate(DhcpScopeEntity scope, Dhcpv4Message request)
    {
        TimeSpan requested =
            request.TryGetIpAddressLeaseTime(out TimeSpan requestedLeaseTime)
            && requestedLeaseTime > TimeSpan.Zero
                ? requestedLeaseTime
                : scope.DefaultLeaseDuration;

        return requested > scope.MaxLeaseDuration ? scope.MaxLeaseDuration : requested;
    }
}
