using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class is the thin <see cref="IHostedService" /> wrapper this project's composition
///     root registers for the TFTP server -- the TFTP analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>. The real, genuinely
///     restartable server logic lives entirely in <see cref="TftpServer" /> (registered separately
///     as an ordinary dependency-injection singleton, both under its own concrete type and as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />), never in this class.
///     <para>
///         <see cref="StartAsync" /> runs exactly once, at host boot, and starts the underlying
///         server only when <see cref="TftpServerOptions.Enable" /> is configured
///         <see langword="true" /> at that moment. From that point on, though, the underlying
///         server stays independently startable/stoppable for the rest of the process's life via
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" /> -- see that interface's
///         own doc comment for why configured <c>Enable</c> and live running state are two
///         distinct concepts. <see cref="StopAsync" /> always stops the underlying server
///         unconditionally at host shutdown, regardless of whether it was auto-started at boot or
///         later started/stopped live, so a clean process exit never leaves a bound socket (or an
///         in-flight transfer) behind -- see <see cref="TftpServer" />'s own doc comment for why a
///         stop aborts an in-flight transfer rather than waiting for it to finish.
///     </para>
/// </summary>
/// <param name="server">The real, controllable server this wrapper starts/stops.</param>
/// <param name="options">This server's own configured <c>Enable</c> flag, consulted only at boot.</param>
/// <param name="logger">The logger this wrapper records its own boot-time decision to.</param>
public sealed class TftpServerHostedService(
    TftpServer server,
    IOptions<TftpServerOptions> options,
    ILogger<TftpServerHostedService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enable)
        {
            logger.LogInformation(
                "The TFTP server is disabled ({OptionName} is false); it will not be started "
                    + "automatically at boot, though it can still be started live at any point "
                    + "during this process's lifetime.",
                $"{TftpServerOptions.SectionName}:{nameof(TftpServerOptions.Enable)}"
            );
            return Task.CompletedTask;
        }

        return server.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        server.StopAsync(cancellationToken);
}
