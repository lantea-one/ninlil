// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record is one complete, named preset: every configuration leaf and every filter-list
///     subscription it bulk-applies. Per the confirmed "overwrite-then-editable" semantics,
///     applying a preset overwrites exactly the leaves it names here and nothing else -- a
///     setting this preset doesn't mention (whether in the same configuration section or a
///     different one entirely) is left exactly as the operator last set it.
/// </summary>
/// <param name="Name">This preset's own stable name, for example <c>"Baseline"</c>.</param>
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
