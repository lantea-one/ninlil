// Define our namespace.
namespace Ninlil.Dhcp.Common.Control;

/// <summary>
///     This interface is the shared, protocol-agnostic contract every DHCP-family transport
///     listener in this project implements to support a genuine, in-process runtime start/stop --
///     no dependency-injection container rebuild, no process restart -- mirroring the "real hot
///     control" shape <c>Ninlil.Web.Services.Storage.IStorageEngineSwitchService</c> already
///     establishes for a live storage-engine switch. A concrete implementation (e.g.
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>) is registered as an ordinary
///     dependency-injection singleton, resolved both by its own concrete type (so a
///     protocol-specific caller can reach its own extra members, if any) and by this interface
///     (so a protocol-agnostic caller -- a REST controller enumerating every controllable DHCP
///     service, a future sub-phase of this same initiative -- can reach every implementation
///     uniformly without knowing which concrete protocol it is talking to).
///     <para>
///         <b>Configured "Enable" and live running state are two distinct concepts.</b> A bound
///         <c>Options</c> class's own <c>Enable</c> flag (e.g. <c>Dhcpv4ProtocolOptions.Enable</c>)
///         still decides whether an implementation is started automatically the moment this host
///         process boots -- via a thin, separately registered
///         <c>Microsoft.Extensions.Hosting.IHostedService</c> wrapper (e.g.
///         <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>) that calls
///         <see cref="StartAsync" /> once, only if that flag is <see langword="true" />, strictly
///         at host startup -- but this interface's own <see cref="StartAsync" />/
///         <see cref="StopAsync" /> stay callable for the rest of the process's life regardless of
///         what that flag said at boot, and regardless of how many times either has already been
///         called. A live start/stop choice made this way is deliberately <b>not</b> persisted
///         anywhere: every process restart reverts to whatever the configured <c>Enable</c> flag
///         says at that moment, exactly as it always did before this interface existed -- an
///         operator wanting a change to survive a restart still edits configuration, the same as
///         any other restart-required setting in this project.
///     </para>
/// </summary>
public interface IDhcpProtocolService
{
    /// <summary>
    ///     This property reports whether this service's own listener is currently bound and
    ///     actively serving traffic.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     This method starts this service's own listener -- binding its socket (and, for a
    ///     multicast-based transport, joining whatever group it needs) -- if it is not already
    ///     running. Calling this while already running is a safe, harmless no-op, never a
    ///     failure; thread-safe against a concurrent <see cref="StopAsync" /> call, and against a
    ///     second, concurrent call to this same method. A genuine bind failure (the configured
    ///     port already in use, for instance) propagates as a real exception rather than being
    ///     swallowed, leaving this service still reporting <see cref="IsRunning" /> as
    ///     <see langword="false" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The token used to observe cancellation of the start operation itself (not of the
    ///     listener's own subsequent, long-lived receive loop, which this call does not wait
    ///     for).
    /// </param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     This method stops this service's own listener -- releasing its bound socket (and any
    ///     multicast-group membership) so a later <see cref="StartAsync" /> call can genuinely
    ///     re-bind the same port -- if it is currently running. Calling this while already
    ///     stopped is a safe, harmless no-op, never a failure; thread-safe against a concurrent
    ///     <see cref="StartAsync" /> call, and against a second, concurrent call to this same
    ///     method. Returns only once the listener's own receive loop has genuinely exited and its
    ///     socket has been released, so a caller observing this method's own completion can rely
    ///     on the underlying port already being free.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The token used to observe cancellation while waiting for the receive loop to exit.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
