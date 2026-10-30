using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Settings;

/// <summary>
///     This class supplies the shared Swagger examples for <see cref="RuntimeSettingsWriteResult" />,
///     used by the <c>PATCH</c> actions of most settings controllers, such as
///     <see cref="Controllers.QueryLoggingSettingsController.Patch" /> and
///     <see cref="Controllers.ReverseProxySettingsController.Patch" />. There is one example for
///     each note constant of <see cref="RuntimeSettingsWriteResult" />, named for the write that
///     returns it. A given action returns only some of them, so the dropdown lists every note the
///     settings controllers use, not the ones for that route alone. An empty patch returns a
///     separate "Nothing to change" note that no constant holds.
///     <see cref="Appearance.AppearanceSettingsPatchResultExample" /> is the one
///     controller-specific replacement.
/// </summary>
public class RuntimeSettingsWriteResultExample
    : IMultipleExamplesProvider<RuntimeSettingsWriteResult>
{
    /// <summary>
    ///     This method returns one example per note constant. The first, "Applied at once", is the
    ///     one most actions return. Each name says which kind of write returns the note, and the
    ///     summary says when the change takes effect.
    /// </summary>
    public IEnumerable<SwaggerExample<RuntimeSettingsWriteResult>> GetExamples() =>
        [
            Example(
                "Applied at once",
                "Every field written is read through IOptionsMonitor, so the change is live.",
                RuntimeSettingsWriteResult.HotReloadedNote
            ),
            Example(
                "Process restart needed",
                "The settings are read once at startup through IOptions.",
                RuntimeSettingsWriteResult.RestartRequiredNote
            ),
            Example(
                "Mixed: root hints or trust anchors",
                "The recursive settings when one write changes rootHintServersOverride or trustAnchorsOverride (read once) and a field that applies at once.",
                RuntimeSettingsWriteResult.MixedReloadNote
            ),
            Example(
                "Mixed: ACME or cluster settings",
                "The ACME and cluster settings when one write mixes a field read once at startup with one that applies at once.",
                RuntimeSettingsWriteResult.ReadOnceMixedReloadNote
            ),
            Example(
                "Mixed: query log privacy level",
                "The query log settings when one write changes privacyLevel (live) and any other field (restart).",
                RuntimeSettingsWriteResult.QueryLoggingMixedReloadNote
            ),
            Example(
                "Mixed: DHCP lease sweep interval",
                "The DHCP core settings when one write changes leaseExpirySweepInterval (next sweep) and any other field (restart).",
                RuntimeSettingsWriteResult.DhcpCoreMixedReloadNote
            ),
            Example(
                "Mixed: outbound query pool size",
                "The outbound query settings when one write changes poolSizePerAddressFamily (restart) and enableSourcePortPool or excludedSourcePorts (live).",
                RuntimeSettingsWriteResult.OutboundQueryMixedReloadNote
            ),
            Example(
                "Outbound query pool size: process restart needed",
                "The outbound query settings when a write changes only poolSizePerAddressFamily.",
                RuntimeSettingsWriteResult.OutboundQueryPoolSizeRestartRequiredNote
            ),
            Example(
                "Transport enabled or disabled",
                "A transport write that changes only enable: the listener is started or stopped at once.",
                RuntimeSettingsWriteResult.TransportEnableToggledNote
            ),
            Example(
                "Transport: listener restart needed",
                "A transport write that changes a port, certificate or tuning value and no enable flag.",
                RuntimeSettingsWriteResult.TransportRestartRequiredNote
            ),
            Example(
                "Transport: enable flag plus listener restart",
                "A transport write that changes enable and another field.",
                RuntimeSettingsWriteResult.TransportMixedReloadNote
            ),
            Example(
                "DNS over HTTPS: process restart needed",
                "A DNS over HTTPS write that changes path or maxMessageSizeBytes and not enable.",
                RuntimeSettingsWriteResult.DnsOverHttpsRestartRequiredNote
            ),
            Example(
                "DNS over HTTPS: enable flag plus process restart",
                "A DNS over HTTPS write that changes enable and path or maxMessageSizeBytes.",
                RuntimeSettingsWriteResult.DnsOverHttpsMixedReloadNote
            ),
            Example(
                "Web listener: process restart needed",
                "Every write to the web listener settings.",
                RuntimeSettingsWriteResult.WebListenRestartRequiredNote
            ),
            Example(
                "Pipeline: service restart needed",
                "A DNS pipeline write that changes only workerCount, maxConcurrentQueries or channelCapacity.",
                RuntimeSettingsWriteResult.PipelineWorkerRestartRequiredNote
            ),
            Example(
                "Pipeline: UDP limits plus service restart",
                "A DNS pipeline write that changes a UDP safety field and a worker, concurrency or channel field.",
                RuntimeSettingsWriteResult.PipelineMixedReloadNote
            ),
            Example(
                "Webhook request timeout: applies to new handlers",
                "A webhook delivery write that changes only requestTimeout.",
                RuntimeSettingsWriteResult.WebhookRequestTimeoutEventualNote
            ),
            Example(
                "Webhook: retry delays plus request timeout",
                "A webhook delivery write that changes requestTimeout and a retry delay.",
                RuntimeSettingsWriteResult.WebhookMixedReloadNote
            ),
            Example(
                "Single sign-on: process restart needed",
                "A single sign-on write that changes any field except displayName.",
                RuntimeSettingsWriteResult.SsoRestartRequiredNote
            ),
        ];

    /// <summary>
    ///     This method builds one named example from a note constant.
    /// </summary>
    private static SwaggerExample<RuntimeSettingsWriteResult> Example(
        string name,
        string summary,
        string note
    ) =>
        new()
        {
            Name = name,
            Summary = summary,
            Value = new RuntimeSettingsWriteResult(note),
        };
}
