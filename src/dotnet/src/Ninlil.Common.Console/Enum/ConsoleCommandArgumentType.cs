using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Enum;

/// <summary>
///     This enumeration classifies a command-line token or declared argument: a named argument,
///     a positional argument, a verb or a nested verb.
/// </summary>
[DefaultValue(nameof(Unknown))]
[JsonConverter(typeof(JsonStringEnumConverter))]
[XmlType("consoleCommandArgumentType")]
public enum ConsoleCommandArgumentType
{
    /// <summary>
    ///     This value means the kind has not been determined. It is the default.
    /// </summary>
    [JsonStringEnumMemberName("unknown")]
    [XmlEnum("unknown")]
    [YamlMember(Alias = "unknown")]
    Unknown,

    /// <summary>
    ///     This value marks a named argument, given as <c>--name value</c> or <c>-n value</c>.
    /// </summary>
    [JsonStringEnumMemberName("named")]
    [XmlEnum("named")]
    [YamlMember(Alias = "named")]
    Named,

    /// <summary>
    ///     This value marks a nested verb: a sub-command inside a verb, such as <c>backup</c> in
    ///     <c>database backup</c>.
    /// </summary>
    [JsonStringEnumMemberName("nestedVerb")]
    [XmlEnum("nestedVerb")]
    [YamlMember(Alias = "nestedVerb")]
    NestedVerb,

    /// <summary>
    ///     This value marks a positional argument, identified by where it appears rather than by a
    ///     name, such as the two file names in <c>copy source.txt destination.txt</c>.
    /// </summary>
    [JsonStringEnumMemberName("positional")]
    [XmlEnum("positional")]
    [YamlMember(Alias = "positional")]
    Positional,

    /// <summary>
    ///     This value marks a verb: the command the user wants to run, such as <c>add</c> in
    ///     <c>todo add</c>.
    /// </summary>
    [JsonStringEnumMemberName("verb")]
    [XmlEnum("verb")]
    [YamlMember(Alias = "verb")]
    Verb,
}
