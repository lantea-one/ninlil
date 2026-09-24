using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.MacVendor;

/// <summary>
///     This class provides the Swagger example for <see cref="MacVendorSettingsDetail" /> -- the
///     shipped defaults, on a server that has refreshed once since installation.
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
