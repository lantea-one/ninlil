using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Protocol.DnsOverHttps.Extension;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Tests;

/// <summary>
///     These tests cover <see cref="WebHostBuilderExclusiveListenSocketExtension" /> with real
///     TCP sockets on OS-assigned ports. On Windows the socket it creates must refuse every
///     second bind of its port, in the combinations Windows allows without exclusive use (a
///     specific address beside the dual-stack wildcard Kestrel binds by default, and a wildcard
///     with SO_REUSEADDR). On every system the IPv6 wildcard endpoint must come back dual-mode
///     and bound, as Kestrel's own factory returns it.
/// </summary>
public class WebHostBuilderExclusiveListenSocketExtensionTests
{
    [Fact]
    public void CreateBoundListenSocket_ForTheIPv6Wildcard_BindsADualModeSocketOnAnAssignedPort()
    {
        if (!Socket.OSSupportsIPv6)
            return;

        using Socket socket = WebHostBuilderExclusiveListenSocketExtension.CreateBoundListenSocket(
            new IPEndPoint(IPAddress.IPv6Any, 0)
        );

        Assert.True(socket.DualMode);
        Assert.NotEqual(0, ((IPEndPoint)socket.LocalEndPoint).Port);
    }

    [Fact]
    public void CreateBoundListenSocket_OnWindows_RefusesEverySecondBindOfThePort()
    {
        if (!OperatingSystem.IsWindows() || !Socket.OSSupportsIPv6)
            return;

        using Socket listener =
            WebHostBuilderExclusiveListenSocketExtension.CreateBoundListenSocket(
                new IPEndPoint(IPAddress.IPv6Any, 0)
            );
        listener.Listen();
        int port = ((IPEndPoint)listener.LocalEndPoint).Port;

        using Socket specific = new(
            AddressFamily.InterNetwork,
            SocketType.Stream,
            ProtocolType.Tcp
        );
        Assert.Throws<SocketException>(() =>
            specific.Bind(new IPEndPoint(IPAddress.Loopback, port))
        );

        using Socket reusing = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        Assert.Throws<SocketException>(() => reusing.Bind(new IPEndPoint(IPAddress.Any, port)));
    }

    [Fact]
    public void CreateBoundListenSocket_ForAUnixDomainSocketEndpoint_LeavesItToKestrelsDefaultFactory()
    {
        // Kestrel can listen on a Unix domain socket path; the hook must not turn that into a
        // TCP socket. Unix domain sockets work on Windows 10 and later as well.
        if (!Socket.OSSupportsUnixDomainSockets)
            return;

        string directory = Path.Combine(Path.GetTempPath(), $"ninlil-uds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "s.sock");

        try
        {
            using Socket socket =
                WebHostBuilderExclusiveListenSocketExtension.CreateBoundListenSocket(
                    new UnixDomainSocketEndPoint(path)
                );

            Assert.Equal(AddressFamily.Unix, socket.AddressFamily);
            Assert.Equal(path, ((UnixDomainSocketEndPoint)socket.LocalEndPoint).ToString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
