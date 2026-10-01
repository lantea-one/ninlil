using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.MacVendor;

/// <summary>
///     This class supplies the Swagger example for <see cref="MacVendorSettingsDetail" />, as
///     returned by <c>GET /api/mac-vendor-settings</c>: the default settings on a server that has
///     refreshed once. The data file path stands in for the default, which is under the application
///     directory.
/// </summary>
public class MacVendorSettingsDetailExample : IExamplesProvider<MacVendorSettingsDetail>
{
    /// <inheritdoc />
    public MacVendorSettingsDetail GetExamples() =>
        new(
            true,
            TimeSpan.FromDays(14),
            MacVendorLookupOptions.DefaultMaLRegistryUrl,
            MacVendorLookupOptions.DefaultMaMRegistryUrl,
            MacVendorLookupOptions.DefaultMaSRegistryUrl,
            "/opt/ninlil/data/mac-vendors.snapshot.br",
            MacVendorDataStatusDetailExample.Value
        );
}
