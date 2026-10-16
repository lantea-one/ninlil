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
///     missing arguments, and calls <see cref="TryGetParsedValue" /> to convert the tokens (a value
///     that does not convert is a parse error) or <see cref="SetValue" /> to write a default when
///     nothing matched.
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
    ///     a new instance of its type; and with no attribute default the property keeps the value
    ///     it has on a new instance of the model (see <see cref="GetInitializerValue" />).
    /// </summary>
    /// <returns>The fallback value for the property.</returns>
    private object GetDefaultValue() =>
        Required
            ? null
            : Attribute switch
            {
                NamedConsoleArgumentAttribute namedAttribute => ConvertAttributeDefault(
                    namedAttribute.Default,
                    Property.PropertyType
                ) ?? GetInitializerValue(),

                // A nested verb falls back to a new instance of its type.
                NestedConsoleVerbAttribute => Property.PropertyType.CreateInstance()
                    ?? Property.PropertyType.GetDefaultValue(),

                PositionalConsoleArgumentAttribute positionalAttribute => ConvertAttributeDefault(
                    positionalAttribute.Default,
                    Property.PropertyType
                ) ?? GetInitializerValue(),

                // Any other attribute falls back to the property's initial value.
                _ => GetInitializerValue(),
            };

    /// <summary>
    ///     This method converts an attribute default to the type of the property it is written to.
    ///     A string default on a non-string property is converted with the same rules as a typed
    ///     command-line value; every other default, and a string that does not convert, is returned
    ///     as given. <see cref="GetDefaultValue" /> and <see cref="ConsoleCommandInputModel.SetDefaults" />
    ///     share it, so both treat <c>Default = "7"</c> on an <see cref="int" /> property alike.
    /// </summary>
    /// <param name="attributeDefault">The default declared on the attribute, or <see langword="null" />.</param>
    /// <param name="propertyType">The type of the property the default is written to.</param>
    /// <returns>The converted default, the default as given, or <see langword="null" />.</returns>
    internal static object ConvertAttributeDefault(object attributeDefault, Type propertyType) =>
        attributeDefault is string text
        && propertyType != typeof(string)
        && text.TryGetTypedValue(propertyType, out object converted)
            ? converted
            : attributeDefault;

    /// <summary>
    ///     This method returns the value the property has on a new instance of the model that
    ///     declares it, so a property initializer (<c>int Port { get; set; } = 53</c>) is the
    ///     fallback for an argument that was not given. A model that cannot be created without
    ///     arguments falls back to the property type's default.
    /// </summary>
    /// <returns>The initial value of the property, or the type default.</returns>
    private object GetInitializerValue()
    {
        try
        {
            // The model type is the type the property was reflected from, which can be a derived
            // type when the property is inherited. The property type itself is the wrong object.
            object model = Property.ReflectedType?.CreateInstance();
            return model is null
                ? Property.PropertyType.GetDefaultValue()
                : Property.GetValue(model);
        }
        catch (Exception)
        {
            // A constructor or getter that throws leaves nothing to read.
            return Property.PropertyType.GetDefaultValue();
        }
    }

    /// <summary>
    ///     This method converts the tokens matched by a named argument to the property type and
    ///     returns the value to use: the converted value, or <see cref="GetDefaultValue" /> when
    ///     nothing matched or conversion fails.
    /// </summary>
    /// <returns>The converted value, or the fallback value.</returns>
    private object GetParsedNamedValue() =>
        Found && TryConvertNamedValue(out object value) ? value : GetDefaultValue();

    /// <summary>
    ///     This method converts the tokens matched by a named argument to the property type. One
    ///     token is converted directly. Several tokens (a repeated argument) are joined with commas,
    ///     wrapped in <c>{}</c> for a dictionary or <c>()</c> for a tuple, and converted as one value.
    ///     It expects at least one matched token.
    /// </summary>
    /// <param name="value">The converted value, or <see langword="null" /> when conversion fails.</param>
    /// <returns><see langword="true" /> when the tokens converted; otherwise <see langword="false" />.</returns>
    private bool TryConvertNamedValue(out object value)
    {
        // A single token converts on its own.
        if (Arguments.Count is 1)
        {
            value = null;
            return Arguments[0].Value?.TryGetTypedValue(Property.PropertyType, out value) is true;
        }

        // Localize our value string.
        string joined = string.Join(
            ",",
            Arguments.Select(a =>
                Property.PropertyType.IsDictionary() ? a.Value?.Trim().TrimStart('{').TrimEnd('}')
                : Property.PropertyType.IsTuple() ? a.Value?.Trim().TrimStart('(').TrimEnd(')')
                : a.Value?.Trim()
            )
        );

        // Check for a dictionary, then update the value string with the proper notation.
        if (Property.PropertyType.IsDictionary())
            joined = $"{{{joined}}}";
        // Check for a tuple. then update the value string with the proper notation.
        else if (Property.PropertyType.IsTuple())
            joined = $"({joined})";

        // Convert the joined value string to the type of the property.
        return joined.TryGetTypedValue(Property.PropertyType, out value);
    }

    /// <summary>
    ///     This method parses a nested verb. The tokens after the verb token are parsed into a new
    ///     instance of the property type. When the verb was not given it returns
    ///     <see cref="GetDefaultValue" />.
    /// </summary>
    /// <param name="argumentCollection">The full token collection being parsed.</param>
    /// <returns>The parsed nested input model, or the fallback value.</returns>
    /// <exception cref="ConsoleParseException">When the tokens after the verb do not parse.</exception>
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
        Found && TryConvertPositionalValue(out object value) ? value : GetDefaultValue();

    /// <summary>
    ///     This method converts the first token matched by a positional argument to the property
    ///     type. It expects at least one matched token.
    /// </summary>
    /// <param name="value">The converted value, or <see langword="null" /> when conversion fails.</param>
    /// <returns><see langword="true" /> when the token converted; otherwise <see langword="false" />.</returns>
    private bool TryConvertPositionalValue(out object value)
    {
        value = null;
        return Arguments.FirstOrDefault()?.Value?.TryGetTypedValue(Property.PropertyType, out value)
            is true;
    }

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
    ///     This method converts the matched tokens to the property type like
    ///     <see cref="GetParsedValue(ConsoleCommandArgumentCollection)" />, and also says whether a
    ///     token the user typed failed to convert. The parser uses it so a value such as
    ///     <c>--age abc</c> is reported as an error instead of being replaced by the default.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The full token collection being parsed. Only a nested verb uses it.
    /// </param>
    /// <param name="value">
    ///     The converted value; when conversion fails or nothing matched, the fallback value
    ///     <see cref="GetParsedValue(ConsoleCommandArgumentCollection)" /> gives.
    /// </param>
    /// <returns>
    ///     <see langword="false" /> when at least one matched token does not convert; otherwise
    ///     <see langword="true" />, including when nothing matched and for a nested verb.
    /// </returns>
    /// <exception cref="ConsoleParseException">
    ///     For a nested verb that was given, when the tokens after it do not parse.
    /// </exception>
    public bool TryGetParsedValue(
        ConsoleCommandArgumentCollection argumentCollection,
        out object value
    )
    {
        // Only a named or positional argument with matched tokens can fail to convert.
        bool converted;
        if (Found && Attribute is NamedConsoleArgumentAttribute)
            converted = TryConvertNamedValue(out value);
        else if (Found && Attribute is PositionalConsoleArgumentAttribute)
            converted = TryConvertPositionalValue(out value);
        else
        {
            // Nothing typed, or a nested verb: there is no user value to reject.
            value = GetParsedValue(argumentCollection);
            return true;
        }

        // A failed conversion still hands back the fallback value, the same one GetParsedValue gives.
        if (!converted)
            value = GetDefaultValue();

        // We're done, report whether every matched token converted.
        return converted;
    }

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
