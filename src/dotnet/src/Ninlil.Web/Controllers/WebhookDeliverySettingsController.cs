using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Webhooks;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.WebhookDeliverySettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="WebhookDeliveryOptions" />: the retry backoff
///     and outbound HTTP timeout shared by every webhook subscription's deliveries. Each
///     subscription's attempt limit (<c>MaxDeliveryAttempts</c>) is managed separately, through
///     <see cref="WebhooksController" />. Reading needs <c>webhookdeliverysettings:read</c> and
///     writing needs <c>webhookdeliverysettings:write</c>.
///     <para>
///         <b><c>InitialRetryDelay</c> and <c>MaxRetryDelay</c> take effect at once;
///         <c>RequestTimeout</c> takes effect gradually.</b> <c>WebhookDeliveryService</c> and its
///         <c>ConfigureHttpClient</c> registration read <see cref="IOptionsMonitor{TOptions}" />'s
///         <c>CurrentValue</c> on every delivery attempt. The timeout, though, is applied only when
///         <see cref="IHttpClientFactory" /> builds a new handler for the webhook client; a pooled
///         handler keeps its old timeout until it is replaced (about two minutes by default). No
///         restart is needed.
///     </para>
/// </summary>
/// <param name="webhookDeliveryOptions">The current webhook delivery settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/webhook-delivery-settings")]
public class WebhookDeliverySettingsController(
    IOptionsMonitor<WebhookDeliveryOptions> webhookDeliveryOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current webhook delivery settings. Requires
    ///     <c>webhookdeliverysettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("webhookdeliverysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(WebhookDeliverySettingsDetailExample))]
    public ActionResult<WebhookDeliverySettingsDetail> Get() =>
        Ok(
            new WebhookDeliverySettingsDetail(
                webhookDeliveryOptions.CurrentValue.InitialRetryDelay,
                webhookDeliveryOptions.CurrentValue.MaxRetryDelay,
                webhookDeliveryOptions.CurrentValue.RequestTimeout
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Webhooks:Delivery</c> as an RFC
    ///     7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. The response
    ///     note depends on whether <c>requestTimeout</c> was changed alone, with another field, or
    ///     not at all. Returns 400 for a value that is not a positive TimeSpan. Requires
    ///     <c>webhookdeliverysettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("webhookdeliverysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(WebhookDeliverySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedRequestTimeout = false;
        bool touchedHotReloadedField = false;

        // The retry delays feed Task.Delay; the request timeout becomes an HttpClient timeout.
        if (
            patch.TryGetMergePatchDuration(
                "initialRetryDelay",
                out TimeSpan initialRetryDelay,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.InitialRetryDelay),
                    initialRetryDelay.ToString()
                )
            );
            touchedHotReloadedField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "maxRetryDelay",
                out TimeSpan maxRetryDelay,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.MaxRetryDelay),
                    maxRetryDelay.ToString()
                )
            );
            touchedHotReloadedField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "requestTimeout",
                out TimeSpan requestTimeout,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.HttpTimeoutMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.RequestTimeout),
                    requestTimeout.ToString()
                )
            );
            touchedRequestTimeout = true;
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedRequestTimeout, touchedHotReloadedField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.WebhookMixedReloadNote,
            (true, false) => RuntimeSettingsWriteResult.WebhookRequestTimeoutEventualNote,
            _ => RuntimeSettingsWriteResult.HotReloadedNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }
}
