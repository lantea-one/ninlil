// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever an operator writes one of the settings sections Phase
///     9-6c's cluster config sync covers (today: event broker, rate limiting, DNS cookies, and
///     reverse-proxy trust -- the four sections <c>PresetConfigurationFileApplier</c>'s own
///     generic <c>IReadOnlyList&lt;PresetConfigurationSetting&gt;</c> overload already writes
///     through, per that class's own doc comment). <see cref="SettingsJson" /> carries the exact
///     list of configuration leaves that were just written, already serialized -- the same shape
///     a receiving node's own settings applier re-hydrates and re-applies via that identical
///     <c>PresetConfigurationFileApplier</c> overload, so this project never needs a second,
///     parallel way to describe "write these configuration keys."
///     <para>
///         Deliberately carries a plain JSON string rather than a strongly-typed settings payload:
///         this project's event definitions live in <c>Ninlil.Dns.Server.Core</c>, which has no
///         dependency on <c>Ninlil.Dns.Server.Presets</c> (where
///         <c>PresetConfigurationSetting</c> itself is declared) -- and, per this project's own
///         layering, never should, so the event's own publisher (a settings controller, in
///         <c>Ninlil.Web</c>, which already depends on both) does the one serialization step
///         instead.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the settings section was written.</param>
/// <param name="SectionName">
///     The settings section's own stable name (<c>ClusterSyncedSettingsStateEntity.SectionName</c>),
///     for example <c>"EventBroker"</c>.
/// </param>
/// <param name="SettingsJson">
///     The list of configuration leaves that were written, serialized as a JSON array of
///     <c>{ sectionPath, key, value }</c> objects (<c>PresetConfigurationSetting</c>'s own shape).
/// </param>
public record ClusterConfigSettingsChangedEvent(
    DateTimeOffset OccurredAtUtc,
    string SectionName,
    string SettingsJson
) : IDnsServerEvent;
