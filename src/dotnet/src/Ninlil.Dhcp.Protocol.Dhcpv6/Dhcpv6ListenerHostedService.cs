using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class is the thin <see cref="IHostedService" /> wrapper this project's composition
///     root registers for the DHCPv6 transport -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>. The real, genuinely
///     restartable listener logic lives entirely in <see cref="Dhcpv6UdpListener" /> (registered
///     separately as an ordinary dependency-injection singleton, both under its own concrete type
///     and as <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />), never in this
///     class.
///     <para>
///         <see cref="StartAsync" /> runs exactly once, at host boot, and starts the underlying
///         listener only when <see cref="Dhcpv6ProtocolOptions.Enable" /> is configured
///         <see langword="true" /> at that moment. From that point on, though, the underlying
///         listener stays independently startable/stoppable for the rest of the process's life
///         via <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> -- see that
///         interface's own doc comment for why configured <c>Enable</c> and live running state
///         are two distinct concepts. <see cref="StopAsync" /> always stops the underlying
///         listener unconditionally at host shutdown, regardless of whether it was auto-started
///         at boot or later started/stopped live, so a clean process exit never leaves a bound
///         socket (or multicast-group membership) behind.
///     </para>
/// </summary>
/// <param name="listener">The real, controllable listener this wrapper starts/stops.</param>
/// <param name="options">This transport's own configured <c>Enable</c> flag, consulted only at boot.</param>
/// <param name="logger">The logger this wrapper records its own boot-time decision to.</param>
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
