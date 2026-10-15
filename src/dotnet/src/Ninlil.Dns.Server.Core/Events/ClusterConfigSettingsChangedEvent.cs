// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when an operator writes one of the settings sections that
///     cluster config sync covers: event broker, rate limiting, DNS cookies, reverse-proxy
///     trust and rewrite settings. <c>ClusterConfigSyncSettingsRecorder</c> (Ninlil.Web) publishes it and
///     <c>SettingsClusterSyncEventSubscriber</c> forwards it to every peer, where it is applied
///     through <c>PresetConfigurationFileApplier</c>, the same writer the local change used.
///     <para>
///         <see cref="SettingsJson" /> is a plain JSON string because <c>Server.Core</c> must not
///         reference <c>Ninlil.Dns.Server.Presets</c>, where <c>PresetConfigurationSetting</c>
///         is declared. The publisher in <c>Ninlil.Web</c> references both and serializes it.
///     </para>
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the settings section was written.</param>
/// <param name="SectionName">
///     The settings section's stable name (<c>ClusterSyncedSettingsStateEntity.SectionName</c>),
///     for example <c>"EventBroker"</c>.
/// </param>
/// <param name="SettingsJson">
///     The list of configuration leaves that were written, serialized as a JSON array of
///     <c>{ sectionPath, key, value }</c> objects (the shape of <c>PresetConfigurationSetting</c>).
/// </param>
public record ClusterConfigSettingsChangedEvent(
    DateTimeOffset OccurredAtUtc,
    string SectionName,
    string SettingsJson
) : IDnsServerEvent;
