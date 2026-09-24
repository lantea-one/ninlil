using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ClientPresence;

/// <summary>
///     This class provides the Swagger example for <see cref="ClientPresenceSettingsDetail" /> --
///     the shipped defaults.
/// </summary>
public class ClientPresenceSettingsDetailExample : IExamplesProvider<ClientPresenceSettingsDetail>
{
    /// <inheritdoc />
    public ClientPresenceSettingsDetail GetExamples() =>
        new(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30), true, TimeSpan.FromSeconds(15));
}
