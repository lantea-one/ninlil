using System.Net;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClientIdentity;

/// <summary>
///     This class tries every configured <see cref="IClientIdentityResolver" />, in a fixed
///     order, returning the first non-null answer -- the composition root's own choice of which
///     resolvers to try, and in what order, is made once, explicitly, in <c>Ninlil.Web/Program.cs</c>
///     (the same "select the active implementation once, before dependency injection is built"
///     precedent already governing which message-broker transport, which OIDC scheme, and which
///     DHCPv4/DHCPv6 engine variant is active), not resolved dynamically or reordered at runtime.
///     This is deliberately a plain ordered fallback list, not a generic priority/plugin system --
///     this project has exactly two real resolvers today (a DHCP-lease lookup and an ARP/neighbor
///     -table lookup), and a third would still just be appended to the list a caller passes in.
/// </summary>
/// <param name="resolvers">
///     Every resolver to try, in the exact order to try them -- typically the DHCP-lease resolver
///     first (faster and more authoritative when this server itself issued the lease), then the
///     ARP/neighbor-table resolver as a fallback that also covers a client whose lease came from
///     an entirely different DHCP server.
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
        // Try each resolver in turn, stopping at the first one that actually knows this address --
        // a resolver returning null means only "I don't know this address," never a hard failure,
        // per IClientIdentityResolver's own contract.
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
