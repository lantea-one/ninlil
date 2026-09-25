using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class tries a fixed list of <see cref="IClientIdentityResolver" />s in order and
///     returns the first non-null answer. <c>Ninlil.Web/Program.cs</c> builds the list once at
///     startup: the static address mappings, then DHCP leases (when this server grants leases
///     itself), then the ARP/neighbor table. It is registered as the
///     <see cref="IClientIdentityResolver" /> the filter, schedule and presence code use.
/// </summary>
/// <param name="resolvers">
///     The resolvers to try, in order.
/// </param>
public sealed class CompositeClientIdentityResolver(
    IReadOnlyList<IClientIdentityResolver> resolvers
) : IClientIdentityResolver
{
    /// <inheritdoc />
    public async Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    )
    {
        // Null means "unknown address", so move on to the next resolver.
        foreach (IClientIdentityResolver resolver in resolvers)
        {
            ClientIdentityResolution resolution = await resolver.ResolveAsync(
                remoteAddress,
                stoppingToken
            );

            if (resolution is not null)
                return resolution;
        }

        return null;
    }
}
