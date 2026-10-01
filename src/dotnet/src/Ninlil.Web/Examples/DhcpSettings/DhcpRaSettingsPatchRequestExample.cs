using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the Swagger example for the RFC 7396 merge patch body of
///     <see cref="Controllers.DhcpSettingsController.PatchRa" />.
/// </summary>
public class DhcpRaSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch that sets a Recursive DNS Server, a DNS Search List and one
    ///     specific route, and adds an <c>eth1</c> override with a shorter router lifetime; every
    ///     other field of the override inherits the global value. <c>routes</c> and
    ///     <c>interfaceOverrides</c> each replace the whole list, so they must hold every entry
    ///     wanted afterwards. The change needs a restart.
    /// </summary>
    public JsonElement GetExamples() =>
        JsonDocument
            .Parse(
                """
                {
                  "recursiveDnsServers": ["2001:db8::1"],
                  "dnsSearchListDomains": ["homelab.internal"],
                  "routes": [
                    {
                      "prefix": "2001:db8:5::",
                      "prefixLength": 64,
                      "preference": "High",
                      "routeLifetime": "00:30:00"
                    }
                  ],
                  "interfaceOverrides": [
                    {
                      "interfaceName": "eth1",
                      "exclude": false,
                      "routerLifetime": "00:05:00"
                    }
                  ]
                }
                """
            )
            .RootElement.Clone();
}
