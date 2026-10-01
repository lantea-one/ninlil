using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class is the <see cref="IHostedService" /> that starts and stops the TFTP server with
///     the host, like <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ListenerHostedService</c>. The server
///     logic is in <see cref="TftpServer" />, a singleton also registered as
///     <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />.
///     <para>
///         <see cref="StartAsync" /> runs once at boot and starts the server only when
///         <see cref="TftpServerOptions.Enable" /> is set. After that the server can be started
///         and stopped at runtime through
///         <see cref="Ninlil.Dhcp.Common.Control.IDhcpProtocolService" />, whatever <c>Enable</c>
///         says. <see cref="StopAsync" /> always stops the server at shutdown, so no socket or
///         transfer is left behind; stopping aborts transfers in progress (see
///         <see cref="TftpServer" />).
///     </para>
/// </summary>
/// <param name="server">The server this wrapper starts and stops.</param>
/// <param name="options">The TFTP options; only <c>Enable</c> is read, at boot.</param>
/// <param name="logger">The logger the boot-time decision is recorded to.</param>
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
