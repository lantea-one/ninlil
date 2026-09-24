// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents a complete Router Solicitation message -- RFC 4861 Section 4.1's
///     ICMPv6 type-133 message a host sends asking every router on its link to send an immediate
///     Router Advertisement rather than waiting for the next unsolicited one.
/// </summary>
public class RouterSolicitationMessage
{
    /// <summary>
    ///     This property contains every RFC 4861 Section 4.6 option this message carries, in the
    ///     order they appeared on the wire -- in practice, at most a Source Link-Layer Address
    ///     option (RFC 4861 Section 4.1 says a host omits it only when soliciting from the
    ///     unspecified address, before it has configured one of its own). Prefer
    ///     <see cref="RaMessageOptionExtensions" />'s strongly-typed accessors to read a specific
    ///     well-known option rather than searching this list by hand.
    /// </summary>
    public List<RaOption> Options { get; } = [];
}
