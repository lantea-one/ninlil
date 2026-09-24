// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This record is the wire-safe shape one configuration leaf takes inside a settings-section
///     config-sync envelope's own <c>PayloadJson</c> -- a list of these is what actually gets
///     serialized and sent, not <c>Server.Presets</c>'s own <c>PresetConfigurationSetting</c>
///     directly.
///     <para>
///         <b>Why not just serialize <c>PresetConfigurationSetting</c> itself</b>: that record's
///         own <c>Value</c> is declared as <see langword="object" />, so a genuinely-typed value
///         (a <see langword="bool" />, an <see langword="int" />) serializes correctly on the way
///         out, but <see cref="System.Text.Json.JsonSerializer" /> deserializes an
///         <see langword="object" />-typed member back as a boxed
///         <see cref="System.Text.Json.JsonElement" />, not the original CLR type --
///         <c>PresetConfigurationFileApplier.ToJsonNode</c> was never written to handle that case,
///         since every existing caller only ever builds a <c>PresetConfigurationSetting</c> list
///         from live C# code, never round-trips one through JSON first. This record sidesteps the
///         whole question by always carrying <see cref="Value" /> as its plain string form (every
///         existing settings controller already produces one of a <see langword="string" />/
///         <see langword="int" />-turned-string/<c>TimeSpan</c>-turned-string in the first place,
///         per <c>PresetConfigurationSetting.Value</c>'s own doc comment) --
///         <c>PresetConfigurationFileApplier</c>'s own string-valued write path, and .NET's own
///         <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> binder underneath it,
///         both already treat every leaf value as a string internally regardless of the JSON
///         file's own literal token shape, so writing <c>"true"</c> instead of a bare JSON
///         <c>true</c> binds identically on read.
///     </para>
/// </summary>
/// <param name="SectionPath">The configuration section this value belongs under.</param>
/// <param name="Key">The specific property name within that section.</param>
/// <param name="Value">The value's own string form.</param>
public sealed record SettingsSyncEntry(string SectionPath, string Key, string Value);
