using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class decides which connecting peers are trusted to prepend a PROXY protocol header
///     onto a DNS-over-TLS connection, from the <see cref="ProxyProtocolOptions.KnownProxies" />
///     and <see cref="ProxyProtocolOptions.KnownNetworks" /> allowlist. It repeats the address
///     allowlist part of the trust check in <c>Ninlil.Web.Middleware.ReverseProxyTrustGatingMiddleware</c>,
///     since this project cannot reference <c>Ninlil.Web</c>.
/// </summary>
public static class ProxyProtocolTrustEvaluator
{
    /// <summary>
    ///     This method parses the <see cref="ProxyProtocolOptions.KnownProxies" /> and
    ///     <see cref="ProxyProtocolOptions.KnownNetworks" /> strings into the
    ///     <see cref="IPAddress" /> and <see cref="IPNetwork" /> values <see cref="IsKnownSource" />
    ///     compares against. A malformed entry is skipped with a warning rather than failing the
    ///     whole parse, as in <c>Ninlil.Web.Middleware.ReverseProxyForwardedHeadersConfigurator</c>.
    ///     <c>ReverseProxySettingsController</c> validates entries before saving them, so a bad
    ///     entry can only come from editing the configuration file by hand.
    /// </summary>
    /// <param name="options">The configured allowlist to parse.</param>
    /// <param name="logger">A logger a malformed entry is warned through.</param>
    /// <param name="knownAddresses">Every individually-listed address, successfully parsed.</param>
    /// <param name="knownNetworks">Every listed CIDR network, successfully parsed.</param>
    public static void ParseKnownSources(
        ProxyProtocolOptions options,
        ILogger logger,
        out List<IPAddress> knownAddresses,
        out List<IPNetwork> knownNetworks
    )
    {
        knownAddresses = [];
        knownNetworks = [];

        foreach (string proxy in options.KnownProxies)
        {
            if (IPAddress.TryParse(proxy, out IPAddress address))
                knownAddresses.Add(address);
            else
                logger.LogWarning(
                    "Ignoring malformed DNS-over-TLS PROXY protocol KnownProxies entry '{Entry}' -- not a valid IP address.",
                    proxy
                );
        }

        foreach (string network in options.KnownNetworks)
        {
            if (IPNetwork.TryParse(network, out IPNetwork parsedNetwork))
                knownNetworks.Add(parsedNetwork);
            else
                logger.LogWarning(
                    "Ignoring malformed DNS-over-TLS PROXY protocol KnownNetworks entry '{Entry}' -- not a valid CIDR network.",
                    network
                );
        }
    }

    /// <summary>
    ///     This method checks a peer address against <paramref name="knownAddresses" /> (exact
    ///     match) and <paramref name="knownNetworks" /> (CIDR containment). It tries the address as
    ///     reported and in its <see cref="ClientAddress.Normalize" /> form (an IPv4-mapped
    ///     <c>::ffff:a.b.c.d</c>, as the default dual-stack socket reports IPv4 peers, becomes
    ///     plain IPv4), so a plain IPv4 proxy entry matches either way.
    /// </summary>
    public static bool IsKnownSource(
        IPAddress remoteAddress,
        IReadOnlyList<IPAddress> knownAddresses,
        IReadOnlyList<IPNetwork> knownNetworks
    )
    {
        IPAddress normalizedAddress = ClientAddress.Normalize(remoteAddress);

        foreach (IPAddress knownAddress in knownAddresses)
        {
            if (knownAddress.Equals(remoteAddress) || knownAddress.Equals(normalizedAddress))
                return true;
        }

        foreach (IPNetwork knownNetwork in knownNetworks)
        {
            if (knownNetwork.Contains(remoteAddress) || knownNetwork.Contains(normalizedAddress))
                return true;
        }

        return false;
    }
}
