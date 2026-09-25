// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record is one configuration value that <see cref="PresetConfigurationFileApplier" />
///     writes: one key, under one colon-delimited section path, set to one value. Presets and
///     the runtime settings controllers in <c>Ninlil.Web</c> both use it.
///     <see cref="SectionPath" /> and <see cref="Key" /> are plain strings that must match the
///     section names and property names of the options classes they target
///     (<c>DnsRateLimitOptions.SectionName</c>, <c>DnsCookieOptions.SectionName</c>, and so on),
///     so this project needs no reference to those classes, just as a hand-edited
///     <c>appsettings.json</c> needs none.
/// </summary>
/// <param name="SectionPath">
///     The configuration section this value belongs under, for example
///     <c>"DnsServer:RateLimiting"</c>.
/// </param>
/// <param name="Key">The specific property name within that section, for example <c>"Enable"</c>.</param>
/// <param name="Value">
///     The value to set. A string or <see langword="bool" /> is written as a JSON scalar, any
///     other enumerable as a JSON array, and anything else (numbers, objects) through
///     <see cref="System.Text.Json.JsonSerializer" />. The result must bind through
///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" />; an enum is best passed
///     as its name, including a comma-separated flags combination such as
///     <c>"Tls12, Tls13"</c>.
/// </param>
public sealed record PresetConfigurationSetting(string SectionPath, string Key, object Value);
