using System.Net;
using Microsoft.Extensions.Logging;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class decides which connecting peers are trusted to prepend a PROXY protocol header
///     onto a DNS-over-TLS connection, from a <see cref="ProxyProtocolOptions" /> instance's own
///     <see cref="ProxyProtocolOptions.KnownProxies" />/<see cref="ProxyProtocolOptions.KnownNetworks" />
///     allowlist -- the IP/CIDR-allowlist half of
///     <c>Ninlil.Web.Middleware.ReverseProxyTrustGatingMiddleware</c>'s own trust check,
///     independently re-implemented here (not cross-project-referenced, since this project has no
///     reference onto <c>Ninlil.Web</c> at all) for the identical reasoning
///     <c>Ninlil.Dns.Server.Rewrites.DnsRewriteEngine</c> already documents for its own
///     independent re-implementation of a wildcard-matching technique it could not reference
///     cross-project either.
/// </summary>
public static class ProxyProtocolTrustEvaluator
{
    /// <summary>
    ///     This method parses <paramref name="options" />'s own string-typed
    ///     <see cref="ProxyProtocolOptions.KnownProxies" />/<see cref="ProxyProtocolOptions.KnownNetworks" />
    ///     into the real <see cref="IPAddress" />/<see cref="IPNetwork" /> values
    ///     <see cref="IsKnownSource" /> compares against, once, up front -- a malformed entry is
    ///     skipped and warned about rather than failing the whole parse, mirroring
    ///     <c>Ninlil.Web.Middleware.ReverseProxyForwardedHeadersConfigurator</c>'s own
    ///     precedent (this should never actually happen against configuration
    ///     <c>ReverseProxySettingsController</c>'s own <c>PATCH</c> action wrote, since it already
    ///     validates every entry before persisting it, but a non-REST edit of the configuration
    ///     file could still introduce one).
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
    ///     This method checks a connecting peer's own address against both
    ///     <paramref name="knownAddresses" /> (an exact match) and <paramref name="knownNetworks" />
    ///     (a CIDR containment check) -- comparing both the address as reported and, since this
    ///     listener binds an IPv6 socket that also accepts IPv4 connections mapped into IPv6 form
    ///     (<c>::ffff:a.b.c.d</c>), its own unmapped IPv4 equivalent when one exists, so an operator
    ///     configuring a plain IPv4 proxy address or network still matches regardless of which form
    ///     the connection actually arrived in.
    /// </summary>
    public static bool IsKnownSource(
        IPAddress remoteAddress,
        IReadOnlyList<IPAddress> knownAddresses,
        IReadOnlyList<IPNetwork> knownNetworks
    )
    {
        IPAddress mappedAddress = remoteAddress.IsIPv4MappedToIPv6
            ? remoteAddress.MapToIPv4()
            : null;

        foreach (IPAddress knownAddress in knownAddresses)
        {
            if (
                knownAddress.Equals(remoteAddress)
                || (mappedAddress is not null && knownAddress.Equals(mappedAddress))
            )
                return true;
        }

        foreach (IPNetwork knownNetwork in knownNetworks)
        {
            if (
                knownNetwork.Contains(remoteAddress)
                || (mappedAddress is not null && knownNetwork.Contains(mappedAddress))
            )
                return true;
        }

        return false;
    }
}
