using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Enum;

/// <summary>
///     This enumeration says how parsing a command line into one input model ended. It is the
///     <see cref="Model.ConsoleParseResult.Outcome" /> that
///     <see cref="Model.ConsoleCommandArgumentCollection.GetParseResult(System.Type)" /> reports in
///     place of printing help or ending the process, so the host decides what to show and which
///     exit code to return.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ConsoleParseOutcomeEnum>))]
public enum ConsoleParseOutcomeEnum
{
    /// <summary>
    ///     This value means the model matched, every argument converted and the model was created.
    /// </summary>
    [JsonStringEnumMemberName("parsed")]
    [XmlEnum("parsed")]
    [YamlMember(Alias = "parsed")]
    Parsed = 0,

    /// <summary>
    ///     This value means the command line asked for the help text instead of running the model.
    /// </summary>
    [JsonStringEnumMemberName("helpRequested")]
    [XmlEnum("helpRequested")]
    [YamlMember(Alias = "helpRequested")]
    HelpRequested = 1,

    /// <summary>
    ///     This value means the command line is not meant for this model: the model's verb is not
    ///     the one given, or no tokens were given to a model that needs its verb.
    /// </summary>
    [JsonStringEnumMemberName("notMatched")]
    [XmlEnum("notMatched")]
    [YamlMember(Alias = "notMatched")]
    NotMatched = 2,

    /// <summary>
    ///     This value means the command line is meant for this model but its arguments are wrong:
    ///     a required argument is missing, a value does not convert, or setting a value threw.
    /// </summary>
    [JsonStringEnumMemberName("invalid")]
    [XmlEnum("invalid")]
    [YamlMember(Alias = "invalid")]
    Invalid = 3,
}
