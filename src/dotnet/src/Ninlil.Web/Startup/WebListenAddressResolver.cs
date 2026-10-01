using System.Net;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This static class decides which addresses the web listener -- the admin console, the REST
///     API, the SignalR hub and DNS-over-HTTPS, which all share one Kestrel server -- binds to,
///     from <see cref="WebListenOptions.ListenAddresses" />. It reads the setting the same way the
///     DNS transports do (<see cref="ListenAddressList" />), with two differences that come from
///     serving the admin console:
///     <list type="bullet">
///         <item>
///             If none of the configured addresses can be used, the listener falls back to
///             loopback (127.0.0.1 and ::1) instead of binding nothing. A DNS transport that binds
///             nothing is just switched off, but a web listener that binds nothing locks the
///             operator out of the console they'd fix the setting from. Loopback keeps it reachable
///             from the host itself (or through an SSH tunnel) without exposing it anywhere the
///             operator tried to keep it off.
///         </item>
///         <item>
///             <c>::</c> listens on every IPv4 address too, as Kestrel's IPv6 wildcard always does,
///             so with <c>::</c> in the list the listener simply binds everywhere. (A DNS transport
///             binds <c>::</c> as IPv6-only.)
///         </item>
///     </list>
///     Kestrel binds before the host's logging exists, and an address this host doesn't have
///     would make Kestrel fail the whole startup, so each address is checked here first and a
///     bad one is skipped with an error rather than taking the process down.
/// </summary>
public static class WebListenAddressResolver
{
    /// <summary>
    ///     This method returns the addresses to bind, or <see langword="null" /> to bind every
    ///     address with Kestrel's dual-stack wildcard.
    /// </summary>
    /// <param name="options">The web listener's options, as read at startup.</param>
    /// <param name="logger">Where skipped addresses and the loopback fallback are reported.</param>
    /// <param name="isAssigned">
    ///     Whether an address belongs to this host; <see cref="ListenSocketSet.IsAssignedToThisHost" />
    ///     outside tests.
    /// </param>
    public static IReadOnlyList<IPAddress> Resolve(
        WebListenOptions options,
        ILogger logger,
        Func<IPAddress, bool> isAssigned = null
    )
    {
        isAssigned ??= ListenSocketSet.IsAssignedToThisHost;
        ListenAddressResolution resolution = ListenAddressList.Resolve(options.ListenAddresses);

        if (resolution.ListensEverywhere || resolution.Addresses.Contains(IPAddress.IPv6Any))
            return null;

        foreach (string entry in resolution.InvalidEntries)
            logger.LogError(
                "The web listener skipped listen address '{Entry}': it is not a valid IP address.",
                entry
            );

        List<IPAddress> usable = [];

        foreach (IPAddress address in resolution.Addresses)
        {
            if (isAssigned(address))
                usable.Add(address);
            else
                logger.LogError(
                    "The web listener skipped listen address {Address}: it is not assigned to any network interface on this host.",
                    address
                );
        }

        if (usable.Count > 0)
            return usable;

        logger.LogError(
            "None of the web listener's listen addresses could be used, so the admin console and REST API are listening on loopback only (127.0.0.1 and ::1). Fix the addresses in the configuration file, or clear them to listen on every address, then restart."
        );
        return isAssigned(IPAddress.IPv6Loopback)
            ? [IPAddress.Loopback, IPAddress.IPv6Loopback]
            : [IPAddress.Loopback];
    }
}
