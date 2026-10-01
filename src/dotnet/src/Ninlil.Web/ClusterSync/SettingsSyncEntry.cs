// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This record is one configuration value inside the <c>PayloadJson</c> of a settings
///     config-sync envelope. The envelope carries a list of these rather than
///     <c>PresetConfigurationSetting</c> records.
///     <para>
///         <b>Why not <c>PresetConfigurationSetting</c>.</b> Its <c>Value</c> is an
///         <see langword="object" />, which <see cref="System.Text.Json.JsonSerializer" /> reads
///         back as a boxed <see cref="System.Text.Json.JsonElement" />, a type
///         <c>PresetConfigurationFileApplier.ToJsonNode</c> does not handle. This record carries
///         <see cref="Value" /> as text instead. The configuration binder reads every leaf as
///         text, so <c>"True"</c> or <c>"00:05:00"</c> binds the same as the JSON
///         <see langword="true" /> or the original <c>TimeSpan</c> string would. A list value does
///         not survive this: <see cref="object.ToString" /> of a list is its type name, not its
///         items.
///     </para>
/// </summary>
/// <param name="SectionPath">The configuration section the value belongs under.</param>
/// <param name="Key">The property name within that section.</param>
/// <param name="Value">The value as text, from <see cref="object.ToString" />.</param>
public sealed record SettingsSyncEntry(string SectionPath, string Key, string Value);
