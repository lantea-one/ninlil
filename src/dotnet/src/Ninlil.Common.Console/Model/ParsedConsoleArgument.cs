using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Type;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class pairs one argument declared on an input model (its attribute and property) with
///     the command-line tokens that matched it. <see cref="ConsoleCommandArgumentCollection" />
///     builds these while parsing, uses <see cref="Found" /> and <see cref="Required" /> to report
///     missing arguments, and calls <see cref="SetValue" /> to convert the tokens and write the
///     result to the model, falling back to the attribute's default when nothing matched.
/// </summary>
public class ParsedConsoleArgument
{
    /// <summary>
    ///     This property contains the collection of CLI arguments associated with the <see cref="Attribute" />.
    /// </summary>
    [JsonPropertyName("arguments")]
    [XmlElement("arguments")]
    [YamlMember(Alias = "arguments")]
    public ConsoleCommandArgumentCollection Arguments { get; set; } = [];

    /// <summary>
    ///     This property contains the named, positional or nested-verb attribute that declares the
    ///     argument.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public ConsoleArgumentAttribute Attribute { get; set; }

    /// <summary>
    ///     This property contains a readable form of the <see cref="Attribute" /> for serialized output.
    /// </summary>
    [JsonPropertyName("attribute")]
    [XmlAttribute("attribute")]
    [YamlMember(Alias = "attribute")]
    public string AttributeString =>
        Attribute is null
            ? null
            : $"{Attribute.GetType().Name}[{Attribute switch
            {
                // A named attribute shows its short and/or long name.
                NamedConsoleArgumentAttribute a => a.ShortName is not null && a.Name?.Trim() is not (null or { Length: 0 })
                    ? $"-{a.ShortName}, --{a.Name.Trim()}" : a.ShortName is null ? $"--{a.Name}" : $"-{a.ShortName}",

                // If we have a nested verb attribute, return its name.
                NestedConsoleVerbAttribute a => a.Name,

                // If we have a positional attribute, return its position in brackets.
                PositionalConsoleArgumentAttribute a => $"[{a.Position}]",

                // Otherwise, we don't have a valid attribute, so return null.
                _ => "null"
            }}]";

    /// <summary>
    ///     This property contains the input model property the argument's value is written to.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public PropertyInfo Property { get; set; }

    /// <summary>
    ///     This property contains a readable form of the <see cref="Property" /> for serialized output.
    /// </summary>
    [JsonPropertyName("property")]
    [XmlAttribute("property")]
    [YamlMember(Alias = "property")]
    public string PropertyString =>
        Property is null ? null : $"{Property.DeclaringType?.Name}.{Property.Name}";

    /// <summary>
    ///     This property indicates whether any command-line token matched the argument.
    /// </summary>
    [JsonPropertyName("found")]
    [XmlAttribute("found")]
    [YamlMember(Alias = "found")]
    public bool Found => Arguments?.Count > 0;

    /// <summary>
    ///     This property indicates whether the named or positional attribute marks the argument as
    ///     required.
    /// </summary>
    [JsonPropertyName("required")]
    [XmlAttribute("required")]
    [YamlMember(Alias = "required")]
    public bool Required =>
        Attribute
            is NamedConsoleArgumentAttribute { Required: true }
                or PositionalConsoleArgumentAttribute { Required: true };

    /// <summary>
    ///     This method returns the value to use when the argument was not given or could not be
    ///     converted. A required argument gets <see langword="null" />. Otherwise the attribute's
    ///     default is used, converted to the property type when it is a string; a nested verb gets
    ///     a new instance of its type; and with no attribute default the property type's default is
    ///     used.
    /// </summary>
    /// <returns>The fallback value for the property.</returns>
    private object GetDefaultValue() =>
        Required
            ? null
            : Attribute switch
            {
                // A string default on a non-string property is converted to the property type.
                NamedConsoleArgumentAttribute namedAttribute
                    when (
                        namedAttribute.Default?.GetType() == typeof(string)
                        && Property.PropertyType != typeof(string)
                        && namedAttribute
                            .Default?.ToString()
                            .TryGetTypedValue(Property.PropertyType, out object namedValue)
                            is true
                    ) => namedValue,
                NamedConsoleArgumentAttribute namedAttribute => namedAttribute.Default
                    ?? (
                        (Property.Attributes & PropertyAttributes.HasDefault) != 0
                            ? Property.GetValue(Property.PropertyType.CreateInstance())
                            : Property.PropertyType.GetDefaultValue()
                    ),

                // A nested verb falls back to a new instance of its type.
                NestedConsoleVerbAttribute => Property.PropertyType.CreateInstance()
                    ?? Property.PropertyType.GetDefaultValue(),

                // A string default on a non-string property is converted to the property type.
                PositionalConsoleArgumentAttribute positionalAttribute
                    when (
                        positionalAttribute.Default?.GetType() == typeof(string)
                        && Property.PropertyType != typeof(string)
                        && positionalAttribute
                            .Default?.ToString()
                            .TryGetTypedValue(Property.PropertyType, out object positionalValue)
                            is true
                    ) => positionalValue,
                PositionalConsoleArgumentAttribute positionalAttribute =>
                    positionalAttribute.Default
                        ?? (
                            (Property.Attributes & PropertyAttributes.HasDefault) != 0
                                ? Property.GetValue(Property.PropertyType.CreateInstance())
                                : Property.PropertyType.GetDefaultValue()
                        ),

                // Any other attribute falls back to the property type's default.
                _ => (Property.Attributes & PropertyAttributes.HasDefault) != 0
                    ? Property.GetValue(Property.PropertyType.CreateInstance())
                    : Property.PropertyType.GetDefaultValue(),
            };

    /// <summary>
    ///     This method converts the tokens matched by a named argument to the property type. One
    ///     token is converted directly. Several tokens (a repeated argument) are joined with commas,
    ///     wrapped in <c>{}</c> for a dictionary or <c>()</c> for a tuple, and converted as one value.
    ///     When nothing matched or conversion fails it returns <see cref="GetDefaultValue" />.
    /// </summary>
    /// <returns>The converted value, or the fallback value.</returns>
    private object GetParsedNamedValue()
    {
        // Without a matching token, use the fallback value.
        if (!Found)
            return GetDefaultValue();

        // Localize our parsed value for reuse.
        object parsedValue;

        // Check for a singular argument value and attempt to parse it to
        // the type of the property. If successful, return the parsed value.
        if (Arguments.Count is 1)
            return
                Arguments[0].Value?.TryGetTypedValue(Property.PropertyType, out parsedValue) is true
                ? parsedValue
                : GetDefaultValue();

        // Localize our value string.
        string value = string.Join(
            ",",
            Arguments.Select(a =>
                Property.PropertyType.IsDictionary() ? a.Value?.Trim().TrimStart('{').TrimEnd('}')
                : Property.PropertyType.IsTuple() ? a.Value?.Trim().TrimStart('(').TrimEnd(')')
                : a.Value?.Trim()
            )
        );

        // Check for a dictionary, then update the value string with the proper notation.
        if (Property.PropertyType.IsDictionary())
            value = $"{{{value}}}";
        // Check for a tuple. then update the value string with the proper notation.
        else if (Property.PropertyType.IsTuple())
            value = $"({value})";

        // Attempt to parse the value string to the type of the property. If
        // successful, return the parsed value, otherwise return the default value.
        return value.TryGetTypedValue(Property.PropertyType, out parsedValue)
            ? parsedValue
            : GetDefaultValue();
    }

    /// <summary>
    ///     This method parses a nested verb. The tokens after the verb token are parsed into a new
    ///     instance of the property type. When the verb was not given it returns
    ///     <see cref="GetDefaultValue" />.
    /// </summary>
    /// <param name="argumentCollection">The full token collection being parsed.</param>
    /// <returns>The parsed nested input model, or the fallback value.</returns>
    private object GetParsedNestedVerbValue(ConsoleCommandArgumentCollection argumentCollection) =>
        Found
            ? new ConsoleCommandArgumentCollection(
                argumentCollection?.Where(a => a.Index > Arguments[0].Index) ?? []
            ).Parse(Property.PropertyType)
            : GetDefaultValue();

    /// <summary>
    ///     This method converts the token matched by a positional argument to the property type.
    ///     When nothing matched or conversion fails it returns <see cref="GetDefaultValue" />.
    /// </summary>
    /// <returns>The converted value, or the fallback value.</returns>
    private object GetParsedPositionalValue() =>
        Found
        && Arguments
            .FirstOrDefault()
            ?.Value?.TryGetTypedValue(Property.PropertyType, out object value)
            is true
            ? value
            : GetDefaultValue();

    /// <summary>
    ///     This method returns the <see cref="Attribute" /> as <typeparamref name="TAttribute" />, or
    ///     <see langword="null" /> when it is a different kind.
    /// </summary>
    /// <typeparam name="TAttribute">
    ///     The type of the <see cref="ConsoleArgumentAttribute" /> to retrieve, which must be a subclass of
    ///     <see cref="ConsoleArgumentAttribute" />.
    /// </typeparam>
    /// <returns>
    ///     The <see cref="Attribute" /> as <typeparamref name="TAttribute" />.
    /// </returns>
    public TAttribute GetAttribute<TAttribute>()
        where TAttribute : ConsoleArgumentAttribute => Attribute as TAttribute;

    /// <summary>
    ///     This method converts the matched tokens to the property type, choosing the conversion by
    ///     the kind of <see cref="Attribute" />.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The full token collection being parsed. Only a nested verb uses it.
    /// </param>
    /// <returns>
    ///     The converted value, the fallback value, or <see langword="null" /> for an unknown
    ///     attribute kind.
    /// </returns>
    public object GetParsedValue(ConsoleCommandArgumentCollection argumentCollection = null) =>
        Attribute switch
        {
            // If we have a named attribute, get the parsed named value.
            NamedConsoleArgumentAttribute => GetParsedNamedValue(),

            // If we have a nested verb attribute, get the parsed nested verb value.
            NestedConsoleVerbAttribute => GetParsedNestedVerbValue(argumentCollection ?? []),

            // If we have a positional attribute, get the parsed positional value.
            PositionalConsoleArgumentAttribute => GetParsedPositionalValue(),

            // Otherwise, we don't have a valid attribute, so return null.
            _ => null,
        };

    /// <summary>
    ///     This method converts the matched tokens to the property type and casts the result to
    ///     <typeparamref name="TValue" />.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The full token collection being parsed. Only a nested verb uses it.
    /// </param>
    /// <typeparam name="TValue">The type to cast the converted value to.</typeparam>
    /// <returns>The converted value as <typeparamref name="TValue" />.</returns>
    public TValue GetParsedValue<TValue>(
        ConsoleCommandArgumentCollection argumentCollection = null
    ) => (TValue)GetParsedValue(argumentCollection);

    /// <summary>
    ///     This method converts the matched tokens and writes the result to <see cref="Property" />
    ///     on <paramref name="inputModel" />.
    /// </summary>
    /// <param name="inputModel">The input model instance to write to.</param>
    /// <param name="argumentCollection">
    ///     The full token collection being parsed, passed to
    ///     <see cref="GetParsedValue(ConsoleCommandArgumentCollection)" /> so a nested verb can parse
    ///     the tokens that follow it. Without it a nested verb always gets its default.
    /// </param>
    public void SetValue(
        object inputModel,
        ConsoleCommandArgumentCollection argumentCollection = null
    ) => Property?.SetValue(inputModel, GetParsedValue(argumentCollection));
}
