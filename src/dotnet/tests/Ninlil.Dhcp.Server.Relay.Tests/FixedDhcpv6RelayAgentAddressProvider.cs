using System.Net;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class is a test stand-in for <see cref="Dhcpv6RelayAgentAddressProvider" /> that
///     returns a fixed address, the DHCPv6 counterpart of
///     <see cref="FixedDhcpv4RelayAgentAddressProvider" />.
/// </summary>
/// <param name="value">The fixed address this stand-in always reports.</param>
internal sealed class FixedDhcpv6RelayAgentAddressProvider(IPAddress value)
    : IDhcpv6RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = value;
}
