using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class is the thin <see cref="IHostedService" /> wrapper this project's composition
///     root registers for the Router Advertisement transport -- the Router Advertisement analogue
///     of <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ListenerHostedService</c>. The real, genuinely
///     restartable listener logic lives entirely in <see cref="Icmpv6RaListener" /> (registered
///     separately as an ordinary dependency-injection singleton, both under its own concrete type
///     and as <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />), never in this
///     class.
///     <para>
///         <see cref="StartAsync" /> runs exactly once, at host boot, and starts the underlying
///         listener only when <see cref="RaProtocolOptions.Enable" /> is configured
///         <see langword="true" /> at that moment. From that point on, though, the underlying
///         listener stays independently startable/stoppable for the rest of the process's life via
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> -- see that interface's
///         own doc comment for why configured <c>Enable</c> and live running state are two
///         distinct concepts. <see cref="StopAsync" /> always stops the underlying listener
///         unconditionally at host shutdown, regardless of whether it was auto-started at boot or
///         later started/stopped live, so a clean process exit never leaves a bound raw socket (or
///         multicast-group membership) behind.
///     </para>
/// </summary>
/// <param name="listener">The real, controllable listener this wrapper starts/stops.</param>
/// <param name="options">This transport's own configured <c>Enable</c> flag, consulted only at boot.</param>
/// <param name="logger">The logger this wrapper records its own boot-time decision to.</param>
public sealed class Icmpv6RaListenerHostedService(
    Icmpv6RaListener listener,
    IOptions<RaProtocolOptions> options,
    ILogger<Icmpv6RaListenerHostedService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            logger.LogInformation(
                "The Router Advertisement listener is disabled ({OptionName} is false); it will "
                    + "not be started automatically at boot, though it can still be started live "
                    + "at any point during this process's lifetime.",
                $"{RaProtocolOptions.SectionName}:{nameof(RaProtocolOptions.Enable)}"
            );
            return Task.CompletedTask;
        }

        return listener.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        listener.StopAsync(cancellationToken);
}
