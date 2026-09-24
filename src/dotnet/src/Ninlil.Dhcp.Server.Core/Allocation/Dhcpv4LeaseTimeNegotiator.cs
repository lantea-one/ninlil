using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class negotiates the lease duration to actually grant a client, per RFC 2131
///     Section 4.3.1's own description: a client may request a specific duration via RFC 2132
///     Section 9.2's IP Address Lease Time option (tag 51); a server "MUST NOT extend the lease
///     ... beyond the requested time" but "MAY choose to give the client a shorter lease" -- this
///     project's own configurable ceiling, <see cref="DhcpScopeEntity.MaxLeaseDuration" />, is
///     exactly that server-side choice.
/// </summary>
internal static class Dhcpv4LeaseTimeNegotiator
{
    /// <summary>
    ///     This method computes the lease duration to actually grant for a given request against
    ///     a given scope.
    /// </summary>
    /// <param name="scope">The scope the lease is being drawn from.</param>
    /// <param name="request">The client's own DISCOVER or REQUEST message.</param>
    /// <returns>
    ///     The client's own requested duration (RFC 2132 Section 9.2), clamped to
    ///     <see cref="DhcpScopeEntity.MaxLeaseDuration" />, or
    ///     <see cref="DhcpScopeEntity.DefaultLeaseDuration" /> if the client requested none, or an
    ///     invalid (zero or negative) one.
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
