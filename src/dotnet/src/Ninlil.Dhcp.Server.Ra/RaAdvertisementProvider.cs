using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class implements <see cref="IRaAdvertisementProvider" />: it builds a
///     <see cref="RouterAdvertisementMessage" /> from <see cref="RaOptions" />, with the matching
///     <see cref="RaOptions.InterfaceOverrides" /> entry applied. Unlike DHCP there is no client
///     state, lease or scope; the content comes only from configuration, read through
///     <see cref="IOptionsMonitor{TOptions}" /> on every call so changes apply at the next send.
///     Entries that do not parse are skipped; other bad values (an IPv4 RDNSS address, a router
///     lifetime too large for its field) are not checked and make building or encoding throw.
/// </summary>
/// <param name="options">The Router Advertisement content configuration.</param>
public sealed class RaAdvertisementProvider(IOptionsMonitor<RaOptions> options)
    : IRaAdvertisementProvider
{
    /// <inheritdoc />
    public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null)
    {
        RaOptions global = options.CurrentValue;
        RaInterfaceOverrideOptions overrideOptions = ResolveOverride(global, interfaceName);

        // An excluded interface advertises nothing, whatever the other fields say.
        if (overrideOptions is { Exclude: true })
            return null;

        bool managedAddressConfiguration =
            overrideOptions?.ManagedAddressConfiguration ?? global.ManagedAddressConfiguration;
        bool otherConfiguration = overrideOptions?.OtherConfiguration ?? global.OtherConfiguration;
        TimeSpan routerLifetime = overrideOptions?.RouterLifetime ?? global.RouterLifetime;
        byte currentHopLimit = overrideOptions?.CurrentHopLimit ?? global.CurrentHopLimit;
        TimeSpan reachableTime = overrideOptions?.ReachableTime ?? global.ReachableTime;
        TimeSpan retransTimer = overrideOptions?.RetransTimer ?? global.RetransTimer;
        uint? linkMtu = overrideOptions?.LinkMtu ?? global.LinkMtu;
        IReadOnlyList<RaAdvertisedPrefixOptions> prefixes =
            overrideOptions?.Prefixes ?? global.Prefixes;
        IReadOnlyList<string> recursiveDnsServers =
            overrideOptions?.RecursiveDnsServers ?? global.RecursiveDnsServers;
        TimeSpan recursiveDnsServerLifetime =
            overrideOptions?.RecursiveDnsServerLifetime ?? global.RecursiveDnsServerLifetime;
        IReadOnlyList<string> dnsSearchListDomains =
            overrideOptions?.DnsSearchListDomains ?? global.DnsSearchListDomains;
        TimeSpan dnsSearchListLifetime =
            overrideOptions?.DnsSearchListLifetime ?? global.DnsSearchListLifetime;
        IReadOnlyList<RaAdvertisedRouteOptions> routes = overrideOptions?.Routes ?? global.Routes;

        // With everything at its "say nothing" default, send no advertisement at all; an empty
        // one would still announce a router to every host on the link.
        bool hasAnythingToAdvertise =
            prefixes.Count > 0
            || routerLifetime > TimeSpan.Zero
            || managedAddressConfiguration
            || otherConfiguration
            || linkMtu is not null
            || recursiveDnsServers.Count > 0
            || dnsSearchListDomains.Count > 0
            || routes.Count > 0;

        if (!hasAnythingToAdvertise)
            return null;

        RouterAdvertisementMessage message = new()
        {
            CurrentHopLimit = currentHopLimit,
            ManagedAddressConfiguration = managedAddressConfiguration,
            OtherConfiguration = otherConfiguration,
            RouterLifetime = routerLifetime,
            ReachableTime = reachableTime,
            RetransTimer = retransTimer,
        };

        foreach (RaAdvertisedPrefixOptions prefix in prefixes)
        {
            // Skip a prefix that does not parse so the rest of the advertisement still goes out.
            if (!IPAddress.TryParse(prefix.Prefix, out IPAddress parsedPrefix))
                continue;

            message.AddPrefixInformation(
                new RaPrefixInformationOption
                {
                    PrefixLength = (byte)prefix.PrefixLength,
                    OnLink = prefix.OnLink,
                    Autonomous = prefix.Autonomous,
                    ValidLifetime = prefix.ValidLifetime,
                    PreferredLifetime = prefix.PreferredLifetime,
                    Prefix = parsedPrefix,
                }
            );
        }

        if (linkMtu is { } mtu)
            message.SetMtu(mtu);

        if (recursiveDnsServers.Count > 0)
        {
            List<IPAddress> parsedDnsServers = [];

            foreach (string dnsServer in recursiveDnsServers)
                if (IPAddress.TryParse(dnsServer, out IPAddress parsedDnsServer))
                    parsedDnsServers.Add(parsedDnsServer);

            if (parsedDnsServers.Count > 0)
                message.SetRecursiveDnsServers(recursiveDnsServerLifetime, parsedDnsServers);
        }

        if (dnsSearchListDomains.Count > 0)
        {
            // Skip a domain the encoder cannot represent (an empty or overlong label, a non-ASCII
            // character), as with prefixes above.
            List<string> validDomainNames =
            [
                .. dnsSearchListDomains.Where(RaDnsSearchListOption.IsValidDomainName),
            ];

            if (validDomainNames.Count > 0)
                message.SetDnsSearchList(dnsSearchListLifetime, validDomainNames);
        }

        foreach (RaAdvertisedRouteOptions route in routes)
        {
            // Skip a route with a prefix length outside 0-128 or a prefix that does not parse.
            if (route.PrefixLength is < 0 or > 128)
                continue;

            IPAddress parsedRoutePrefix = IPAddress.IPv6Any;
            if (route.PrefixLength > 0 && !IPAddress.TryParse(route.Prefix, out parsedRoutePrefix))
                continue;

            message.AddRouteInformation(
                new RaRouteInformationOption
                {
                    PrefixLength = (byte)route.PrefixLength,
                    Preference = route.Preference,
                    RouteLifetime = route.RouteLifetime,
                    Prefix = parsedRoutePrefix,
                }
            );
        }

        return message;
    }

    /// <summary>
    ///     This method returns the first override whose name matches
    ///     <paramref name="interfaceName" /> case-insensitively, or <see langword="null" /> (always
    ///     for a <see langword="null" /> name).
    /// </summary>
    private static RaInterfaceOverrideOptions ResolveOverride(
        RaOptions global,
        string interfaceName
    )
    {
        if (interfaceName is null)
            return null;

        foreach (RaInterfaceOverrideOptions candidate in global.InterfaceOverrides)
            if (
                string.Equals(
                    candidate.InterfaceName,
                    interfaceName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return candidate;

        return null;
    }
}
