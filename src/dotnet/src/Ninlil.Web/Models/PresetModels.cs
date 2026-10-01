// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record summarizes one built-in <see cref="Ninlil.Dns.Server.Presets.PresetDefinition" />
///     for <c>GET /api/presets</c>. It leaves out the individual settings, since a preset is
///     applied as a whole, not inspected field by field.
/// </summary>
/// <param name="Name">The preset's stable name, for example <c>"Baseline"</c>.</param>
/// <param name="Description">A short summary of what the preset changes and why.</param>
/// <param name="ConfigurationSettingCount">How many individual configuration values the preset overwrites.</param>
/// <param name="FilterListSubscriptionCount">How many block or allow list subscriptions the preset adds.</param>
public record PresetSummary(
    string Name,
    string Description,
    int ConfigurationSettingCount,
    int FilterListSubscriptionCount
);

/// <summary>
///     This record is returned after a preset is applied (<c>POST /api/presets/{name}/apply</c>).
/// </summary>
/// <param name="Name">The preset that was applied.</param>
/// <param name="AppliedAtUtc">When the preset was applied.</param>
/// <param name="Note">
///     A reminder that settings read through
///     <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}" /> take effect at once,
///     while the others the preset touched need a restart.
/// </param>
public record PresetApplyResult(string Name, DateTimeOffset AppliedAtUtc, string Note);
