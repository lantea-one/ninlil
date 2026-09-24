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
///     This class represents a parsed console argument, which is used to store the information about a console
///     argument that has been parsed from command-line input. It contains properties for the collection of CLI
///     arguments associated with the argument, the attribute associated with the argument, and the property
///     information of the associated input model property that corresponds to the CLI argument. This information
///     is used to set the value of the property when parsing command-line input and to provide context about the
///     argument for further processing in the application.
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
    ///     This property contains the <see cref="ConsoleArgumentAttribute" />
    ///     associated with the CLI <see cref="Arguments" />.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public ConsoleArgumentAttribute Attribute { get; set; }

    /// <summary>
    ///     This property contains a string representation of the <see cref="Attribute" />.
    /// </summary>
    [JsonPropertyName("attribute")]
    [XmlAttribute("attribute")]
    [YamlMember(Alias = "attribute")]
    public string AttributeString =>
        Attribute is null
            ? null
            : $"{Attribute.GetType().Name}[{Attribute switch
            {
                // If we have a named attribute, return it's short or full name.
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
    ///     This property contains the property information of the associated input model property that corresponds
    ///     to the CLI argument, which is used to set the value of the property when parsing command-line input.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public PropertyInfo Property { get; set; }

    /// <summary>
    ///     This property contains a string representation of the <see cref="Property" />.
    /// </summary>
    [JsonPropertyName("property")]
    [XmlAttribute("property")]
    [YamlMember(Alias = "property")]
    public string PropertyString =>
        Property is null ? null : $"{Property.DeclaringType?.Name}.{Property.Name}";

    /// <summary>
    ///     This field indicates whether the console argument was successfully found and parsed from the
    ///     command-line input, which is used to determine whether the value of the property should be set
    ///     when parsing command-line input and to provide context about the argument for further processing
    ///     in the application.
    /// </summary>
    [JsonPropertyName("found")]
    [XmlAttribute("found")]
    [YamlMember(Alias = "found")]
    public bool Found => Arguments?.Count > 0;

    /// <summary>
    ///     This property indicates whether the console argument is required based on the associated attribute, which
    ///     is used to determine whether the value of the property must be set when parsing command-line input and to
    ///     provide context about the argument for further processing in the application.
    /// </summary>
    [JsonPropertyName("required")]
    [XmlAttribute("required")]
    [YamlMember(Alias = "required")]
    public bool Required =>
        Attribute
            is NamedConsoleArgumentAttribute { Required: true }
                or PositionalConsoleArgumentAttribute { Required: true };

    /// <summary>
    ///     This method retrieves the default value of the console argument based on the associated attribute and
    ///     the context of the argument, which is used to provide a default value for the property when parsing
    ///     command-line input if the argument is not found or cannot be parsed. The method checks the required
    ///     state of the argument and returns null if it's required. If the argument is not required, it determines
    ///     the default value based on the type of the associated attribute and the context of the argument. For a
    ///     named argument with a string default value, it attempts to parse the default value to the type of the
    ///     property. For a nested verb, it creates an instance of the property type or retrieves the default value
    ///     from the type itself. For a positional argument with a string default value, it also attempts to parse
    ///     the default value to the type of the property. If none of these conditions are met, it checks for a
    ///     default value property attribute and attempts to retrieve the default value from the property, or simply
    ///     returns the default value from the type itself if there is no default value property attribute. This
    ///     allows for flexible handling of default values for console arguments in the application, as it can
    ///     determine the appropriate default value based on the context of the argument and the associated attribute,
    ///     providing a more dynamic and robust way to handle default values for console arguments when parsing
    ///     command-line input.
    /// </summary>
    /// <returns>
    ///     The default value of the console argument based on the associated attribute and the context of the
    ///     argument, which is used to provide a default value for the property when parsing command-line input
    ///     if the argument is not found or cannot be parsed. The method checks the required state of the argument
    ///     and returns null if it's required. If the argument is not required, it determines the default value based
    ///     on the type of the associated attribute and the context of the argument. For a named argument with a string
    ///     default value, it attempts to parse the default value to the type of the property. For a nested verb, it
    ///     creates an instance of the property type or retrieves the default value from the type itself. For a
    ///     positional argument with a string default value, it also attempts to parse the default value to the type
    ///     of the property. If none of these conditions are met, it checks for a default value property attribute and
    ///     attempts to retrieve the default value from the property, or simply returns the default value from the type
    ///     itself if there is no default value property attribute. This allows for flexible handling of default values
    ///     for console arguments in the application, as it can determine the appropriate default value based on the
    ///     context of the argument and the associated attribute, providing a more dynamic and robust way to handle
    ///     default values for console arguments when parsing command-line input.
    /// </returns>
    private object GetDefaultValue() =>
        Required
            ? null
            : Attribute switch
            {
                // Check for a named argument with a string default value then attempt to parse it,
                // otherwise return the default value specified in the attribute, or the default value
                // from the type if there is no default value specified in the attribute.
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

                // Check for a nested verb with a string default value then attempt to parse it.
                NestedConsoleVerbAttribute => Property.PropertyType.CreateInstance()
                    ?? Property.PropertyType.GetDefaultValue(),

                // Check for a positional argument with a string default value then attempt to parse it,
                // otherwise return the default value specified in the attribute, or the default value
                // from the type if there is no default value specified in the attribute.
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

                // Otherwise, check for a default value property attribute and attempt to retrieve the default value
                // from the property, or simply return the default value from the type itself if there is no default
                // value property attribute.
                _ => (Property.Attributes & PropertyAttributes.HasDefault) != 0
                    ? Property.GetValue(Property.PropertyType.CreateInstance())
                    : Property.PropertyType.GetDefaultValue(),
            };

    /// <summary>
    ///     This method retrieves the value of the console argument as its specific type, which is used to set the
    ///     value of on an input model property when parsing command-line input. The method checks if the argument
    ///     was found in the command-line input and attempts to parse the value of the argument to the type of the
    ///     property. If the argument was not found and is required, it returns null; if the argument was not found
    ///     and is not required, it returns the default value specified in the attribute. If the argument was found
    ///     but the value cannot be parsed to the type of the property, it also returns null if the argument is
    ///     required, or the default value if the argument is not required. This allows for flexible handling of
    ///     console arguments in the application, as it can gracefully handle missing or invalid arguments based on
    ///     the requirements specified in the attribute and provide appropriate default values when necessary.
    /// </summary>
    /// <returns>
    ///     The value of the console argument as its specific type, which is used to set the value of on an input model
    ///     property when parsing command-line input. The method checks if the argument was found in the command-line
    ///     input and attempts to parse the value of the argument to the type of the property. If the argument was not
    ///     found and is required, it returns null; if the argument was not found and is not required, it returns the
    ///     default value specified in the attribute. If the argument was found but the value cannot be parsed to the
    ///     type of the property, it also returns null if the argument is required, or the default value if the argument
    ///     is not required. This allows for flexible handling of console arguments in the application, as it can
    ///     gracefully handle missing or invalid arguments based on the requirements specified in the attribute and
    ///     provide appropriate default values when necessary.
    /// </returns>
    private object GetParsedNamedValue()
    {
        // Ensure the attribute has been found/provided and that it's not required.
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
    ///     This method retrieves the value of the console argument as its specific type, which is used to set the
    ///     value of on an input model property when parsing command-line input. The method checks if the argument
    ///     was found in the command-line input and attempts to parse the value of the argument to the type of the
    ///     property. If the argument was not found and is required, it returns null; if the argument was not found
    ///     and is not required, it returns the default value specified in the attribute. If the argument was found
    ///     but the value cannot be parsed to the type of the property, it also returns null if the argument is
    ///     required, or the default value if the argument is not required. This allows for flexible handling of
    ///     console arguments in the application, as it can gracefully handle missing or invalid arguments based on
    ///     the requirements specified in the attribute and provide appropriate default values when necessary. The
    ///     method also takes an optional <paramref name="argumentCollection" /> parameter, which is used to provide
    ///     the collection of CLI arguments for parsing nested verb arguments, allowing for more complex command
    ///     structures and nested commands in the application. If the argument is not found or cannot be parsed, it
    ///     returns null or the default value based on the requirements specified in the attribute, providing graceful
    ///     handling of missing or invalid arguments in the application. Overall, this method serves as a central
    ///     point for retrieving the parsed value of a console argument based on its associated attribute and the
    ///     context of the command being executed, allowing for flexible and dynamic handling of console arguments
    ///     in the application when parsing command-line input.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The collection of CLI arguments for parsing nested verb arguments, which is used to provide the
    ///     collection of CLI arguments for parsing nested verb arguments, allowing for more complex command structures
    ///     and nested commands in the application. This parameter is particularly useful when the console argument
    ///     being parsed is associated with a nested verb attribute, as it allows for the retrieval of the appropriate
    ///     arguments for parsing the nested verb and its associated properties, providing a more dynamic and robust
    ///     way to handle nested commands and their arguments in the application when parsing command-line input.
    /// </param>
    /// <returns>
    ///     The value of the console argument as its specific type, which is used to set the value of on an input
    ///     model property when parsing command-line input. The method checks if the argument was found in the
    ///     command-line input and attempts to parse the value of the argument to the type of the property. If the
    ///     argument was not found and is required, it returns null; if the argument was not found and is not
    ///     required, it returns the default value specified in the attribute. If the argument was found but the
    ///     value cannot be parsed to the type of the property, it also returns null if the argument is required,
    ///     or the default value if the argument is not required. This allows for flexible handling of console
    ///     arguments in the application, as it can gracefully handle missing or invalid arguments based on the
    ///     requirements specified in the attribute and provide appropriate default values when necessary. The
    ///     method also takes an optional <paramref name="argumentCollection" /> parameter, which is used to
    ///     provide the collection of CLI arguments for parsing nested verb arguments, allowing for more complex
    ///     command structures and nested commands in the application. If the argument is not found or cannot be
    ///     parsed, it returns null or the default value based on the requirements specified in the attribute,
    ///     providing graceful handling of missing or invalid arguments in the application. Overall, this method
    ///     serves as a central point for retrieving the parsed value of a console argument based on its associated
    ///     attribute and the context of the command being executed, allowing for flexible and dynamic handling of
    ///     console arguments in the application when parsing command-line input.
    /// </returns>
    private object GetParsedNestedVerbValue(ConsoleCommandArgumentCollection argumentCollection) =>
        Found
            ? new ConsoleCommandArgumentCollection(
                argumentCollection?.Where(a => a.Index > Arguments[0].Index) ?? []
            ).Parse(Property.PropertyType)
            : GetDefaultValue();

    /// <summary>
    ///     This method retrieves the value of the console argument as its specific type, which is used to set the
    ///     value of on an input model property when parsing command-line input. The method checks if the argument
    ///     was found in the command-line input and attempts to parse the value of the argument to the type of the
    ///     property. If the argument was not found and is required, it returns null; if the argument was not found
    ///     and is not required, it returns the default value specified in the attribute. If the argument was found
    ///     but the value cannot be parsed to the type of the property, it also returns null if the argument is
    ///     required, or the default value if the argument is not required. This allows for flexible handling of
    ///     console arguments in the application, as it can gracefully handle missing or invalid arguments based on
    ///     the requirements specified in the attribute and provide appropriate default values when necessary.
    /// </summary>
    /// <returns>
    ///     The value of the console argument as its specific type, which is used to set the value of on an input model
    ///     property when parsing command-line input. The method checks if the argument was found in the command-line
    ///     input and attempts to parse the value of the argument to the type of the property. If the argument was not
    ///     found and is required, it returns null; if the argument was not found and is not required, it returns the
    ///     default value specified in the attribute. If the argument was found but the value cannot be parsed to the
    ///     type of the property, it also returns null if the argument is required, or the default value if the
    ///     argument is not required. This allows for flexible handling of console arguments in the application, as it
    ///     can gracefully handle missing or invalid arguments based on the requirements specified in the attribute and
    ///     provide appropriate default values when necessary.
    /// </returns>
    private object GetParsedPositionalValue() =>
        Found
        && Arguments
            .FirstOrDefault()
            ?.Value?.TryGetTypedValue(Property.PropertyType, out object value)
            is true
            ? value
            : GetDefaultValue();

    /// <summary>
    ///     This method retrieves the <see cref="ConsoleArgumentAttribute" /> associated with the CLI arguments as a
    ///     specific type, which is used to provide context about the argument for further processing in the application.
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
    ///     This method retrieves the value of the console argument as its specific type, which is used to set
    ///     the value of on an input model property when parsing command-line input. The method checks the type
    ///     of the associated attribute and calls the appropriate parsing method based on whether it's a named
    ///     argument, a nested verb, or a positional argument. This allows for flexible handling of different
    ///     types of console arguments in the application, as it can determine the appropriate parsing logic
    ///     based on the context of the argument and the associated attribute, providing a more dynamic and robust
    ///     way to handle console arguments when parsing command-line input. The method also takes an optional
    ///     <paramref name="argumentCollection" /> parameter, which is used to provide the collection of CLI
    ///     arguments for parsing nested verb arguments, allowing for more complex command structures and nested
    ///     commands in the application. If the argument is not found or cannot be parsed, it returns null or
    ///     the default value based on the requirements specified in the attribute, providing graceful handling of
    ///     missing or invalid arguments in the application. Overall, this method serves as a central point for
    ///     retrieving the parsed value of a console argument based on its associated attribute and the context of
    ///     the command being executed, allowing for flexible and dynamic handling of console arguments in the
    ///     application when parsing command-line input.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The optional collection of CLI arguments for parsing nested verb arguments, which is used to provide
    ///     the collection of CLI arguments for parsing nested verb arguments, allowing for more complex command
    ///     structures and nested commands in the application. This parameter is particularly useful when the
    ///     console argument being parsed is associated with a nested verb attribute, as it allows for the retrieval
    ///     of the appropriate arguments for parsing the nested verb and its associated properties, providing a more
    ///     dynamic and robust way to handle nested commands and their arguments in the application when parsing
    ///     command-line input.
    /// </param>
    /// <returns>
    ///     The value of the console argument as its specific type, which is used to set the value of on an input model
    ///     property when parsing command-line input. The method checks the type of the associated attribute and calls
    ///     the appropriate parsing method based on whether it's a named argument, a nested verb, or a positional
    ///     argument. This allows for flexible handling of different types of console arguments in the application,
    ///     as it can determine the appropriate parsing logic based on the context of the argument and the associated
    ///     attribute, providing a more dynamic and robust way to handle console arguments when parsing command-line
    ///     input. The method also takes an optional <paramref name="argumentCollection" /> parameter, which is used
    ///     to provide the collection of CLI arguments for parsing nested verb arguments, allowing for more complex
    ///     command structures and nested commands in the application. If the argument is not found or cannot be
    ///     parsed, it returns null or the default value based on the requirements specified in the attribute,
    ///     providing graceful handling of missing or invalid arguments in the application. Overall, this method
    ///     serves as a central point for retrieving the parsed value of a console argument based on its associated
    ///     attribute and the context of the command being executed, allowing for flexible and dynamic handling of
    ///     console arguments in the application when parsing command-line input.
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
    ///     This method retrieves the value of the console argument as its specific type, which is used to set the
    ///     value of on an input model property when parsing command-line input. The method checks the type of the
    ///     associated attribute and calls the appropriate parsing method based on whether it's a named argument,
    ///     a nested verb, or a positional argument. This allows for flexible handling of different types of console
    ///     arguments in the application, as it can determine the appropriate parsing logic based on the context of
    ///     the argument and the associated attribute, providing a more dynamic and robust way to handle console
    ///     arguments when parsing command-line input. The method also takes an optional
    ///     <paramref name="argumentCollection" /> parameter, which is used to provide the collection of CLI arguments
    ///     for parsing nested verb arguments, allowing for more complex command structures and nested commands in the
    ///     application. If the argument is not found or cannot be parsed, it returns null or the default value based
    ///     on the requirements specified in the attribute, providing graceful handling of missing or invalid arguments
    ///     in the application. Overall, this method serves as a central point for retrieving the parsed value of a
    ///     console argument based on its associated attribute and the context of the command being executed, allowing
    ///     for flexible and dynamic handling of console arguments in the application when parsing command-line input.
    ///     The method is also generic, allowing the caller to specify the desired type of the value being retrieved,
    ///     which is used to convert the parsed value to a more specific data type for further processing in the
    ///     application. This provides a more convenient and type-safe way to retrieve the parsed value of a console
    ///     argument, as the caller can directly specify the expected type of the value and receive it in that type
    ///     without needing to manually cast the result, enhancing the usability and readability of the code when
    ///     working with parsed console arguments in the application when parsing command-line input.
    /// </summary>
    /// <param name="argumentCollection">
    ///     The optional collection of CLI arguments for parsing nested verb arguments, which is used to provide the
    ///     collection of CLI arguments for parsing nested verb arguments, allowing for more complex command structures
    ///     and nested commands in the application. This parameter is particularly useful when the console argument
    ///     being parsed is associated with a nested verb attribute, as it allows for the retrieval of the appropriate
    ///     arguments for parsing the nested verb and its associated properties, providing a more dynamic and robust
    ///     way to handle nested commands and their arguments in the application when parsing command-line input.
    ///     If the argument is not found or cannot be parsed, it returns null or the default value based on the
    ///     requirements specified in the attribute, providing graceful handling of missing or invalid arguments in
    ///     the application. Overall, this method serves as a central point for retrieving the parsed value of a
    ///     console argument based on its associated attribute and the context of the command being executed, allowing
    ///     for flexible and dynamic handling of console arguments in the application when parsing command-line input.
    ///     The method is also generic, allowing the caller to specify the desired type of the value being retrieved,
    ///     which is used to convert the parsed value to a more specific data type for further processing in the
    ///     application. This provides a more convenient and type-safe way to retrieve the parsed value of a console
    ///     argument, as the caller can directly specify the expected type of the value and receive it in that type
    ///     without needing to manually cast the result, enhancing the usability and readability of the code when
    ///     working with parsed console arguments in the application when parsing command-line input.
    /// </param>
    /// <typeparam name="TValue">
    ///     The type to which the value of the console argument should be converted, which is used to convert the
    ///     parsed value to a more specific data type for further processing in the application. This allows the
    ///     caller to directly specify the expected type of the value being retrieved, providing a more convenient
    ///     and type-safe way to work with parsed console arguments in the application when parsing command-line input,
    ///     as it eliminates the need for manual casting and enhances the readability of the code when working with
    ///     parsed console arguments in the application.
    /// </typeparam>
    /// <returns>
    ///     The value of the console argument as its specific type, which is used to set the value of on an input model
    ///     property when parsing command-line input. The method checks the type of the associated attribute and calls
    ///     the appropriate parsing method based on whether it's a named argument, a nested verb, or a positional
    ///     argument. This allows for flexible handling of different types of console arguments in the application, as
    ///     it can determine the appropriate parsing logic based on the context of the argument and the associated
    ///     attribute, providing a more dynamic and robust way to handle console arguments when parsing command-line
    ///     input. The method also takes an optional <paramref name="argumentCollection" /> parameter, which is used
    ///     to provide the collection of CLI arguments for parsing nested verb arguments, allowing for more complex
    ///     command structures and nested commands in the application. If the argument is not found or cannot be
    ///     parsed, it returns null or the default value based on the requirements specified in the attribute,
    ///     providing graceful handling of missing or invalid arguments in the application. Overall, this method
    ///     serves as a central point for retrieving the parsed value of a console argument based on its associated
    ///     attribute and the context of the command being executed, allowing for flexible and dynamic handling of
    ///     console arguments in the application when parsing command-line input. The method is also generic, allowing
    ///     the caller to specify the desired type of the value being retrieved, which is used to convert the parsed
    ///     value to a more specific data type for further processing in the application. This provides a more
    ///     convenient and type-safe way to retrieve the parsed value of a console argument, as the caller can directly
    ///     specify the expected type of the value and receive it in that type without needing to manually cast the
    ///     result, enhancing the usability and readability of the code when working with parsed console arguments in
    ///     the application when parsing command-line input.
    /// </returns>
    public TValue GetParsedValue<TValue>(
        ConsoleCommandArgumentCollection argumentCollection = null
    ) => (TValue)GetParsedValue(argumentCollection);

    /// <summary>
    ///     This method retrieves the value of the console argument as its specific type,
    ///     which is used to set the value of the provided <paramref name="inputModel" />
    ///     property when parsing command-line input.
    /// </summary>
    /// <param name="inputModel">
    ///     The input model instance for which the value of the console argument should be set,
    ///     which is used to set the value of the property when parsing command-line input.
    /// </param>
    /// <param name="argumentCollection">
    ///     The full collection of CLI arguments being parsed, forwarded to <see cref="GetParsedValue(ConsoleCommandArgumentCollection)" />
    ///     so a nested-verb property can locate and parse its own sub-collection of arguments. Without
    ///     this, a nested verb's own arguments are invisible to it and it always resolves to its
    ///     default value instead of what was actually typed on the command line.
    /// </param>
    public void SetValue(
        object inputModel,
        ConsoleCommandArgumentCollection argumentCollection = null
    ) => Property?.SetValue(inputModel, GetParsedValue(argumentCollection));
}
