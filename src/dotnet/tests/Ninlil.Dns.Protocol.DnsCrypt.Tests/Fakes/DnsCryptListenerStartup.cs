using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;

/// <summary>
///     This class starts the DNSCrypt UDP and TCP listeners for the tests and waits until each has
///     bound or given up, so a test never talks to a listener that is not yet bound. A hosted
///     service binds on its own background task, and a fixed delay is too short on a busy host: the
///     client then sees "connection refused" on TCP or "connection forcibly closed" on UDP (the
///     ICMP port-unreachable reply, which Windows reports as a receive error).
/// </summary>
public static class DnsCryptListenerStartup
{
    /// <summary>
    ///     This constant is how many ports <see cref="StartOnFreePortAsync" /> tries before it
    ///     gives up. A port-0 UDP bind can land in a port range that Windows excludes for TCP only,
    ///     and the following UDP ports lie in the same range, so the search has to walk through a
    ///     block of several hundred ports.
    /// </summary>
    private const int PortSearchLimit = 2000;

    /// <summary>
    ///     This constant is how long <see cref="WaitForBindOutcomeAsync" /> waits for one listener,
    ///     far above a normal bind, so only a listener that hangs fails the test.
    /// </summary>
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This method returns once each listener is bound (<c>IsListening</c>) or has finished
    ///     without binding (a bad provider name, or no usable address), whichever comes first.
    /// </summary>
    /// <exception cref="TimeoutException">A listener neither bound nor finished in time.</exception>
    public static Task WaitForBindOutcomeAsync(DnsCryptUdpListener udp, DnsCryptTcpListener tcp) =>
        WaitUntilAsync(() =>
            (udp.IsListening || udp.ExecuteTask.IsCompleted)
            && (tcp.IsListening || tcp.ExecuteTask.IsCompleted)
        );

    private static async Task WaitUntilAsync(Func<bool> done)
    {
        DateTime deadline = DateTime.UtcNow + BindTimeout;

        while (!done())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("A DNSCrypt listener did not report its bind result.");

            await Task.Delay(10);
        }
    }

    /// <summary>
    ///     This method starts a UDP and a TCP listener on one port the operating system chooses.
    ///     The UDP listener binds port 0 and the TCP listener then binds the port number it got.
    ///     When another program holds that number for TCP, or Windows excludes it for TCP, both
    ///     listeners are stopped and the pair starts again on the next port.
    ///     <para>
    ///         Use it only with a single listen address: with several, port 0 gives each socket
    ///         its own port. A pair whose UDP listener does not bind at all (a bad provider name)
    ///         is returned as it is, because another port would not change that.
    ///     </para>
    /// </summary>
    /// <param name="options">
    ///     The options both listeners read; this method sets <see cref="DnsCryptOptions.Port" />.
    /// </param>
    /// <param name="create">Creates a new listener pair that reads <paramref name="options" />.</param>
    /// <returns>The started pair and the port it shares (0 when the UDP listener did not bind).</returns>
    public static async Task<(
        DnsCryptUdpListener Udp,
        DnsCryptTcpListener Tcp,
        int Port
    )> StartOnFreePortAsync(
        DnsCryptOptions options,
        Func<(DnsCryptUdpListener Udp, DnsCryptTcpListener Tcp)> create
    )
    {
        for (int attempt = 1; ; attempt++)
        {
            options.Port = 0;
            (DnsCryptUdpListener udp, DnsCryptTcpListener tcp) = create();
            await udp.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => udp.IsListening || udp.ExecuteTask.IsCompleted);

            if (udp.IsListening)
                options.Port = udp.BoundPort;

            await tcp.StartAsync(CancellationToken.None);
            await WaitForBindOutcomeAsync(udp, tcp);

            if (!udp.IsListening || tcp.IsListening)
                return (udp, tcp, options.Port);

            await tcp.StopAsync(CancellationToken.None);
            await udp.StopAsync(CancellationToken.None);

            if (attempt == PortSearchLimit)
                throw new InvalidOperationException("No port was free for both UDP and TCP.");
        }
    }
}
