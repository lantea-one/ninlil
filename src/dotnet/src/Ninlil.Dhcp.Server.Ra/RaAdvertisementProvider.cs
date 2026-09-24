using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Wire.Ra;
using Ninlil.Dhcp.Protocol.Ra;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class implements <see cref="IRaAdvertisementProvider" /> by building a
///     <see cref="RouterAdvertisementMessage" /> from <see cref="RaOptions" />'s own global
///     default content, individually resolved per interface against
///     <see cref="RaOptions.InterfaceOverrides" /> -- the only real decisions this engine ever
///     makes, since (unlike DHCPv4/DHCPv6) there is no per-client state, lease, or scope
///     resolution involved in Router Advertisement at all.
/// </summary>
/// <param name="options">This server's own Router Advertisement content configuration.</param>
public sealed class RaAdvertisementProvider(IOptionsMonitor<RaOptions> options)
    : IRaAdvertisementProvider
{
    /// <inheritdoc />
    public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null)
    {
        RaOptions global = options.CurrentValue;
        RaInterfaceOverrideOptions overrideOptions = ResolveOverride(global, interfaceName);

        // An interface explicitly excluded never advertises anything at all, regardless of every
        // other field -- checked first, unconditionally, before resolving a single other field.
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

        // If every one of these is at its own "say nothing" default, there is genuinely nothing
        // for this server to advertise on this interface -- the caller sends no message at all in
        // that case, rather than a real Router Advertisement with every field unset, which would
        // still misleadingly tell every host on the link "I exist and have an opinion."
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
            // An operator-supplied prefix string that fails to parse is skipped rather than
            // crashing this entire engine -- every other configured prefix, and every other part
            // of the advertisement, still goes out correctly.
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
            // An operator-supplied domain suffix this option's own encoder cannot represent (an
            // empty label, an over-length label, or a non-ASCII character) is skipped rather than
            // crashing this entire engine -- the same "skip what doesn't parse, keep going"
            // precedent this method's own prefix/RDNSS handling already establishes above.
            List<string> validDomainNames =
            [
                .. dnsSearchListDomains.Where(RaDnsSearchListOption.IsValidDomainName),
            ];

            if (validDomainNames.Count > 0)
                message.SetDnsSearchList(dnsSearchListLifetime, validDomainNames);
        }

        foreach (RaAdvertisedRouteOptions route in routes)
        {
            // An operator-supplied route this option's own encoder cannot represent (an
            // unparseable prefix, or a prefix length outside 0-128) is skipped rather than
            // crashing this entire engine -- the same "skip what doesn't parse, keep going"
            // precedent this method's own prefix/RDNSS/DNSSL handling already establishes above.
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
    ///     This method finds <paramref name="interfaceName" />'s own configured override, if one
    ///     exists -- matched case-insensitively, since an interface name's own casing is not a
    ///     meaningful distinction on any of this project's real target platforms.
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
