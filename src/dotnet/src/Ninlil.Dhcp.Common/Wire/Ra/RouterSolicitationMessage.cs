// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents a decoded Router Solicitation: the RFC 4861 Section 4.1 ICMPv6
///     type-133 message a host sends to ask every router on the link for an immediate Router
///     Advertisement instead of waiting for the next unsolicited one.
/// </summary>
public class RouterSolicitationMessage
{
    /// <summary>
    ///     This property contains the message's options in wire order. In practice this is at
    ///     most a Source Link-Layer Address option, which RFC 4861 Section 4.1 leaves out only
    ///     when the host solicits from the unspecified address. Use the accessors in
    ///     <see cref="RaMessageOptionExtensions" /> rather than searching this list.
    /// </summary>
    public List<RaOption> Options { get; } = [];
}
