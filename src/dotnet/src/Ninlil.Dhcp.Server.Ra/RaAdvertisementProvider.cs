using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
///     Building never throws for any configured value, because the monitor can deliver a
///     hand-edited file that the settings controller never saw. <see cref="RaContentRules" />
///     decides what is valid: an invalid list entry (a prefix, an IPv4 RDNSS address, a route) is
///     skipped, an out-of-range scalar (a router lifetime over 65 535 s) is clamped to the nearest
///     valid value, and an MTU under 1280 is omitted. Each distinct problem is logged once as a
///     Warning until the options change.
/// </summary>
public sealed class RaAdvertisementProvider : IRaAdvertisementProvider
{
    private readonly IOptionsMonitor<RaOptions> options;
    private readonly ILogger<RaAdvertisementProvider> logger;

    /// <summary>
    ///     This field holds the problems already logged since the options last changed, so a
    ///     problem that persists across advertisements is reported once. Guarded by itself.
    /// </summary>
    private readonly HashSet<string> loggedProblems = [];

    /// <summary>
    ///     This constructor wires the provider to the options monitor and clears the logged
    ///     problems whenever the options change.
    /// </summary>
    /// <param name="options">The Router Advertisement content configuration.</param>
    /// <param name="logger">The logger for configuration problems, or <see langword="null" /> for none.</param>
    public RaAdvertisementProvider(
        IOptionsMonitor<RaOptions> options,
        ILogger<RaAdvertisementProvider> logger = null
    )
    {
        this.options = options;
        this.logger = logger ?? NullLogger<RaAdvertisementProvider>.Instance;

        options.OnChange(
            (_, _) =>
            {
                lock (loggedProblems)
                    loggedProblems.Clear();
            }
        );
    }

    /// <inheritdoc />
    public RouterAdvertisementMessage BuildCurrentAdvertisement(string interfaceName = null)
    {
        RaOptions global = options.CurrentValue;
        RaInterfaceOverrideOptions overrideOptions = ResolveOverride(global, interfaceName);

        // An excluded interface advertises nothing, whatever the other fields say.
        if (overrideOptions is { Exclude: true })
            return null;

        string where = interfaceName is null ? "" : $" (interface '{interfaceName}')";
        List<string> problems = [];

        bool managedAddressConfiguration =
            overrideOptions?.ManagedAddressConfiguration ?? global.ManagedAddressConfiguration;
        bool otherConfiguration = overrideOptions?.OtherConfiguration ?? global.OtherConfiguration;
        TimeSpan routerLifetime = ClampSeconds(
            overrideOptions?.RouterLifetime ?? global.RouterLifetime,
            RaContentRules.MaxRouterLifetimeSeconds,
            RaContentRules.CheckRouterLifetime,
            "routerLifetime",
            where,
            problems
        );
        byte currentHopLimit = overrideOptions?.CurrentHopLimit ?? global.CurrentHopLimit;
        TimeSpan reachableTime = ClampMilliseconds(
            overrideOptions?.ReachableTime ?? global.ReachableTime,
            RaContentRules.MaxReachableTimeMilliseconds,
            RaContentRules.CheckReachableTime,
            "reachableTime",
            where,
            problems
        );
        TimeSpan retransTimer = ClampMilliseconds(
            overrideOptions?.RetransTimer ?? global.RetransTimer,
            uint.MaxValue,
            RaContentRules.CheckRetransTimer,
            "retransTimer",
            where,
            problems
        );
        uint? linkMtu = overrideOptions?.LinkMtu ?? global.LinkMtu;
        if (RaContentRules.CheckLinkMtu(linkMtu) is { } mtuProblem)
        {
            problems.Add($"{mtuProblem} The MTU option is omitted{where}.");
            linkMtu = null;
        }

        IReadOnlyList<RaAdvertisedPrefixOptions> prefixes =
            overrideOptions?.Prefixes ?? global.Prefixes;
        IReadOnlyList<string> recursiveDnsServers =
            overrideOptions?.RecursiveDnsServers ?? global.RecursiveDnsServers;
        TimeSpan recursiveDnsServerLifetime = ClampNegative(
            overrideOptions?.RecursiveDnsServerLifetime ?? global.RecursiveDnsServerLifetime,
            "recursiveDnsServerLifetime",
            where,
            problems
        );
        IReadOnlyList<string> dnsSearchListDomains =
            overrideOptions?.DnsSearchListDomains ?? global.DnsSearchListDomains;
        TimeSpan dnsSearchListLifetime = ClampNegative(
            overrideOptions?.DnsSearchListLifetime ?? global.DnsSearchListLifetime,
            "dnsSearchListLifetime",
            where,
            problems
        );
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
            // Skip a prefix the rules reject so the rest of the advertisement still goes out.
            // The prefix is masked to its length: hosts form addresses from the prefix bits only.
            string prefixProblem =
                RaContentRules.CheckPrefix(prefix.Prefix, prefix.PrefixLength)
                ?? RaContentRules.CheckPrefixLifetimes(
                    prefix.ValidLifetime,
                    prefix.PreferredLifetime
                );

            if (
                prefixProblem is not null
                || !RaContentRules.TryNormalizePrefix(
                    prefix.Prefix,
                    prefix.PrefixLength,
                    out IPAddress maskedPrefix
                )
            )
            {
                problems.Add(
                    $"Prefix '{prefix.Prefix}/{prefix.PrefixLength}' is skipped{where}: "
                        + prefixProblem
                );
                continue;
            }

            message.AddPrefixInformation(
                new RaPrefixInformationOption
                {
                    PrefixLength = (byte)prefix.PrefixLength,
                    OnLink = prefix.OnLink,
                    Autonomous = prefix.Autonomous,
                    ValidLifetime = prefix.ValidLifetime,
                    PreferredLifetime = prefix.PreferredLifetime,
                    Prefix = maskedPrefix,
                }
            );
        }

        if (linkMtu is { } mtu)
            message.SetMtu(mtu);

        if (recursiveDnsServers.Count > 0)
        {
            List<IPAddress> parsedDnsServers = [];

            foreach (string dnsServer in recursiveDnsServers)
            {
                if (RaContentRules.CheckRecursiveDnsServer(dnsServer) is { } dnsServerProblem)
                    problems.Add(
                        $"RDNSS address '{dnsServer}' is skipped{where}: {dnsServerProblem}"
                    );
                else if (parsedDnsServers.Count >= RaContentRules.MaxRecursiveDnsServers)
                    problems.Add(
                        $"RDNSS address '{dnsServer}' is skipped{where}: only "
                            + $"{RaContentRules.MaxRecursiveDnsServers} fit in one option."
                    );
                else
                    parsedDnsServers.Add(IPAddress.Parse(dnsServer));
            }

            if (parsedDnsServers.Count > 0)
                message.SetRecursiveDnsServers(recursiveDnsServerLifetime, parsedDnsServers);
        }

        if (dnsSearchListDomains.Count > 0)
        {
            // Skip a domain the encoder cannot represent (an empty or overlong label, a non-ASCII
            // character), as with prefixes above.
            List<string> validDomainNames = [];

            foreach (string domainName in dnsSearchListDomains)
                if (RaDnsSearchListOption.IsValidDomainName(domainName))
                    validDomainNames.Add(domainName);
                else
                    problems.Add($"DNS search domain '{domainName}' is skipped{where}.");

            if (validDomainNames.Count > 0)
                message.SetDnsSearchList(dnsSearchListLifetime, validDomainNames);
        }

        foreach (RaAdvertisedRouteOptions route in routes)
        {
            // Skip a route the rules reject; a valid one is masked to its length.
            string routeProblem =
                RaContentRules.CheckRoutePrefix(route.Prefix, route.PrefixLength)
                ?? RaContentRules.CheckLifetime(route.RouteLifetime, "routeLifetime");

            if (
                routeProblem is not null
                || !RaContentRules.TryNormalizeRoutePrefix(
                    route.Prefix,
                    route.PrefixLength,
                    out IPAddress maskedRoutePrefix
                )
            )
            {
                problems.Add(
                    $"Route '{route.Prefix}/{route.PrefixLength}' is skipped{where}: "
                        + routeProblem
                );
                continue;
            }

            message.AddRouteInformation(
                new RaRouteInformationOption
                {
                    PrefixLength = (byte)route.PrefixLength,
                    Preference = route.Preference,
                    RouteLifetime = route.RouteLifetime,
                    Prefix = maskedRoutePrefix,
                }
            );
        }

        LogNewProblems(problems);
        return message;
    }

    /// <summary>
    ///     This method logs the problems not yet reported since the options last changed, as one
    ///     Warning, so a persistent problem does not repeat on every advertisement.
    /// </summary>
    private void LogNewProblems(List<string> problems)
    {
        if (problems.Count == 0)
            return;

        List<string> newProblems;
        lock (loggedProblems)
            newProblems = [.. problems.Distinct().Where(loggedProblems.Add)];

        if (newProblems.Count > 0)
            logger.LogWarning(
                "The Router Advertisement configuration has values that are skipped or "
                    + "adjusted: {Problems}",
                string.Join(" ", newProblems)
            );
    }

    /// <summary>
    ///     This method clamps a duration to 0 to <paramref name="maxSeconds" /> seconds and records
    ///     the adjustment.
    /// </summary>
    private static TimeSpan ClampSeconds(
        TimeSpan value,
        double maxSeconds,
        Func<TimeSpan, string, string> check,
        string field,
        string where,
        List<string> problems
    )
    {
        if (check(value, field) is not { } problem)
            return value;

        TimeSpan clamped = TimeSpan.FromSeconds(value < TimeSpan.Zero ? 0 : maxSeconds);
        problems.Add($"{problem} It is sent as {clamped}{where}.");
        return clamped;
    }

    /// <summary>
    ///     This method clamps a duration to 0 to <paramref name="maxMilliseconds" /> milliseconds
    ///     and records the adjustment.
    /// </summary>
    private static TimeSpan ClampMilliseconds(
        TimeSpan value,
        double maxMilliseconds,
        Func<TimeSpan, string, string> check,
        string field,
        string where,
        List<string> problems
    )
    {
        if (check(value, field) is not { } problem)
            return value;

        TimeSpan clamped = TimeSpan.FromMilliseconds(value < TimeSpan.Zero ? 0 : maxMilliseconds);
        problems.Add($"{problem} It is sent as {clamped}{where}.");
        return clamped;
    }

    /// <summary>
    ///     This method raises a negative lifetime to zero and records the adjustment.
    /// </summary>
    private static TimeSpan ClampNegative(
        TimeSpan value,
        string field,
        string where,
        List<string> problems
    )
    {
        if (RaContentRules.CheckLifetime(value, field) is not { } problem)
            return value;

        problems.Add($"{problem} It is sent as zero{where}.");
        return TimeSpan.Zero;
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
