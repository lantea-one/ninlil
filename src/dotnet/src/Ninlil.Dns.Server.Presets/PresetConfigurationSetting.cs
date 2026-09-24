// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This record represents a single configuration leaf a preset overwrites: one key, at one
///     colon-delimited section path, set to one value. <see cref="SectionPath" /> and
///     <see cref="Key" /> are deliberately plain strings, matching the exact section-name
///     constants and property names already declared by each settings class this project does
///     not otherwise reference (<c>DnsRateLimitOptions.SectionName</c>,
///     <c>DnsCookieOptions.SectionName</c>, and so on) -- the same "no compiled dependency needed
///     to write configuration that matches another project's binding shape" relationship an
///     operator hand-editing <c>appsettings.json</c> already has with every one of those classes.
/// </summary>
/// <param name="SectionPath">
///     The configuration section this value belongs under, for example
///     <c>"DnsServer:RateLimiting"</c>.
/// </param>
/// <param name="Key">The specific property name within that section, for example <c>"Enable"</c>.</param>
/// <param name="Value">
///     The value to set. Written to the configuration file's own JSON via
///     <see cref="object.ToString" />, so any type whose string form binds correctly through
///     .NET's own <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> conversion
///     (a <see langword="bool" />, a number, a plain string, or an enum's own name -- including a
///     comma-separated flags combination such as <c>"Tls12, Tls13"</c>) is acceptable.
/// </param>
public sealed record PresetConfigurationSetting(string SectionPath, string Key, object Value);
