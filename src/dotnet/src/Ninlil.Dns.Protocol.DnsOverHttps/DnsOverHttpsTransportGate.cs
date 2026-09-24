using Microsoft.Extensions.Hosting;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverHttps;

/// <summary>
///     This class gives RFC 8484 DNS-over-HTTPS a real, in-process hot start/stop lifecycle when
///     it is mapped directly onto an already-running host's own shared Kestrel instance (see
///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />)
///     rather than self-hosted via <see cref="DnsOverHttpsHostedService" />'s own separate,
///     independently start/stop-able Kestrel instance.
///     <para>
///         ASP.NET Core's own Minimal API routing has no supported "unmap this endpoint" operation
///         -- once <c>MapGet</c>/<c>MapPost</c> registers a route against a running host, it stays
///         registered for the life of the process. This class exists purely to give
///         <see cref="DnsOverHttpsRequestHandler" /> a live, checkable "should I actually answer
///         this request" signal instead: every GET/POST request still reaches the already-mapped
///         route, but is answered <c>503 Service Unavailable</c> immediately, without ever
///         touching the DNS resolution pipeline, whenever this gate reports itself stopped. From a
///         client's own point of view this is functionally equivalent to the socket-level
///         stop/start every other transport listener in this project genuinely performs -- a
///         query sent while this gate is closed gets no usable answer, exactly as it would against
///         an unbound socket.
///     </para>
/// </summary>
public sealed class DnsOverHttpsTransportGate : IHostedService
{
    /// <summary>
    ///     This field tracks whether a request should currently be answered. Marked
    ///     <see langword="volatile" /> since the request-handling path and a future
    ///     <c>ServicesController</c>'s own start/stop actions run on different threads.
    /// </summary>
    private volatile bool _isRunning;

    /// <summary>
    ///     This property reports whether a DNS-over-HTTPS request reaching the already-mapped
    ///     route should currently be answered.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    ///     This method opens the gate, so a subsequent request is answered normally. Called once,
    ///     directly (not through the generic host, since this class is never registered as an
    ///     ordinary <see cref="IHostedService" />), by
    ///     <see cref="Extension.EndpointRouteBuilderDnsOverHttpsExtension.MapNinlilDnsOverHttps" />
    ///     right after mapping the route, and again by a future <c>ServicesController</c>'s own
    ///     start/restart actions.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    /// <summary>
    ///     This method closes the gate, so a subsequent request is answered
    ///     <c>503 Service Unavailable</c> instead of a real DNS response, until
    ///     <see cref="StartAsync" /> is called again.
    /// </summary>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _isRunning = false;
        return Task.CompletedTask;
    }
}
