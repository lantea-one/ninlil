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
///     This controller reads and writes this host's own <see cref="WebhookDeliveryOptions" /> --
///     the shared retry/backoff timing and outbound HTTP timeout every webhook subscription's own
///     deliveries use -- `appsettings.json`-only until this pass. Every action requires the
///     <c>webhookdeliverysettings:read</c> or <c>webhookdeliverysettings:write</c> permission.
///     This is deliberately separate from <c>WebhookSubscriptionsController</c>, which manages
///     each subscription's own <c>MaxDeliveryAttempts</c>; this controller manages the shared
///     timing tuning every subscription's own retries use alike.
///     <para>
///         <b><c>InitialRetryDelay</c>/<c>MaxRetryDelay</c> take effect immediately, with no
///         restart; <c>RequestTimeout</c> is a partial exception.</b> <c>WebhookDeliveryService</c>
///         and its own <c>ConfigureHttpClient</c> registration used to read
///         <see cref="WebhookDeliveryOptions" /> via the snapshotted <see cref="IOptions{TOptions}" />,
///         cached for the life of the process the first time it was read -- a real functional gap
///         beyond REST/UI exposure, the same shape already found and fixed elsewhere in this
///         project for other options classes, converted here the same way to
///         <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c>. Every field is now
///         read fresh on every single delivery attempt, except <see cref="WebhookDeliveryOptions.RequestTimeout" />,
///         which is only ever applied to a new <see cref="System.Net.Http.HttpClient" /> handler
///         <see cref="IHttpClientFactory" /> builds for the named webhook client -- an
///         already-pooled handler keeps using whatever timeout was in effect when it was built,
///         for the rest of that handler's own lifetime (roughly two minutes by that factory's own
///         default). Neither a restart nor a true hot-reload: no operator action is needed, but
///         the change is not instant either.
///     </para>
/// </summary>
/// <param name="webhookDeliveryOptions">This host's own currently-effective webhook-delivery-timing settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/webhook-delivery-settings")]
public class WebhookDeliverySettingsController(
    IOptionsMonitor<WebhookDeliveryOptions> webhookDeliveryOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     webhook-delivery-timing settings.
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
    ///     This action merges a partial update into <c>DnsServer:Webhooks:Delivery</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. The response note names exactly which of the three notes this
    ///     class's own doc comment describes applies, depending on whether <c>requestTimeout</c>
    ///     was touched alone, alongside another field, or not at all.
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

        if (
            patch.TryGetMergePatchField(
                "initialRetryDelay",
                out JsonElement initialRetryDelayElement
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.InitialRetryDelay),
                    ParsePositiveTimeSpan(initialRetryDelayElement, "initialRetryDelay").ToString()
                )
            );
            touchedHotReloadedField = true;
        }

        if (patch.TryGetMergePatchField("maxRetryDelay", out JsonElement maxRetryDelayElement))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.MaxRetryDelay),
                    ParsePositiveTimeSpan(maxRetryDelayElement, "maxRetryDelay").ToString()
                )
            );
            touchedHotReloadedField = true;
        }

        if (patch.TryGetMergePatchField("requestTimeout", out JsonElement requestTimeoutElement))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    WebhookDeliveryOptions.SectionName,
                    nameof(WebhookDeliveryOptions.RequestTimeout),
                    ParsePositiveTimeSpan(requestTimeoutElement, "requestTimeout").ToString()
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

    /// <summary>
    ///     This method parses a merge-patch field expected to be a positive <see cref="TimeSpan" />,
    ///     matching this project's own established convention of a small, controller-local helper
    ///     over a shared one (see <c>TryGetDuration</c>'s own several independent copies already
    ///     in this codebase).
    /// </summary>
    private static TimeSpan ParsePositiveTimeSpan(JsonElement element, string fieldName)
    {
        if (!TimeSpan.TryParse(element.GetString(), out TimeSpan parsed) || parsed <= TimeSpan.Zero)
            throw new ValidationException(
                $"'{fieldName}' must be a valid, positive TimeSpan (for example \"00:00:10\")."
            );

        return parsed;
    }
}
