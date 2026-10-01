using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.MacVendor;

/// <summary>
///     This class supplies the Swagger example for <see cref="MacVendorDataStatusDetail" />, as
///     returned by <c>POST /api/mac-vendor-settings/refresh</c>: a refresh that downloaded newer
///     registry data. The three registry counts add up to the total.
/// </summary>
public class MacVendorDataStatusDetailExample : IExamplesProvider<MacVendorDataStatusDetail>
{
    /// <summary>
    ///     This property contains the example value, shared with <see cref="MacVendorSettingsDetailExample" />.
    /// </summary>
    public static MacVendorDataStatusDetail Value { get; } =
        new(
            MacVendorDataSourceEnum.Downloaded,
            DateTimeOffset.Parse("2026-10-07T03:12:00+00:00"),
            40214,
            6599,
            7200,
            54013,
            DateTimeOffset.Parse("2026-10-07T03:12:00+00:00"),
            MacVendorRefreshResultEnum.Updated,
            null,
            DateTimeOffset.Parse("2026-10-07T03:12:00+00:00"),
            DateTimeOffset.Parse("2026-10-21T03:12:00+00:00")
        );

    /// <inheritdoc />
    public MacVendorDataStatusDetail GetExamples() => Value;
}
