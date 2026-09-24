// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of one built-in <see cref="Ninlil.Dns.Server.Presets.PresetDefinition" />
///     -- summary only, not the full leaf-by-leaf settings list, since a preset is applied as one
///     atomic action, never inspected field-by-field through this endpoint.
/// </summary>
/// <param name="Name">The preset's own stable name, for example <c>"Baseline"</c>.</param>
/// <param name="Description">A short, human-readable summary of what this preset changes and why.</param>
/// <param name="ConfigurationSettingCount">How many individual configuration leaves this preset overwrites.</param>
/// <param name="FilterListSubscriptionCount">How many block/allow-list subscriptions this preset attaches.</param>
public record PresetSummary(
    string Name,
    string Description,
    int ConfigurationSettingCount,
    int FilterListSubscriptionCount
);

/// <summary>
///     This record is returned after a preset is successfully applied.
/// </summary>
/// <param name="Name">The preset that was applied.</param>
/// <param name="AppliedAtUtc">When the preset was applied.</param>
/// <param name="Note">
///     A reminder that some settings this preset touches are only read via
///     <see cref="Microsoft.Extensions.Options.IOptions{TOptions}" />, not
///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" />, and therefore need a
///     restart before they take effect.
/// </param>
public record PresetApplyResult(string Name, DateTimeOffset AppliedAtUtc, string Note);
