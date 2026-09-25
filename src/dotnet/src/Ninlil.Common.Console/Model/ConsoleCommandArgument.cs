using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Serializer.Enum;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is one token of a command line, produced by
///     <see cref="ConsoleCommandArgumentCollection" />. A named token has a short
///     <see cref="Character" /> or a long <see cref="Name" /> and a <see cref="Value" />; a
///     positional token has a <see cref="Position" /> and a <see cref="Value" />.
/// </summary>
[XmlRoot("consoleArgument")]
public class ConsoleCommandArgument
{
    /// <summary>
    ///     This property contains the short name of a <c>-x</c> token, or <see cref="char.MinValue" />
    ///     when there is none.
    /// </summary>
    [JsonPropertyName("character")]
    [XmlAttribute("character")]
    [YamlMember(Alias = "character")]
    public char Character { get; set; } = char.MinValue;

    /// <summary>
    ///     This property contains the token's index in the raw argument list.
    /// </summary>
    [JsonPropertyName("index")]
    [XmlAttribute("index")]
    [YamlMember(Alias = "index")]
    public int Index { get; set; }

    /// <summary>
    ///     This property contains the name of a <c>--name</c> token, or <see langword="null" /> for a
    ///     short or positional token.
    /// </summary>
    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the position of a positional token (its raw index), or -1 for a
    ///     named token.
    /// </summary>
    [JsonPropertyName("position")]
    [XmlAttribute("position")]
    [YamlMember(Alias = "position")]
    public int Position { get; set; } = -1;

    /// <summary>
    ///     This property contains the kind of token. The tokenizer sets it to
    ///     <see cref="ConsoleCommandArgumentType.Named" /> or
    ///     <see cref="ConsoleCommandArgumentType.Positional" />.
    /// </summary>
    [JsonPropertyName("type")]
    [XmlAttribute("type")]
    [YamlMember(Alias = "type")]
    public ConsoleCommandArgumentType Type { get; set; } = ConsoleCommandArgumentType.Unknown;

    /// <summary>
    ///     This property contains the token's value as typed. A switch with no value gets
    ///     <c>true</c>.
    /// </summary>
    [JsonPropertyName("value")]
    [XmlText]
    [YamlMember(Alias = "value")]
    public string Value { get; set; }

    /// <summary>
    ///     This method converts <see cref="Value" /> to <paramref name="valueType" />.
    /// </summary>
    /// <param name="valueType">The type to convert the value to.</param>
    /// <param name="delimiter">The separator between items when the target is a collection.</param>
    /// <param name="serializerFormat">
    ///     The serializer format to use when the value holds a serialized object.
    /// </param>
    /// <returns>
    ///     The <see cref="Value" /> typed as <paramref name="valueType" />.
    /// </returns>
    public object GetValue(
        System.Type valueType,
        string delimiter = ",",
        SerializerFormatEnum serializerFormat = SerializerFormatEnum.None
    ) => Value.GetTypedValue(valueType, delimiter, serializerFormat);

    /// <summary>
    ///     This method converts <see cref="Value" /> to <typeparamref name="TValue" />.
    /// </summary>
    /// <param name="delimiter">The separator between items when the target is a collection.</param>
    /// <param name="serializerFormat">
    ///     The serializer format to use when the value holds a serialized object.
    /// </param>
    /// <typeparam name="TValue">The type to convert the value to.</typeparam>
    /// <returns>
    ///     The <see cref="Value" /> typed as <typeparamref name="TValue" />.
    /// </returns>
    public TValue GetValue<TValue>(
        string delimiter = ",",
        SerializerFormatEnum serializerFormat = SerializerFormatEnum.None
    ) => (TValue)GetValue(typeof(TValue), delimiter, serializerFormat);

    /// <summary>
    ///     This method returns a string representation of the console argument, which includes
    ///     the argument's character or name (if the character is not set) and its value.
    ///     If the argument has a positional index, only the value is returned.
    /// </summary>
    /// <returns>The string representation of the console argument.</returns>
    public override string ToString() =>
        Position > -1
            ? Value?.Trim()
            : $"{(Character == char.MinValue ? $"--{Name}" : $"-{Character}")} {Value?.Trim()}";
}
