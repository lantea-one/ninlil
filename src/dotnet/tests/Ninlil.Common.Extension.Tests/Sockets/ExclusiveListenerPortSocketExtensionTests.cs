using System.Net;
using System.Net.Sockets;
using Ninlil.Common.Extension.Sockets;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Sockets;

/// <summary>
///     These tests cover <see cref="ExclusiveListenerPortSocketExtension" /> over real UDP sockets
///     on the IPv4 loopback with OS-assigned ports. On Windows the call must make a second bind of
///     the same port fail; on other systems it must do nothing and report that it did nothing.
/// </summary>
public class ExclusiveListenerPortSocketExtensionTests
{
    [Fact]
    public void ClaimPortExclusivelyOnWindows_OnEverySystem_ReportsWhetherItClaimedThePort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        bool claimed = socket.ClaimPortExclusivelyOnWindows();

        Assert.Equal(OperatingSystem.IsWindows(), claimed);
    }

    [Fact]
    public void ClaimPortExclusivelyOnWindows_OnLinuxAndMacOs_LeavesTheSocketUnchanged()
    {
        if (OperatingSystem.IsWindows())
            return;

        using Socket control = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.ClaimPortExclusivelyOnWindows();

        // The call changed no option (the control socket shows the platform's default, which
        // differs between systems), so the socket still binds as before.
        Assert.Equal(control.ExclusiveAddressUse, socket.ExclusiveAddressUse);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    }

    [Fact]
    public void ClaimPortExclusivelyOnWindows_OnWindows_RefusesEverySecondBindOfThePort()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using Socket listener = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        listener.ClaimPortExclusivelyOnWindows();
        listener.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)listener.LocalEndPoint).Port;

        // The combinations Windows allows without exclusive use: a specific address beside the
        // wildcard, and a second wildcard with SO_REUSEADDR (the Winsock documentation's
        // "Using SO_REUSEADDR and SO_EXCLUSIVEADDRUSE").
        using Socket specific = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        Assert.Throws<SocketException>(() =>
            specific.Bind(new IPEndPoint(IPAddress.Loopback, port))
        );

        using Socket reusing = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        Assert.Throws<SocketException>(() => reusing.Bind(new IPEndPoint(IPAddress.Any, port)));
    }
}
