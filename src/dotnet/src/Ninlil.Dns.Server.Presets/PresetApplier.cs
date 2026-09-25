// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class applies a whole <see cref="PresetDefinition" />: its configuration values
///     through <see cref="PresetConfigurationFileApplier" /> and its filter-list subscriptions
///     through <see cref="PresetFilterListApplier" />. It is a plain library class with no CLI or
///     configuration switch; <c>PresetsController</c> in <c>Ninlil.Web</c> is its only caller.
/// </summary>
/// <param name="filterListApplier">The applier used for a preset's filter-list subscriptions.</param>
public sealed class PresetApplier(PresetFilterListApplier filterListApplier)
{
    /// <summary>
    ///     This method applies <paramref name="preset" />'s configuration settings to
    ///     <paramref name="configurationFilePath" />, then its filter-list subscriptions, if any.
    ///     The two steps run one after the other, not as one transaction. If the second fails,
    ///     applying the same preset again is safe: the file merge writes the same values again,
    ///     and <see cref="PresetFilterListApplier" /> matches lists by name.
    /// </summary>
    /// <param name="preset">The preset to apply.</param>
    /// <param name="configurationFilePath">The JSON configuration file this preset's settings should be merged into.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ApplyAsync(
        PresetDefinition preset,
        string configurationFilePath,
        CancellationToken stoppingToken = default
    )
    {
        await PresetConfigurationFileApplier.ApplyAsync(preset, configurationFilePath);
        await filterListApplier.ApplyAsync(preset, stoppingToken);
    }
}
