using System.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class tests <see cref="RelayInterfaceLocator" /> against the real network interfaces
///     of the machine running the tests. Only the loopback interface and an address from the
///     documentation range (RFC 5737) are used, since every machine has the first and none owns
///     the second.
/// </summary>
public sealed class RelayInterfaceLocatorTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void TryFind_ForALoopbackAddress_ReturnsTheLoopbackInterface(string text)
    {
        if (text == "::1" && !System.Net.Sockets.Socket.OSSupportsIPv6)
            return;

        bool found = RelayInterfaceLocator.TryFind(
            IPAddress.Parse(text),
            out int interfaceIndex,
            out string interfaceName
        );

        Assert.True(found);
        Assert.True(interfaceIndex > 0);
        Assert.False(string.IsNullOrEmpty(interfaceName));
    }

    [Fact]
    public void TryFind_ForAnAddressNoInterfaceOwns_ReturnsFalse()
    {
        bool found = RelayInterfaceLocator.TryFind(
            IPAddress.Parse("192.0.2.123"),
            out int interfaceIndex,
            out string interfaceName
        );

        Assert.False(found);
        Assert.Equal(0, interfaceIndex);
        Assert.Null(interfaceName);
    }

    [Fact]
    public void TryFind_ForAnAddressInsideAnInterfacesIPv4Prefix_ReturnsThatInterface()
    {
        // 127.0.0.2 is local on Linux because the loopback prefix is 127.0.0.0/8, although the
        // interface lists only 127.0.0.1.
        bool loopbackFound = RelayInterfaceLocator.TryFind(
            IPAddress.Loopback,
            out int loopbackIndex,
            out _
        );
        Assert.True(loopbackFound);

        bool found = RelayInterfaceLocator.TryFind(
            IPAddress.Parse("127.0.0.2"),
            out int interfaceIndex,
            out _
        );

        Assert.True(found);
        Assert.Equal(loopbackIndex, interfaceIndex);
    }
}
