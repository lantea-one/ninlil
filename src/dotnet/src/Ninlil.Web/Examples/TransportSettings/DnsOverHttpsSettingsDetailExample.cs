using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsOverHttpsSettingsDetail" />, as
///     returned by <see cref="Controllers.TransportSettingsController.GetDnsOverHttps" />.
/// </summary>
public class DnsOverHttpsSettingsDetailExample : IExamplesProvider<DnsOverHttpsSettingsDetail>
{
    /// <summary>
    ///     This method returns DNS-over-HTTPS enabled at the conventional RFC 8484 path
    ///     <c>/dns-query</c> with the default 65535-byte message limit.
    /// </summary>
    public DnsOverHttpsSettingsDetail GetExamples() =>
        new(Enable: true, Path: "/dns-query", MaxMessageSizeBytes: 65535);
}
