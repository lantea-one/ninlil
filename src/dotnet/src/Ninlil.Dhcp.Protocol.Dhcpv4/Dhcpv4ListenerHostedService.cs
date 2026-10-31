using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This class is the <see cref="IHostedService" /> that starts the DHCPv4 transport at boot.
///     The listener itself is <see cref="Dhcpv4UdpListener" />, a singleton also registered as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///     <para>
///         <see cref="StartAsync" /> runs once at boot and starts the listener only when
///         <see cref="Dhcpv4ProtocolOptions.Enable" /> is <see langword="true" />. After that the
///         listener can be started or stopped at runtime through
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> (for example from
///         <c>DhcpServiceControlController</c>). <see cref="StopAsync" /> always stops the
///         listener at shutdown, however it was started, so no socket is left bound.
///     </para>
///     <para>
///         A bind failure at boot (a port another program holds, a missing permission) does
///         not stop the host: <see cref="StartAsync" /> logs one Error and goes on, and
///         <see cref="Dhcpv4UdpListener.StartFailure" /> keeps the reason for the <c>dhcpServices</c> health
///         check.
///     </para>
/// </summary>
/// <param name="listener">The listener this service starts and stops.</param>
/// <param name="options">The transport options; only <c>Enable</c> is read, at boot.</param>
/// <param name="logger">The logger the boot-time decision is recorded to.</param>
public sealed class Dhcpv4ListenerHostedService(
    Dhcpv4UdpListener listener,
    IOptions<Dhcpv4ProtocolOptions> options,
    ILogger<Dhcpv4ListenerHostedService> logger
) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            logger.LogInformation(
                "The DHCPv4 listener is disabled ({OptionName} is false); it will not be started "
                    + "automatically at boot, though it can still be started live at any point "
                    + "during this process's lifetime.",
                $"{Dhcpv4ProtocolOptions.SectionName}:{nameof(Dhcpv4ProtocolOptions.Enable)}"
            );
            return;
        }

        try
        {
            await listener.StartAsync(cancellationToken);
        }
        catch (SocketException exception)
        {
            // An exception out of a hosted service's StartAsync stops the whole host, taking DNS,
            // the other DHCP services and the REST API with it, and the operator could not reach
            // the settings to fix the cause. Report it once instead; the health check shows the
            // service as not running with the reason, and the service can be started again from
            // the service control API after the cause is gone.
            logger.LogError(
                exception,
                "The DHCPv4 listener could not bind its socket: {Reason}. This service is not running; the rest of Ninlil is unaffected. Free the port or fix the permission, then restart Ninlil or start the service again from the DHCP service control.",
                exception.Message
            );
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        listener.StopAsync(cancellationToken);
}
