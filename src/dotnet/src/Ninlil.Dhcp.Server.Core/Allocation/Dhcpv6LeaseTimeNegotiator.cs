using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This readonly struct carries the four lifetimes RFC 8415 attaches to an address or
///     delegated prefix: the preferred/valid lifetimes carried on the
///     <c>IAADDR</c>/<c>IAPREFIX</c> option itself, and the T1/T2 renewal timers carried on the
///     owning <c>IA_NA</c>/<c>IA_PD</c> option one level up.
/// </summary>
/// <param name="Preferred">RFC 8415 Section 21.6/21.22's own preferred-lifetime.</param>
/// <param name="Valid">RFC 8415 Section 21.6/21.22's own valid-lifetime.</param>
/// <param name="T1">RFC 8415 Section 21.4/21.21's own T1 renewal timer.</param>
/// <param name="T2">RFC 8415 Section 21.4/21.21's own T2 renewal timer.</param>
public readonly record struct Dhcpv6LeaseTimes(
    TimeSpan Preferred,
    TimeSpan Valid,
    TimeSpan T1,
    TimeSpan T2
);

/// <summary>
///     This static class negotiates the lifetimes to actually grant a client for an address or
///     delegated prefix -- the DHCPv6 analogue of <see cref="Dhcpv4LeaseTimeNegotiator" />. Unlike
///     DHCPv4, RFC 8415 gives a client no option to request a specific lifetime at all (there is
///     no v6 analogue of RFC 2132 Section 9.2's IP Address Lease Time option) -- every lifetime is
///     entirely the server's own unilateral choice, so this negotiation only ever needs the
///     scope's own configuration, never the request itself.
/// </summary>
internal static class Dhcpv6LeaseTimeNegotiator
{
    /// <summary>
    ///     This method computes the lifetimes to grant for a given scope, using the common,
    ///     RFC 8415-recommended proportions between them: the preferred lifetime equals the valid
    ///     lifetime (this project hands out one flat lifetime rather than a shorter renumbering
    ///     window within it), T1 is half the valid lifetime, and T2 is four-fifths of it -- close
    ///     enough ahead of expiry for a well-behaved client to always have time to renew/rebind
    ///     before the lease itself lapses.
    /// </summary>
    /// <param name="scope">The scope the lease is being drawn from.</param>
    /// <returns>The negotiated lifetimes.</returns>
    public static Dhcpv6LeaseTimes Negotiate(DhcpScopeEntity scope)
    {
        TimeSpan valid =
            scope.DefaultLeaseDuration > scope.MaxLeaseDuration
                ? scope.MaxLeaseDuration
                : scope.DefaultLeaseDuration;

        return new Dhcpv6LeaseTimes(Preferred: valid, Valid: valid, T1: valid / 2, T2: valid * 0.8);
    }
}
