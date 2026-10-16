using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class holds the one rule that decides whether a client may use this server's
///     recursive service. <see cref="RecursiveQueryResolver" /> (iteration) and
///     <c>ForwarderQueryResolver</c> (forwarding) both call it, so the two cannot drift apart. The
///     rule exists because an open recursive resolver is an amplification vector and BCP 140
///     (RFC 5358) asks that recursive service be limited to its intended clients.
/// </summary>
public static class RecursionPermission
{
    /// <summary>
    ///     This method tells whether the client at <paramref name="remoteAddress" /> may use the
    ///     recursive service: <see cref="RecursiveServerOptions.EnableRecursion" /> is on for
    ///     everyone, or, while it is off, an access-control rule grants
    ///     <see cref="DnsAccessControlCapabilityEnum.RecursionAccess" /> to the address.
    /// </summary>
    /// <param name="options">The recursive settings, read once by the caller for the query.</param>
    /// <param name="accessControlEngine">
    ///     The access-control engine, or <see langword="null" /> when none is registered, in which
    ///     case only <see cref="RecursiveServerOptions.EnableRecursion" /> counts.
    /// </param>
    /// <param name="remoteAddress">The client's address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the client may recurse.</returns>
    public static async ValueTask<bool> IsPermittedAsync(
        RecursiveServerOptions options,
        IDnsAccessControlEngine accessControlEngine,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    ) =>
        options.EnableRecursion
        || (
            accessControlEngine is not null
            && await accessControlEngine.HasCapabilityAsync(
                remoteAddress,
                DnsAccessControlCapabilityEnum.RecursionAccess,
                stoppingToken
            )
        );
}
