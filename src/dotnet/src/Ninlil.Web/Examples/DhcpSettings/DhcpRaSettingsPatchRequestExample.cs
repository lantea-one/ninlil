using System.Text.Json;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.DhcpSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.DhcpSettingsController.PatchRa" />'s own RFC 7396 JSON Merge Patch
///     request body.
/// </summary>
public class DhcpRaSettingsPatchRequestExample : IExamplesProvider<JsonElement>
{
    /// <summary>
    ///     This method returns a patch configuring a DNS Search List and a specific advertised
    ///     route alongside a Recursive DNS Server -- restart-required, per
    ///     <see cref="Controllers.DhcpSettingsController" />'s own doc comment. The
    ///     <c>routes</c>/<c>interfaceOverrides</c> arrays are each a whole-list replace: every
    ///     route/interface override this server should advertise after this patch applies, not
    ///     merely the ones being added. This example's own <c>eth1</c> entry advertises a shorter
    ///     router lifetime on that one interface, leaving every other field (and every other
    ///     interface) inheriting the global content above unchanged.
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
