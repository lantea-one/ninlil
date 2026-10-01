using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class is the <see cref="IHostedService" /> that starts the DHCPv6 transport at boot,
///     like <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>. The listener itself
///     is <see cref="Dhcpv6UdpListener" />, a singleton also registered as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///     <para>
///         <see cref="StartAsync" /> runs once at boot and starts the listener only when
///         <see cref="Dhcpv6ProtocolOptions.Enable" /> is <see langword="true" />; after that it
///         can be started or stopped at runtime through
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///         <see cref="StopAsync" /> always stops the listener at shutdown, so no socket or
///         multicast membership is left behind.
///     </para>
/// </summary>
/// <param name="listener">The listener this service starts and stops.</param>
/// <param name="options">The transport options; only <c>Enable</c> is read, at boot.</param>
/// <param name="logger">The logger the boot-time decision is recorded to.</param>
public sealed class Dhcpv6ListenerHostedService(
    Dhcpv6UdpListener listener,
    IOptions<Dhcpv6ProtocolOptions> options,
    ILogger<Dhcpv6ListenerHostedService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            logger.LogInformation(
                "The DHCPv6 listener is disabled ({OptionName} is false); it will not be started "
                    + "automatically at boot, though it can still be started live at any point "
                    + "during this process's lifetime.",
                $"{Dhcpv6ProtocolOptions.SectionName}:{nameof(Dhcpv6ProtocolOptions.Enable)}"
            );
            return Task.CompletedTask;
        }

        return listener.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        listener.StopAsync(cancellationToken);
}
