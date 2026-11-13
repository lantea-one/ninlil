using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Ninlil.Common.Extension.Sockets;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps.Extension;

/// <summary>
///     This static class makes the Kestrel servers of Ninlil claim their TCP ports exclusively on
///     Windows. Kestrel creates and binds its listening sockets itself, and its default socket
///     factory does not set exclusive address use, so another program could share the port of the
///     web server or of DNS over HTTPS. <c>Ninlil.Web</c> and
///     <see cref="DnsOverHttpsHostedService" /> both call
///     <see cref="UseExclusiveListenSockets" /> when they configure their web host. Microsoft
///     documents the hook it uses, <see cref="SocketTransportOptions.CreateBoundListenSocket" />.
///     On Linux and macOS the Kestrel default factory runs unchanged.
/// </summary>
public static class WebHostBuilderExclusiveListenSocketExtension
{
    /// <summary>
    ///     This extension method installs <see cref="CreateBoundListenSocket" /> as the factory of
    ///     Kestrel's sockets transport.
    /// </summary>
    /// <param name="builder">The web host builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IWebHostBuilder UseExclusiveListenSockets(this IWebHostBuilder builder) =>
        builder.UseSockets(options => options.CreateBoundListenSocket = CreateBoundListenSocket);

    /// <summary>
    ///     This method creates and binds the listening socket of one Kestrel endpoint. On Windows
    ///     an IP endpoint gets a socket that claims its port exclusively; the socket is otherwise
    ///     the one Kestrel's own factory creates (a TCP socket, dual-mode for the IPv6 wildcard,
    ///     because Kestrel expects <c>[::]</c> to serve IPv4 too). Every other endpoint type (a
    ///     Unix socket, a file handle) and every other system goes to Kestrel's own factory.
    ///     Public so a test can bind through the same code Kestrel runs.
    /// </summary>
    /// <param name="endpoint">The endpoint Kestrel listens on.</param>
    /// <returns>The bound socket.</returns>
    public static Socket CreateBoundListenSocket(EndPoint endpoint)
    {
        if (!OperatingSystem.IsWindows() || endpoint is not IPEndPoint ipEndPoint)
            return SocketTransportOptions.CreateDefaultBoundListenSocket(endpoint);

        Socket socket = new(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            if (ipEndPoint.Address.Equals(IPAddress.IPv6Any))
                socket.DualMode = true;

            socket.ClaimPortExclusivelyOnWindows();
            socket.Bind(ipEndPoint);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }
}
