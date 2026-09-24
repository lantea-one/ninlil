using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebhookDeliverySettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebhookDeliverySettingsDetail" />, referenced by
///     <see cref="Controllers.WebhookDeliverySettingsController.Get" />.
/// </summary>
public class WebhookDeliverySettingsDetailExample : IExamplesProvider<WebhookDeliverySettingsDetail>
{
    /// <summary>
    ///     This method returns this project's own compiled-in defaults (see
    ///     <see cref="Ninlil.Dns.Server.Webhooks.WebhookDeliveryOptions" />), not arbitrary values.
    /// </summary>
    public WebhookDeliverySettingsDetail GetExamples() =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
}
