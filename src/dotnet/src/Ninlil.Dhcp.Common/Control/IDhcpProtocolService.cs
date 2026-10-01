// Define our namespace.
namespace Ninlil.Dhcp.Common.Control;

/// <summary>
///     This interface is the common start/stop contract of the DHCP-family listeners (DHCPv4,
///     DHCPv6, Router Advertisement and TFTP), so they can be started and stopped at runtime
///     without a restart. Each implementation (e.g. <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>)
///     is a singleton registered under its concrete type and under this interface.
///     <c>DhcpServiceControlController</c> and <c>DhcpServiceHealthCheck</c> resolve every
///     implementation through this interface.
///     <para>
///         <b>Configured "Enable" and running state are separate.</b> The options class's
///         <c>Enable</c> flag (e.g. <c>Dhcpv4ProtocolOptions.Enable</c>) decides whether a hosted
///         service wrapper (e.g. <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>)
///         calls <see cref="StartAsync" /> at host startup. After that, <see cref="StartAsync" />
///         and <see cref="StopAsync" /> can be called any number of times whatever the flag says.
///         A runtime start or stop is not persisted: a restart goes back to the configured
///         <c>Enable</c> flag, so a lasting change needs a configuration edit.
///     </para>
/// </summary>
public interface IDhcpProtocolService
{
    /// <summary>
    ///     This property reports whether the listener is bound and serving traffic.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    ///     This method starts the listener, binding its socket (and joining a multicast group
    ///     where the transport needs one), unless it is already running, in which case it does
    ///     nothing. It is safe to call concurrently with itself and with <see cref="StopAsync" />.
    ///     A bind failure (the port already in use, for instance) is thrown, and
    ///     <see cref="IsRunning" /> stays <see langword="false" />.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The token that cancels the start operation. It does not affect the receive loop, which
    ///     this call does not wait for.
    /// </param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     This method stops the listener if it is running, releasing its socket (and any
    ///     multicast membership) so a later <see cref="StartAsync" /> can bind the same port again.
    ///     If it is already stopped, it does nothing. It is safe to call concurrently with itself
    ///     and with <see cref="StartAsync" />. It returns only after the receive loop has exited
    ///     and the socket is released, so the port is free when it completes.
    /// </summary>
    /// <param name="cancellationToken">
    ///     The token used to observe cancellation while waiting for the receive loop to exit.
    /// </param>
    Task StopAsync(CancellationToken cancellationToken = default);
}
