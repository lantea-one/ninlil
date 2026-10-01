using Ninlil.Dhcp.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DhcpRaSettingsDetail" />, as returned
///     by <see cref="Controllers.DhcpSettingsController.GetRa" />.
/// </summary>
public class DhcpRaSettingsDetailExample : IExamplesProvider<DhcpRaSettingsDetail>
{
    /// <summary>
    ///     This method returns a router configuration: a non-zero router lifetime (so hosts use
    ///     this server as their default router), a Recursive DNS Server, a DNS Search List, one
    ///     specific route, and an <c>eth1</c> override that only shortens the router lifetime.
    /// </summary>
    public DhcpRaSettingsDetail GetExamples() =>
        new(
            ManagedAddressConfiguration: false,
            OtherConfiguration: false,
            RouterLifetime: TimeSpan.FromSeconds(1800),
            CurrentHopLimit: 64,
            ReachableTime: TimeSpan.Zero,
            RetransTimer: TimeSpan.Zero,
            LinkMtu: null,
            RecursiveDnsServers: ["2001:db8::1"],
            RecursiveDnsServerLifetime: TimeSpan.FromSeconds(1800),
            DnsSearchListDomains: ["homelab.internal"],
            DnsSearchListLifetime: TimeSpan.FromSeconds(1800),
            Routes:
            [
                new DhcpRaRouteDetail(
                    Prefix: "2001:db8:5::",
                    PrefixLength: 64,
                    Preference: RaRoutePreferenceEnum.High,
                    RouteLifetime: TimeSpan.FromSeconds(1800)
                ),
            ],
            InterfaceOverrides:
            [
                new DhcpRaInterfaceOverrideDetail(
                    InterfaceName: "eth1",
                    Exclude: false,
                    ManagedAddressConfiguration: null,
                    OtherConfiguration: null,
                    RouterLifetime: TimeSpan.FromSeconds(300),
                    CurrentHopLimit: null,
                    ReachableTime: null,
                    RetransTimer: null,
                    LinkMtu: null,
                    RecursiveDnsServers: null,
                    RecursiveDnsServerLifetime: null,
                    DnsSearchListDomains: null,
                    DnsSearchListLifetime: null
                ),
            ]
        );
}
