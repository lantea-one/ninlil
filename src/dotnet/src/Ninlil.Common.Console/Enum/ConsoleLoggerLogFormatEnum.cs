using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Enum;

/// <summary>
///     This enumeration lists the output formats <see cref="ConsoleLogger" /> can write log
///     entries in. The lowercase names are what configuration and serialized output use.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsoleLoggerLogFormatEnum>))]
public enum ConsoleLoggerLogFormatEnum
{
    /// <summary>
    ///     Represents the plain text log format.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    [XmlEnum("text")]
    [YamlMember(Alias = "text")]
    Text = 0,

    /// <summary>
    ///     Represents the JSON log format.
    /// </summary>
    [JsonStringEnumMemberName("json")]
    [XmlEnum("json")]
    [YamlMember(Alias = "json")]
    Json = 1,

    /// <summary>
    ///     Represents the table log format.
    /// </summary>
    [JsonStringEnumMemberName("table")]
    [XmlEnum("table")]
    [YamlMember(Alias = "table")]
    Table = 2,

    /// <summary>
    ///     Represents the XML log format.
    /// </summary>
    [JsonStringEnumMemberName("xml")]
    [XmlEnum("xml")]
    [YamlMember(Alias = "xml")]
    Xml = 3,

    /// <summary>
    ///     Represents the YAML log format.
    /// </summary>
    [JsonStringEnumMemberName("yaml")]
    [XmlEnum("yaml")]
    [YamlMember(Alias = "yaml")]
    Yaml = 4,

    /// <summary>
    ///     Represents the YAML log format (alias for Yaml).
    /// </summary>
    [JsonStringEnumMemberName("yml")]
    [XmlEnum("yml")]
    [YamlMember(Alias = "yml")]
    Yml = 5,
}
