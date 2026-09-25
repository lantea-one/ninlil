using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Serializer.Attribute;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Enum;

/// <summary>
///     This enumeration lists the serialization formats the serializers and
///     <see cref="Service.SerializerService" /> can pick between. <c>Jsonc</c> and <c>Yml</c> are
///     aliases with the same values as <c>Json</c> and <c>Yaml</c>.
/// </summary>
[DefaultValue(None)]
[JsonConverter<JsonStringEnumConverter<SerializerFormatEnum>>]
public enum SerializerFormatEnum
{
    /// <summary>
    ///     This value means no serialization: callers pass strings through or use
    ///     <see cref="object.ToString" />.
    /// </summary>
    [Description("none")]
    [JsonStringEnumMemberName("none")]
    [XmlEnum("none")]
    [YamlMember(Alias = "none")]
    None,

    /// <summary>
    ///     This value is JSON.
    /// </summary>
    [Description("json")]
    [JsonStringEnumMemberName("json")]
    [XmlEnum("json")]
    [YamlMember(Alias = "json")]
    Json,

    /// <summary>
    ///     This value is JSONC (JSON with comments), an alias of <see cref="Json" />.
    /// </summary>
    [Description("jsonc")]
    [JsonStringEnumMemberName("jsonc")]
    [XmlEnum("jsonc")]
    [YamlMember(Alias = "jsonc")]
    Jsonc = Json,

    /// <summary>
    ///     This value is XML.
    /// </summary>
    [Description("xml")]
    [JsonStringEnumMemberName("xml")]
    [XmlEnum("xml")]
    [YamlMember(Alias = "xml")]
    Xml,

    /// <summary>
    ///     This value is YAML.
    /// </summary>
    [Description("yaml")]
    [JsonStringEnumMemberName("yaml")]
    [XmlEnum("yaml")]
    [YamlMember(Alias = "yaml")]
    Yaml,

    /// <summary>
    ///     This value is an alias of <see cref="Yaml" />.
    /// </summary>
    [Description("yml")]
    [JsonStringEnumMemberName("yml")]
    [XmlEnum("yml")]
    [YamlMember(Alias = "yml")]
    Yml = Yaml,
}
