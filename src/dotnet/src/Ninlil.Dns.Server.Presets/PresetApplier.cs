// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class is the single entry point for bulk-applying a <see cref="PresetDefinition" />:
///     both its own <see cref="PresetConfigurationFileApplier" /> (JSON-config-rewrite) half and
///     its own <see cref="PresetFilterListApplier" /> (block/allow-list subscription) half. This
///     class carries no invocation surface of its own -- no CLI, no config flag -- it is a plain
///     library class other callers invoke directly; <c>Ninlil.Web</c>'s own
///     <c>PresetsController</c> is the one REST-facing caller today.
/// </summary>
/// <param name="filterListApplier">The applier used for a preset's own filter-list subscriptions.</param>
public sealed class PresetApplier(PresetFilterListApplier filterListApplier)
{
    /// <summary>
    ///     This method applies <paramref name="preset" />'s configuration settings to
    ///     <paramref name="configurationFilePath" />, then its filter-list subscriptions (if
    ///     any) via this class's own injected <see cref="PresetFilterListApplier" />. The two halves are applied
    ///     sequentially, not as a single all-or-nothing transaction -- a failure partway through
    ///     is safely recoverable by simply re-applying the same preset again, since both halves
    ///     are themselves idempotent (the config-file merge always overwrites the same leaves the
    ///     same way, and <see cref="PresetFilterListApplier" /> matches by each subscription's
    ///     own stable name).
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
