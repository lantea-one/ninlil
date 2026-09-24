using System.Net;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This is a minimal, test-only stand-in for <see cref="Dhcpv6RelayAgentAddressProvider" />
///     that always reports a fixed, test-chosen address rather than genuinely auto-detecting one
///     from this test-runner machine's own real network interfaces -- the DHCPv6 analogue of
///     <see cref="FixedDhcpv4RelayAgentAddressProvider" />.
/// </summary>
/// <param name="value">The fixed address this stand-in always reports.</param>
internal sealed class FixedDhcpv6RelayAgentAddressProvider(IPAddress value)
    : IDhcpv6RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = value;
}
