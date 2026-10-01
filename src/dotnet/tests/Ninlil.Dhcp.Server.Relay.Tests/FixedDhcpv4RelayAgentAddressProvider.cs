using System.Net;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class is a test stand-in for <see cref="Dhcpv4RelayAgentAddressProvider" /> that
///     returns a fixed address instead of detecting one, so tests do not depend on the network
///     interfaces of the machine running them.
/// </summary>
/// <param name="value">The fixed address this stand-in always reports.</param>
internal sealed class FixedDhcpv4RelayAgentAddressProvider(IPAddress value)
    : IDhcpv4RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = value;
}
