using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Serializer.Attribute;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Enum;

/// <summary>
///     This enum defines our serialization formats.
/// </summary>
[DefaultValue(None)]
[JsonConverter<JsonStringEnumConverter<SerializerFormatEnum>>]
public enum SerializerFormatEnum
{
    /// <summary>
    ///     This enum value defines no format, for
    ///     string passthrough in external libraries.
    /// </summary>
    [Description("none")]
    [JsonStringEnumMemberName("none")]
    [XmlEnum("none")]
    [YamlMember(Alias = "none")]
    None,

    /// <summary>
    ///     This enum value defines JSON format.
    /// </summary>
    [Description("json")]
    [JsonStringEnumMemberName("json")]
    [XmlEnum("json")]
    [YamlMember(Alias = "json")]
    Json,

    /// <summary>
    ///     This enum value defines JSONC format (JSON with comments).
    /// </summary>
    [Description("jsonc")]
    [JsonStringEnumMemberName("jsonc")]
    [XmlEnum("jsonc")]
    [YamlMember(Alias = "jsonc")]
    Jsonc = Json,

    /// <summary>
    ///     This enum value defines XML format.
    /// </summary>
    [Description("xml")]
    [JsonStringEnumMemberName("xml")]
    [XmlEnum("xml")]
    [YamlMember(Alias = "xml")]
    Xml,

    /// <summary>
    ///     This enum value defines YAML format.
    /// </summary>
    [Description("yaml")]
    [JsonStringEnumMemberName("yaml")]
    [XmlEnum("yaml")]
    [YamlMember(Alias = "yaml")]
    Yaml,

    /// <summary>
    ///     This enum value defines YML format.
    /// </summary>
    [Description("yml")]
    [JsonStringEnumMemberName("yml")]
    [XmlEnum("yml")]
    [YamlMember(Alias = "yml")]
    Yml = Yaml,
}
