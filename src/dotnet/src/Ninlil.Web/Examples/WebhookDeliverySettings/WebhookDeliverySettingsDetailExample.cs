using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.WebhookDeliverySettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="WebhookDeliverySettingsDetail" />, as
///     returned by <see cref="Controllers.WebhookDeliverySettingsController.Get" />.
/// </summary>
public class WebhookDeliverySettingsDetailExample : IExamplesProvider<WebhookDeliverySettingsDetail>
{
    /// <summary>
    ///     This method returns the defaults of
    ///     <see cref="Ninlil.Dns.Server.Webhooks.WebhookDeliveryOptions" />.
    /// </summary>
    public WebhookDeliverySettingsDetail GetExamples() =>
        new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10));
}
