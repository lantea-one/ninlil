using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsOverHttpsSettingsDetail" />, referenced by
///     <see cref="Controllers.TransportSettingsController.GetDnsOverHttps" />.
/// </summary>
public class DnsOverHttpsSettingsDetailExample : IExamplesProvider<DnsOverHttpsSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: this transport
    ///     enabled, at RFC 8484's conventional path, this class's own compiled-in default maximum
    ///     message size.
    /// </summary>
    public DnsOverHttpsSettingsDetail GetExamples() =>
        new(Enable: true, Path: "/dns-query", MaxMessageSizeBytes: 65535);
}
