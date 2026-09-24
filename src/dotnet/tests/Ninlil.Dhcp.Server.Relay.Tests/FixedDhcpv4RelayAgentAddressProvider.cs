using System.Net;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This is a minimal, test-only stand-in for <see cref="Dhcpv4RelayAgentAddressProvider" />
///     that always reports a fixed, test-chosen address rather than genuinely auto-detecting one
///     from this test-runner machine's own real network interfaces -- keeping every test in this
///     project fully deterministic, independent of whatever interfaces happen to exist on
///     whichever machine actually runs the suite.
/// </summary>
/// <param name="value">The fixed address this stand-in always reports.</param>
internal sealed class FixedDhcpv4RelayAgentAddressProvider(IPAddress value)
    : IDhcpv4RelayAgentAddressProvider
{
    /// <inheritdoc />
    public IPAddress Value { get; } = value;
}
