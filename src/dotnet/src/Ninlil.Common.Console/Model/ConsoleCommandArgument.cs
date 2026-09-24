using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Serializer.Enum;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class represents a console command argument, which is used to define the parameters
///     that can be passed to a command-line application when executing a command. It contains
///     properties for the argument's character, name, position, and value, which are used to
///     identify and process the argument when parsing command-line input.
/// </summary>
[XmlRoot("consoleArgument")]
public class ConsoleCommandArgument
{
    /// <summary>
    ///     This property contains the short name of the console argument, which is used as
    ///     a single-character identifier for the argument when parsing command-line input.
    /// </summary>
    [JsonPropertyName("character")]
    [XmlAttribute("character")]
    [YamlMember(Alias = "character")]
    public char Character { get; set; } = char.MinValue;

    /// <summary>
    ///     This property contains the index of the console argument as it appears in the command-line
    ///     input, which is used to determine the order of arguments when parsing command-line input.
    /// </summary>
    [JsonPropertyName("index")]
    [XmlAttribute("index")]
    [YamlMember(Alias = "index")]
    public int Index { get; set; }

    /// <summary>
    ///     This property contains the name of the console argument, which
    ///     is used to identify the argument when parsing command-line input.
    /// </summary>
    [JsonPropertyName("name")]
    [XmlAttribute("name")]
    [YamlMember(Alias = "name")]
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the positional index of the console argument, which is
    ///     used to determine the order of arguments when parsing command-line input.
    /// </summary>
    [JsonPropertyName("position")]
    [XmlAttribute("position")]
    [YamlMember(Alias = "position")]
    public int Position { get; set; } = -1;

    /// <summary>
    ///     This property determines the type of the console argument based on its characteristics, such as whether
    ///     it has a character, name, or positional index. The type is determined as follows:
    ///         - If the argument has a positional index greater than -1, it is classified as a positional argument.
    ///         - If the argument has a character that is not the minimum value or a name that is not null or empty,
    ///           it is classified as a named argument.
    ///         - If neither of the above conditions are met, it is classified as a verb argument.
    /// </summary>
    [JsonPropertyName("type")]
    [XmlAttribute("type")]
    [YamlMember(Alias = "type")]
    public ConsoleCommandArgumentType Type { get; set; } = ConsoleCommandArgumentType.Unknown;

    /// <summary>
    ///     This property contains the value of the console argument, which is the
    ///     actual input provided by the user when executing a command-line application.
    /// </summary>
    [JsonPropertyName("value")]
    [XmlText]
    [YamlMember(Alias = "value")]
    public string Value { get; set; }

    /// <summary>
    ///     This method retrieves the value of the console argument as a specific <paramref name="valueType" />,
    ///     which is used to convert the string value of the argument to a more specific data type for further
    ///     processing in the application. The method takes in the desired type of the value, a delimiter for
    ///     parsing collections, and a serializer format for handling complex data structures, and returns the
    ///     value of the argument as an object of the specified type. This allows for more flexible and dynamic
    ///     handling of console arguments in the application, as the value can be easily converted to the
    ///     appropriate type based on the context of the command being executed.
    /// </summary>
    /// <param name="valueType">
    ///     The type to which the value of the console argument should be converted, which is used to convert
    ///     the string value of the argument to a more specific data type for further processing in the application.
    /// </param>
    /// <param name="delimiter">
    ///     The optional delimiter used for parsing collections, which is used to specify the character that separates
    ///     individual values in a collection when converting the string value of the argument to a more specific data
    ///     type. This is particularly useful when the argument represents a list or array of values, allowing for easy
    ///     parsing and conversion of the input into a collection data structure in the application.
    /// </param>
    /// <param name="serializerFormat">
    ///     The optional <see cref="SerializerFormatEnum" /> format used for handling complex data structures, which is
    ///     used to specify the format in which the string value of the argument should be serialized or deserialized
    ///     when converting it to a more specific data type. This is particularly useful when the argument represents
    ///     a complex object or data structure, allowing for flexible handling of the input based on the context of
    ///     the command being executed and the desired format for processing the data in the application.
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
    ///     This method retrieves the value of the console argument as a specific <typeparamref name="TValue" />,
    ///     which is used to convert the string value of the argument to a more specific data type for further
    ///     processing in the application. The method takes in the desired type of the value, a delimiter for
    ///     parsing collections, and a serializer format for handling complex data structures, and returns the
    ///     value of the argument as an object of the specified type. This allows for more flexible and dynamic
    ///     handling of console arguments in the application, as the value can be easily converted to the
    ///     appropriate type based on the context of the command being executed.
    /// </summary>
    /// <param name="delimiter">
    ///     The optional delimiter used for parsing collections, which is used to specify the character that separates
    ///     individual values in a collection when converting the string value of the argument to a more specific data
    ///     type. This is particularly useful when the argument represents a list or array of values, allowing for easy
    ///     parsing and conversion of the input into a collection data structure in the application.
    /// </param>
    /// <param name="serializerFormat">
    ///     The optional <see cref="SerializerFormatEnum" /> format used for handling complex data structures, which is
    ///     used to specify the format in which the string value of the argument should be serialized or deserialized
    ///     when converting it to a more specific data type. This is particularly useful when the argument represents
    ///     a complex object or data structure, allowing for flexible handling of the input based on the context of
    ///     the command being executed and the desired format for processing the data in the application.
    /// </param>
    /// <typeparam name="TValue">
    ///     The type to which the value of the console argument should be converted, which is used to convert
    ///     the string value of the argument to a more specific data type for further processing in the application.
    /// </typeparam>
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
