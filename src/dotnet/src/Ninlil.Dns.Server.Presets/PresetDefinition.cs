// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record is one named preset: the configuration values and filter-list subscriptions
///     it applies (see <see cref="DnsServerPresetCatalog" /> for the built-in ones and
///     <see cref="PresetApplier" /> for how they are applied). Applying a preset overwrites only
///     the values it names; every other setting, in the same section or another, keeps the
///     operator's value, and the operator can edit the preset's values afterwards.
/// </summary>
/// <param name="Name">The preset's stable name, for example <c>"Baseline"</c>.</param>
/// <param name="Description">A short, human-readable summary of what this preset changes and why.</param>
/// <param name="ConfigurationSettings">Every configuration leaf this preset overwrites.</param>
/// <param name="FilterListSubscriptions">
///     Every subscribed block/allow list this preset attaches to the default client group. Empty
///     for every preset except <c>Protective</c> and <c>Family</c>.
/// </param>
public sealed record PresetDefinition(
    string Name,
    string Description,
    IReadOnlyList<PresetConfigurationSetting> ConfigurationSettings,
    IReadOnlyList<PresetFilterListSubscription> FilterListSubscriptions
);
