using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.IdentitySettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="IdentitySettingsDetail" />, as
///     returned by <see cref="Controllers.IdentitySettingsController.Get" />.
/// </summary>
public class IdentitySettingsDetailExample : IExamplesProvider<IdentitySettingsDetail>
{
    /// <summary>
    ///     This method returns the policy
    ///     <c>ServiceCollectionIdentityExtension.AddNinlilDnsIdentity</c> sets: 12 characters with
    ///     every character class, one unique character (the ASP.NET Core default), and a 15-minute
    ///     lockout after 5 failures, new accounts included.
    /// </summary>
    public IdentitySettingsDetail GetExamples() =>
        new(12, 1, true, true, true, true, TimeSpan.FromMinutes(15), 5, true);
}
