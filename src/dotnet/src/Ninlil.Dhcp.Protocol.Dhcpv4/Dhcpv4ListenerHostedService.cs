using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This class is the thin <see cref="IHostedService" /> wrapper this project's composition
///     root registers for the DHCPv4 transport -- the real, genuinely restartable listener logic
///     lives entirely in <see cref="Dhcpv4UdpListener" /> (registered separately as an ordinary
///     dependency-injection singleton, both under its own concrete type and as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />), never in this class.
///     <para>
///         <see cref="StartAsync" /> runs exactly once, at host boot, and starts the underlying
///         listener only when <see cref="Dhcpv4ProtocolOptions.Enable" /> is configured
///         <see langword="true" /> at that moment -- mirroring exactly what this listener's own
///         previous, single-shot <see cref="Microsoft.Extensions.Hosting.BackgroundService" />
///         implementation used to check internally. From that point on, though, the underlying
///         listener stays independently startable/stoppable for the rest of the process's life
///         via <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> -- a future REST
///         surface (or any other in-process caller) can start it even though it booted disabled,
///         or stop it even though it booted enabled, with no restart and no change to this
///         wrapper at all. <see cref="StopAsync" /> always stops the underlying listener
///         unconditionally at host shutdown, regardless of whether it was auto-started at boot or
///         later started/stopped live, so a clean process exit never leaves a bound socket
///         behind.
///     </para>
/// </summary>
/// <param name="listener">The real, controllable listener this wrapper starts/stops.</param>
/// <param name="options">This transport's own configured <c>Enable</c> flag, consulted only at boot.</param>
/// <param name="logger">The logger this wrapper records its own boot-time decision to.</param>
public sealed class Dhcpv4ListenerHostedService(
    Dhcpv4UdpListener listener,
    IOptions<Dhcpv4ProtocolOptions> options,
    ILogger<Dhcpv4ListenerHostedService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            logger.LogInformation(
                "The DHCPv4 listener is disabled ({OptionName} is false); it will not be started "
                    + "automatically at boot, though it can still be started live at any point "
                    + "during this process's lifetime.",
                $"{Dhcpv4ProtocolOptions.SectionName}:{nameof(Dhcpv4ProtocolOptions.Enable)}"
            );
            return Task.CompletedTask;
        }

        return listener.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        listener.StopAsync(cancellationToken);
}
