using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class lets DNS over HTTPS be started and stopped while the process runs when it is
///     mapped onto the web host's Kestrel server (see
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />)
///     rather than hosted on its own by <see cref="DnsOverHttpsHostedService" />.
///     <c>Ninlil.Web/Program.cs</c> registers it as a singleton.
///     <para>
///         Minimal API routing cannot unmap an endpoint: once <c>MapGet</c>/<c>MapPost</c> adds a
///         route, it stays for the life of the process. So the route stays mapped and
///         <see cref="DnsOverHttpsRequestHandler" /> checks this gate: while it is closed, every
///         request is answered <c>503 Service Unavailable</c> without reaching the pipeline. For a
///         client that is as good as the socket-level stop the other transports do: a query gets
///         no usable answer.
///     </para>
/// </summary>
public sealed class DnsOverHttpsTransportGate : IHostedService
{
    /// <summary>
    ///     This field tracks whether requests should be answered. It is <see langword="volatile" />
    ///     because request handling and the start and stop actions run on different threads.
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    ///     This property reports whether a DNS-over-HTTPS request reaching the mapped route should
    ///     be answered.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    ///     This method opens the gate, so later requests are answered normally. The generic host
    ///     never calls it (the gate is not registered as a hosted service).
    ///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />
    ///     calls it after mapping the route when the transport starts enabled, and the web host's
    ///     service start/restart and transport settings actions call it later.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method closes the gate, so later requests are answered
    ///     <c>503 Service Unavailable</c> instead of a DNS response until <see cref="StartAsync" />
    ///     is called again.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _isRunning = false;
        return Task.CompletedTask;
    }
}
