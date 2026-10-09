using System.Text.Json;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This record is one configuration value inside the <c>PayloadJson</c> of a settings
///     config-sync envelope. The envelope carries a list of these rather than
///     <c>PresetConfigurationSetting</c> records.
///     <para>
///         <b>Why not <c>PresetConfigurationSetting</c>.</b> Its <c>Value</c> is an
///         <see langword="object" />, which <see cref="System.Text.Json.JsonSerializer" /> cannot
///         read back as the original type. This record carries <see cref="Value" /> as a
///         <see cref="JsonElement" /> instead, so a list stays a JSON array, a number stays a
///         number in invariant form (never a locale-formatted <c>2,5</c>), a boolean stays a
///         boolean, a <see cref="System.TimeSpan" /> stays its string and <see langword="null" />
///         stays a JSON <c>null</c>. <c>PresetConfigurationFileApplier</c> writes the element
///         into the peer's file as it is.
///     </para>
///     <para>
///         A peer that sends every value as a string still applies: the string is a
///         <see cref="JsonValueKind.String" /> element and the configuration binder reads it as
///         it reads any text leaf. A peer that cannot read arrays or numbers in this field drops
///         the envelope as malformed.
///     </para>
/// </summary>
/// <param name="SectionPath">The configuration section the value belongs under.</param>
/// <param name="Key">The property name within that section.</param>
/// <param name="Value">The value as JSON, serialized from the original object.</param>
public sealed record SettingsSyncEntry(string SectionPath, string Key, JsonElement Value);
