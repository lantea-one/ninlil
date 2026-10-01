using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Ra;

/// <summary>
///     This class is the <see cref="IHostedService" /> that starts the Router Advertisement
///     transport at boot, like <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6ListenerHostedService</c>. The
///     listener itself is <see cref="Icmpv6RaListener" />, a singleton also registered as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///     <para>
///         <see cref="StartAsync" /> runs once at boot and starts the listener only when
///         <see cref="RaProtocolOptions.Enable" /> is <see langword="true" />; after that it can
///         be started or stopped at runtime through
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///         <see cref="StopAsync" /> always stops the listener at shutdown, so no raw socket or
///         multicast membership is left behind.
///     </para>
/// </summary>
/// <param name="listener">The listener this service starts and stops.</param>
/// <param name="options">The transport options; only <c>Enable</c> is read, at boot.</param>
/// <param name="logger">The logger the boot-time decision is recorded to.</param>
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
