using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class starts the lookup of every host-name forwarder when the host starts, so the
///     addresses are usually known before the first query and that query does not skip the
///     forwarder. It runs in the background and does not delay readiness. It does nothing while
///     <see cref="ForwardingOptions.Enable" /> is off at startup; the first query after forwarding
///     is turned on starts the lookups then. A failed load is logged and left to the first query.
/// </summary>
/// <param name="sets">The forwarder sets whose definitions are warmed.</param>
/// <param name="endpoints">The cache that resolves them.</param>
/// <param name="options">The <see cref="ForwardingOptions.Enable" /> switch, read at startup.</param>
/// <param name="logger">The logger the outcome is recorded to.</param>
public sealed class ForwarderEndpointWarmUpService(
    ForwarderSetCache sets,
    ForwarderEndpointCache endpoints,
    IOptionsMonitor<ForwardingOptions> options,
    ILogger<ForwarderEndpointWarmUpService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Return control to the host at once; loading the sets must not hold up startup.
        await Task.Yield();

        if (!options.CurrentValue.Enable)
            return;

        try
        {
            IReadOnlySet<string> definitions = await sets.GetAllDefinitionsAsync(stoppingToken);

            // Each call starts a background lookup for a host name and returns at once.
            foreach (string definition in definitions)
                endpoints.TryGetSendable(definition);
        }
        catch (OperationCanceledException)
        {
            // The host is stopping.
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Could not start the forwarder host lookups at startup; the first query will start them."
            );
        }
    }
}
