using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This readonly struct carries the four times RFC 8415 attaches to an address or delegated
///     prefix: the preferred and valid lifetimes on the <c>IAADDR</c> or <c>IAPREFIX</c> option,
///     and the T1/T2 timers on the containing <c>IA_NA</c> or <c>IA_PD</c>.
/// </summary>
/// <param name="Preferred">The preferred-lifetime (RFC 8415 Sections 21.6 and 21.22).</param>
/// <param name="Valid">The valid-lifetime (RFC 8415 Sections 21.6 and 21.22).</param>
/// <param name="T1">The T1 renewal timer (RFC 8415 Sections 21.4 and 21.21).</param>
/// <param name="T2">The T2 rebinding timer (RFC 8415 Sections 21.4 and 21.21).</param>
public readonly record struct Dhcpv6LeaseTimes(
    TimeSpan Preferred,
    TimeSpan Valid,
    TimeSpan T1,
    TimeSpan T2
);

/// <summary>
///     This static class chooses the lifetimes to grant for an address or delegated prefix, the
///     DHCPv6 counterpart of <see cref="Dhcpv4LeaseTimeNegotiator" />. DHCPv6 has no lease time
///     option; lifetimes a client puts in its IA are only hints, and this class ignores them, so
///     only the scope's configuration matters.
/// </summary>
internal static class Dhcpv6LeaseTimeNegotiator
{
    /// <summary>
    ///     This method computes the lifetimes for a scope. The valid lifetime is
    ///     <see cref="DhcpScopeEntity.DefaultLeaseDuration" /> capped at
    ///     <see cref="DhcpScopeEntity.MaxLeaseDuration" />, and the preferred lifetime is the same
    ///     (there is no shorter renumbering window). T1 is 0.5 and T2 0.8 of it, the ratios RFC
    ///     8415 recommends, which leave the client time to renew or rebind before expiry.
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
