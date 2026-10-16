using System.Collections;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using ConsoleTables;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Service;
using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.String;
using Ninlil.Common.Extension.Type;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Enum;
using YamlDotNet.Serialization;
using XmlSerializer = Ninlil.Common.Serializer.XmlSerializer;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This abstract class is the base for input models: classes whose attributes declare a verb
///     (<see cref="ConsoleVerbAttribute" />), nested verbs, positional arguments and named
///     arguments. Its static methods read those attributes to build help text and to parse a
///     command line (through <see cref="ConsoleCommandArgumentCollection" />). Every input model
///     inherits the <c>--output-format</c> and <c>-s|--silent</c> arguments, and
///     <see cref="ToOutput{TModel}" /> renders results as JSON, XML, YAML or a console table
///     according to <see cref="Format" />.
/// </summary>
public abstract class ConsoleCommandInputModel : IConsoleCommandInputModel
{
    /// <summary>
    ///     This method generates the help text for the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The <see cref="IConsoleCommandInputModel" /> type to generate help text for.</param>
    /// <returns>A <see cref="string" /> containing the help text for the specified <paramref name="type" />.</returns>
    /// <param name="preface">Optional preface to the help text, such as missing arguments.</param>
    /// <param name="helpText">
    ///     Optional <see cref="ConsoleHelpTextCollectionModel" /> to use for application metadata.
    /// </param>
    /// <exception cref="ArgumentNullException">When the <paramref name="type" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="type" /> is not a valid console command input model.
    /// </exception>
    public static string GetHelpText(
        Type type,
        string preface = null,
        ConsoleHelpTextCollectionModel helpText = null
    ) =>
        preface?.Trim() is null or ""
            ? GetHelpTextCollection(type, helpText).ToString()
            : $"{preface.Trim()}{Environment.NewLine}{GetHelpTextCollection(type, helpText)}";

    /// <summary>
    ///     This method generates the help text for the specified <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="preface">Optional preface to the help text, such as missing arguments.</param>
    /// <param name="helpText">
    ///     Optional <see cref="ConsoleHelpTextCollectionModel" /> to use for application metadata.
    /// </param>
    /// <typeparam name="TInputModel">
    ///     The <see cref="IConsoleCommandInputModel" /> type to generate help text for.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="string" /> containing the help text for the specified <typeparamref name="TInputModel" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">When the <typeparamref name="TInputModel" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <typeparamref name="TInputModel" /> is not a valid console command input model.
    /// </exception>
    public static string GetHelpText<TInputModel>(
        string preface = null,
        ConsoleHelpTextCollectionModel helpText = null
    )
        where TInputModel : class, IConsoleCommandInputModel =>
        GetHelpText(typeof(TInputModel), preface, helpText);

    /// <summary>
    ///     This method generates the help text collection for the specified <paramref name="type" />
    ///     while, optionally, populating an existing <paramref name="helpText" /> collection.
    /// </summary>
    /// <param name="type">The <see cref="IConsoleCommandInputModel" /> type to generate help text for.</param>
    /// <param name="helpText">
    ///     Optional <see cref="ConsoleHelpTextCollectionModel" /> to use for application metadata.
    /// </param>
    /// <returns>
    ///     A <see cref="ConsoleHelpTextCollectionModel" /> containing
    ///     the help text for the specified <paramref name="type" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">When the <paramref name="type" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="type" /> is not a valid console command input model.
    /// </exception>
    public static ConsoleHelpTextCollectionModel GetHelpTextCollection(
        Type type,
        ConsoleHelpTextCollectionModel helpText = null
    )
    {
        // Ensure the type is not null.
        if (type is null)
            throw new ArgumentNullException(nameof(type), "The type cannot be null.");

        // Ensure the type is a valid console command input model.
        if (!typeof(IConsoleCommandInputModel).IsAssignableFrom(type))
            throw new ArgumentException(
                $"The type '{type.Name}' is not a valid ConsoleCommandInputModel.",
                nameof(type)
            );

        // Localize the verb attribute from the model.
        ConsoleVerbAttribute verb = GetVerb(type);

        // Localize the nested verb attributes from the model.
        List<NestedConsoleVerbAttribute> nestedVerbs = GetNestedVerbs(type)
            .Select(v => v.Item2)
            .ToList();

        // Localize the named argument attributes from the model.
        List<NamedConsoleArgumentAttribute> namedArguments = GetNamedArguments(type)
            .Select(n => n.Item2)
            .ToList();

        // Localize the positional argument attributes from the model.
        List<PositionalConsoleArgumentAttribute> positionalArguments = GetPositionalArguments(type)
            .Select(p => p.Item2)
            .OrderBy(p => p.Position)
            .ToList();

        // Define our help text container.
        helpText ??= [];

        // Check for a verb attribute then append it to the help text.
        if (verb is not null)
            helpText.Add(verb.ToHelpTextModel());

        // Check for nested verbs then add them to the help text.
        helpText.AddRange(nestedVerbs.Select(v => v.ToHelpTextModel()));

        // Check for positional arguments then add them to the help text.
        helpText.AddRange(positionalArguments.Select(a => a.ToHelpTextModel()));

        // Add the named arguments to the help text.
        helpText.AddRange(namedArguments.Select(a => a.ToHelpTextModel()));

        // We're done, return the help text collection.
        return helpText;
    }

    /// <summary>
    ///     This method generates the help text collection for the specified <typeparamref name="TInputModel" />
    ///     while, optionally, populating an existing <paramref name="helpText" /> collection.
    /// </summary>
    /// <param name="helpText">
    ///     Optional <see cref="ConsoleHelpTextCollectionModel" /> to use for application metadata.
    /// </param>
    /// <typeparam name="TInputModel">
    ///     The <see cref="IConsoleCommandInputModel" /> type to generate help text for.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="ConsoleHelpTextCollectionModel" /> containing
    ///     the help text for the specified <typeparamref name="TInputModel" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">When the <typeparamref name="TInputModel" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <typeparamref name="TInputModel" /> is not a valid console command input model.
    /// </exception>
    public static ConsoleHelpTextCollectionModel GetHelpTextCollection<TInputModel>(
        ConsoleHelpTextCollectionModel helpText = null
    )
        where TInputModel : class, IConsoleCommandInputModel =>
        GetHelpTextCollection(typeof(TInputModel), helpText);

    /// <summary>
    ///     This method retrieves the <see cref="PropertyInfo" /> and
    ///     <see cref="NamedConsoleArgumentAttribute" /> from the
    ///     specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to retrieve named arguments from.</param>
    /// <returns>
    ///     An <see cref="IEnumerable{T}" /> of tuples containing the <see cref="PropertyInfo" />
    ///     and <see cref="NamedConsoleArgumentAttribute" /> for each named argument in the <paramref name="type" />.
    /// </returns>
    public static IEnumerable<Tuple<PropertyInfo, NamedConsoleArgumentAttribute>> GetNamedArguments(
        Type type
    ) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<NamedConsoleArgumentAttribute>(false) is not null)
            .Select(p => new Tuple<PropertyInfo, NamedConsoleArgumentAttribute>(
                p,
                p.GetCustomAttribute<NamedConsoleArgumentAttribute>(false)
            ))
            .DistinctBy(a => a.Item2.Name.Trim());

    /// <summary>
    ///     This method retrieves the <see cref="PropertyInfo" /> and
    ///     <see cref="NestedConsoleVerbAttribute" /> from the
    ///     specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to retrieve nested verbs from.</param>
    /// <returns>
    ///     An <see cref="IEnumerable{T}" /> of tuples containing the <see cref="PropertyInfo" />
    ///     and <see cref="NestedConsoleVerbAttribute" /> for each nested verb in the <paramref name="type" />.
    /// </returns>
    public static IEnumerable<Tuple<PropertyInfo, NestedConsoleVerbAttribute>> GetNestedVerbs(
        Type type
    ) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<NestedConsoleVerbAttribute>(false) is not null)
            .Select(p => new Tuple<PropertyInfo, NestedConsoleVerbAttribute>(
                p,
                p.GetCustomAttribute<NestedConsoleVerbAttribute>(false)
            ))
            .DistinctBy(v => v.Item2.Name.Trim());

    /// <summary>
    ///     This method retrieves the <see cref="PropertyInfo" /> and
    ///     <see cref="PositionalConsoleArgumentAttribute" /> from the
    ///     specified <paramref name="type" />, ordered by position. Only the first property declared
    ///     for each position is kept.
    /// </summary>
    /// <param name="type">The type to retrieve positional arguments from.</param>
    /// <returns>
    ///     An <see cref="IEnumerable{T}" /> of tuples containing the <see cref="PropertyInfo" />
    ///     and <see cref="PositionalConsoleArgumentAttribute" /> for each positional argument in the <paramref name="type" />.
    /// </returns>
    public static IEnumerable<
        Tuple<PropertyInfo, PositionalConsoleArgumentAttribute>
    > GetPositionalArguments(Type type) =>
        type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<PositionalConsoleArgumentAttribute>(false) is not null)
            .Select(p => new Tuple<PropertyInfo, PositionalConsoleArgumentAttribute>(
                p,
                p.GetCustomAttribute<PositionalConsoleArgumentAttribute>(false)
            ))
            .OrderBy(p => p.Item2.Position)
            .DistinctBy(a => a.Item2.Position);

    /// <summary>
    ///     This method retrieves the <see cref="ConsoleVerbAttribute" /> declared on the specified
    ///     <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to retrieve the verb from.</param>
    /// <returns>
    ///     The <see cref="ConsoleVerbAttribute" /> declared on <paramref name="type" /> (not inherited),
    ///     or <see langword="null" /> when it has none.
    /// </returns>
    public static ConsoleVerbAttribute GetVerb(Type type) =>
        type.GetCustomAttribute<ConsoleVerbAttribute>(false);

    /// <summary>
    ///     This method generates and prints the help text for the specified <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="preface">Optional preface to the help text, such as missing arguments.</param>
    /// <typeparam name="TInputModel">
    ///     The <see cref="IConsoleCommandInputModel" /> to generate help text for.
    /// </typeparam>
    /// <exception cref="ArgumentNullException">When the <typeparamref name="TInputModel" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <typeparamref name="TInputModel" /> is not a valid console command input model.
    /// </exception>
    public static void HelpText<TInputModel>(string preface = null)
        where TInputModel : class, IConsoleCommandInputModel =>
        HelpText(typeof(TInputModel), preface);

    /// <summary>
    ///     This method generates and prints the help text for the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The <see cref="IConsoleCommandInputModel" /> to generate help text for.</param>
    /// <param name="preface">Optional preface to the help text, such as missing arguments.</param>
    /// <exception cref="ArgumentNullException">When the <paramref name="type" /> is null.</exception>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="type" /> is not a valid console command input model.
    /// </exception>
    public static void HelpText(Type type, string preface = null) =>
        ConsoleService.WriteLine(ConsoleColor.DarkCyan, GetHelpText(type, preface));

    /// <summary>
    ///     This method parses the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <paramref name="type" /> and
    ///     reports how it went. It never prints and never ends the process. See
    ///     <see cref="ConsoleCommandArgumentCollection.GetParseResult(Type)" /> for the rules.
    /// </summary>
    /// <param name="type">The type of the console command input model to parse the arguments into.</param>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <returns>The parse result; the model is in <see cref="ConsoleParseResult.Model" /> when it parsed.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="type" /> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="type" /> does not implement <see cref="IConsoleCommandInputModel" />.
    /// </exception>
    public static ConsoleParseResult GetParseResult(
        [NotNull] Type type,
        params string[] arguments
    ) => new ConsoleCommandArgumentCollection(arguments).GetParseResult(type);

    /// <summary>
    ///     This method parses the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <typeparamref name="TInputModel" />
    ///     and reports how it went. See
    ///     <see cref="ConsoleCommandArgumentCollection.GetParseResult(Type)" /> for the rules.
    /// </summary>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <typeparam name="TInputModel">
    ///     The type of the console command input model to parse the arguments into.
    /// </typeparam>
    /// <returns>The parse result.</returns>
    public static ConsoleParseResult GetParseResult<TInputModel>(params string[] arguments)
        where TInputModel : class, IConsoleCommandInputModel =>
        GetParseResult(typeof(TInputModel), arguments);

    /// <summary>
    ///     This method parses the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <paramref name="type" />. It never
    ///     prints, never ends the process and never returns <see langword="null" />.
    /// </summary>
    /// <param name="type">The type of the console command input model to parse the arguments into.</param>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <returns>
    ///     The <see cref="IConsoleCommandInputModel" /> representation of the parsed <paramref name="arguments" />.
    /// </returns>
    /// <exception cref="ArgumentNullException">When <paramref name="type" /> is null.</exception>
    /// <exception cref="ConsoleParseException">
    ///     When no model was produced: help was requested, the first argument does not match the
    ///     verb defined in <paramref name="type" />, or the arguments are invalid. The exception
    ///     carries the <see cref="ConsoleParseResult" /> with the help text to show.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="type" /> does not implement <see cref="IConsoleCommandInputModel" />.
    /// </exception>
    public static object Parse([NotNull] Type type, params string[] arguments) =>
        new ConsoleCommandArgumentCollection(arguments).Parse(type);

    /// <summary>
    ///     This method parses the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <typeparamref name="TInputModel" />.
    ///     See <see cref="Parse(Type, string[])" /> for the rules.
    /// </summary>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <typeparam name="TInputModel">
    ///     The type of the console command input model to parse the arguments into.
    /// </typeparam>
    /// <returns>
    ///     The <typeparamref name="TInputModel" /> representation of the parsed <paramref name="arguments" />.
    /// </returns>
    /// <exception cref="ConsoleParseException">
    ///     In the same cases as <see cref="Parse(Type, string[])" />.
    /// </exception>
    public static TInputModel Parse<TInputModel>(params string[] arguments)
        where TInputModel : class, IConsoleCommandInputModel =>
        (TInputModel)Parse(typeof(TInputModel), arguments);

    /// <summary>
    ///     This method attempts to parse the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <paramref name="type" />. It is
    ///     true exactly when <see cref="GetParseResult(Type, string[])" /> reports
    ///     <see cref="ConsoleParseOutcomeEnum.Parsed" />; help, a mismatched verb, invalid arguments
    ///     and an invalid type are all false.
    /// </summary>
    /// <param name="type">The type of the console command input model to parse the arguments into.</param>
    /// <param name="model">The parsed model, or <see langword="null" /> on failure.</param>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the parsing was successful or not.
    /// </returns>
    public static bool TryParse([NotNull] Type type, out object model, params string[] arguments)
    {
        // Try to parse the arguments into the model.
        try
        {
            // Parse the arguments and localize the result.
            ConsoleParseResult result = GetParseResult(type, arguments);

            // The result only carries a model when it parsed, so a null model is never success.
            model = result.Model;

            // We're done, report whether a model was produced.
            return result.Succeeded;
        }
        // Only an invalid type throws; it is a failure like any other here.
        catch (Exception)
        {
            // Reset the output model variable to null.
            model = null;

            // We're done, return false to indicate that parsing failed.
            return false;
        }
    }

    /// <summary>
    ///     This method attempts to parse the <paramref name="arguments" /> from the CLI into an
    ///     <see cref="IConsoleCommandInputModel" /> of the specified <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="model">The output model to parse the arguments into.</param>
    /// <param name="arguments">The arguments from the CLI to parse into the model.</param>
    /// <typeparam name="TInputModel">
    ///     The type of the console command input model to parse the <paramref name="arguments" /> into.
    /// </typeparam>
    /// <returns>
    ///     A <see cref="bool" /> indicating whether the parsing was successful or not.
    /// </returns>
    public static bool TryParse<TInputModel>(out TInputModel model, params string[] arguments)
        where TInputModel : class, IConsoleCommandInputModel
    {
        // Try to parse the arguments into the model.
        bool parsed = TryParse(typeof(TInputModel), out object parsedModel, arguments);

        // Reset the output model variable to the parsed model.
        model = parsedModel as TInputModel;

        // We're done, return the parsed result.
        return parsed;
    }

    /// <summary>
    ///     This property contains the format the output should be serialized into.
    /// </summary>
    [JsonPropertyName("outputFormat")]
    [NamedConsoleArgument<SerializerFormatEnum>(
        "output-format",
        "The format to use for the output of the console application.",
        false
    )]
    [XmlElement("OutputFormat")]
    [YamlMember(Alias = "outputFormat")]
    public SerializerFormatEnum Format { get; set; } = SerializerFormatEnum.None;

    /// <summary>
    ///     This property indicates whether output should be suppressed or not.
    /// </summary>
    [JsonPropertyName("silent")]
    [NamedConsoleArgument(
        's',
        "silent",
        "Indicates whether output should be suppressed or not.",
        false,
        false
    )]
    [XmlAttribute("silent")]
    [YamlMember(Alias = "silent")]
    public bool Silent { get; set; }

    /// <summary>
    ///     This method retrieves the <see cref="PropertyInfo" /> for the specified <paramref name="propertyName" />.
    /// </summary>
    /// <param name="propertyName">The name of the property to retrieve, ignoring case.</param>
    /// <returns>The <see cref="PropertyInfo" /> for the property.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist
    ///     in the type or isn't a public instance property.
    /// </exception>
    private PropertyInfo GetPropertyInfo(string propertyName) =>
        GetType()
            .GetProperty(
                propertyName?.Trim() ?? string.Empty,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            )
        ?? throw new ArgumentException(
            $"Property '{propertyName}' not found in type '{GetType().Name}'.  Ensure it's a public instance property.",
            nameof(propertyName)
        );

    /// <summary>
    ///     This method retrieves the <see cref="PropertyInfo" /> for
    ///     the specified property in the <paramref name="expression" />.
    /// </summary>
    /// <param name="expression">The expression containing the property to retrieve.</param>
    /// <typeparam name="TModel">The model type containing the property.</typeparam>
    /// <typeparam name="TProperty">The property type to retrieve.</typeparam>
    /// <returns>The <see cref="PropertyInfo" /> for the specified property.</returns>
    /// <exception cref="ArgumentException">
    ///     When the property in the <paramref name="expression" /> does not exist in
    ///     the <typeparamref name="TModel" /> or isn't a public instance property.
    /// </exception>
    private PropertyInfo GetPropertyInfo<TModel, TProperty>(
        Expression<Func<TModel, TProperty>> expression
    )
        where TModel : class, IConsoleCommandInputModel =>
        GetPropertyInfo((expression.Body as MemberExpression)?.Member.Name);

    /// <summary>
    ///     This method makes sure the output uses a serialized format. A <see cref="Format" /> that is
    ///     already JSON, XML or YAML is kept. Otherwise it is set to <paramref name="format" /> when
    ///     that is JSON, XML or YAML, and to JSON when it is not.
    /// </summary>
    /// <param name="format">The format to use when the current one is not serialized.</param>
    public void ForceSerializedOutputFormat(SerializerFormatEnum format)
    {
        // A serialized format the user chose stays.
        if (IsSerializedFormat(Format))
            return;

        // Use the requested format when it is serialized, otherwise fall back to JSON.
        Format = IsSerializedFormat(format) ? format : SerializerFormatEnum.Json;
    }

    /// <summary>
    ///     This method tells whether <paramref name="format" /> is one of the serializer formats
    ///     (JSON, XML or YAML) rather than the console table.
    /// </summary>
    /// <param name="format">The format to test.</param>
    /// <returns><see langword="true" /> for JSON, XML and YAML.</returns>
    private static bool IsSerializedFormat(SerializerFormatEnum format) =>
        format
            is SerializerFormatEnum.Json
                or SerializerFormatEnum.Xml
                or SerializerFormatEnum.Yaml;

    /// <summary>
    ///     This method retrieves the argument name or position for the specified property name.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the argument name or position for.</param>
    /// <returns>The argument name or position for the specified property name in a printable format.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    ///
    ///     When the <paramref name="propertyName" /> is not a named argument,
    ///     positional argument, or nested verb on the current type.
    /// </exception>
    public string GetArgumentNameOrPosition(string propertyName)
    {
        // Localize the property info from the type.
        PropertyInfo property = GetType()
            .GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase
            );

        // Ensure we have a property to process.
        if (property is null)
            throw new ArgumentException(
                $"The property '{propertyName}' does not exist, or is not public, on '{GetType().Name}'.",
                nameof(propertyName)
            );

        // Check for a named argument attribute on the property.
        if (IsNamedArgument(propertyName))
        {
            // Localize the attribute from the property.
            NamedConsoleArgumentAttribute attribute = GetNamedArgument(propertyName);

            // We're done, generate and return the argument name.
            return attribute.ShortName is not null
                ? $"-{attribute.ShortName}|--{attribute.Name}"
                : $"--{attribute.Name}";
        }

        // Check for a positional argument attribute on the property.
        if (IsPositionalArgument(propertyName))
            return $"<{GetPositionalArgument(propertyName).Position}>";

        // Check for a nested verb attribute on the property.
        if (IsNestedVerb(propertyName))
            return GetNestedVerb(propertyName).Name;

        // Otherwise, the property isn't an argument so it's out of scope.
        throw new ArgumentException(
            $"The property '{propertyName}' is not a named argument, positional argument, or nested verb on '{GetType().Name}'.",
            nameof(propertyName)
        );
    }

    /// <summary>
    ///     This method retrieves the <see cref="NamedConsoleArgumentAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a named argument.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the named argument attribute for.</param>
    /// <returns>
    ///     The <see cref="NamedConsoleArgumentAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public NamedConsoleArgumentAttribute GetNamedArgument(string propertyName) =>
        IsNamedArgument(propertyName)
            ? GetNamedArguments(GetType())
                .First(n =>
                    n.Item1.Name.Equals(
                        GetPropertyInfo(propertyName).Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Item2
            : null;

    /// <summary>
    ///     This method retrieves the <see cref="PositionalConsoleArgumentAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a positional argument.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the positional argument attribute for.</param>
    /// <returns>
    ///     The <see cref="PositionalConsoleArgumentAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public PositionalConsoleArgumentAttribute GetPositionalArgument(string propertyName) =>
        IsPositionalArgument(propertyName)
            ? GetPositionalArguments(GetType())
                .First(p =>
                    p.Item1.Name.Equals(
                        GetPropertyInfo(propertyName).Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Item2
            : null;

    /// <summary>
    ///     This method retrieves the <see cref="NestedConsoleVerbAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a nested verb.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the nested verb attribute for.</param>
    /// <returns>
    ///     The <see cref="NestedConsoleVerbAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public NestedConsoleVerbAttribute GetNestedVerb(string propertyName) =>
        IsNestedVerb(propertyName)
            ? GetNestedVerbs(GetType())
                .First(v =>
                    v.Item1.Name.Equals(
                        GetPropertyInfo(propertyName).Name,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .Item2
            : null;

    /// <summary>
    ///     This method determines if the current type has any named or positional arguments defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has named or positional arguments, otherwise <see langword="false" />.
    /// </returns>
    public bool HasArguments() =>
        GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(p =>
                p.GetCustomAttributes<NamedConsoleArgumentAttribute>(true).Any()
                || p.GetCustomAttributes<PositionalConsoleArgumentAttribute>(true).Any()
            );

    /// <summary>
    ///     This method determines if the current type has any required named or positional arguments defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has required named
    ///     or positional arguments, otherwise <see langword="false" />.
    /// </returns>
    public bool HasRequiredArguments() =>
        GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(p =>
                p.GetCustomAttributes<NamedConsoleArgumentAttribute>(true).Any(a => a.Required)
            )
        || GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Any(p =>
                p.GetCustomAttributes<PositionalConsoleArgumentAttribute>(true).Any(a => a.Required)
            );

    /// <summary>
    ///     This method determines if the current type has a verb defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has a verb defined, otherwise <see langword="false" />.
    /// </returns>
    public bool HasVerb() => GetType().GetCustomAttributes<ConsoleVerbAttribute>(true).Any();

    /// <summary>
    ///     This method determines if the current type is the default verb for the console application.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type is the default verb, otherwise <see langword="false" />.
    /// </returns>
    public bool IsDefault() =>
        GetType().GetCustomAttributes<ConsoleVerbAttribute>(true).Any(v => v.Default);

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a named argument or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a named argument or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsNamedArgument(string propertyName) =>
        GetNamedArguments(GetType())
            .Any(n =>
                n.Item1.Name.Equals(
                    GetPropertyInfo(propertyName).Name,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a positional argument or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a positional argument or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsPositionalArgument(string propertyName) =>
        GetPositionalArguments(GetType())
            .Any(p =>
                p.Item1.Name.Equals(
                    GetPropertyInfo(propertyName).Name,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a nested verb or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a nested verb or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsNestedVerb(string propertyName) =>
        GetNestedVerbs(GetType())
            .Any(v =>
                v.Item1.Name.Equals(
                    GetPropertyInfo(propertyName).Name,
                    StringComparison.OrdinalIgnoreCase
                )
            );

    /// <summary>
    ///     This method sets the default values for all named and positional arguments along with the nested verbs in
    ///     the input model. If a property already has a value, it will not be overwritten by the default value.
    ///     A string default on a non-string property is converted to the property type, as the parser does.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="IConsoleCommandInputModel" /> with the default
    ///     values set for all named and positional arguments along with the nested verbs.
    /// </returns>
    public IConsoleCommandInputModel SetDefaults()
    {
        // Iterate through the named arguments in the instance.
        foreach (
            PropertyInfo property in GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttribute<NamedConsoleArgumentAttribute>(true) is not null)
        )
        {
            // Localize the attribute for the property.
            NamedConsoleArgumentAttribute attribute = GetNamedArgument(property.Name);

            // Localize the current value of the property.
            object currentValue = property.GetValue(this);

            // Localize the default value of the property type.
            object typeDefault = property.PropertyType.GetDefaultValue();

            // Only apply attribute defaults when the property is still at its type default.
            if (Equals(currentValue, typeDefault) && attribute.Default is not null)
                property.SetValue(
                    this,
                    ParsedConsoleArgument.ConvertAttributeDefault(
                        attribute.Default,
                        property.PropertyType
                    )
                );
        }

        // Iterate through the nested verbs in the instance.
        foreach (
            PropertyInfo property in GetType()
                .GetProperties()
                .Where(p => p.GetCustomAttribute<NestedConsoleVerbAttribute>(true) is not null)
        )
        {
            // Localize the current value of the property.
            object currentValue = property.GetValue(this);

            // If we already have a value, go ahead and skip to the next iteration.
            if (currentValue is not null)
                continue;

            // Instantiate our nested verb model.
            object instance = property.PropertyType.CreateInstance();

            // If the instance implements IConsoleCommandInputModel,
            // call SetDefaults on it to apply defaults recursively.
            if (instance is IConsoleCommandInputModel nested)
                nested.SetDefaults();

            // Set the property value to the newly created instance.
            property.SetValue(this, instance);
        }

        // Iterate through the positional arguments in the instance.
        foreach (
            PropertyInfo property in GetType()
                .GetProperties()
                .Where(p =>
                    p.GetCustomAttribute<PositionalConsoleArgumentAttribute>(true) is not null
                )
        )
        {
            // Localize the attribute for the property.
            PositionalConsoleArgumentAttribute attribute = GetPositionalArgument(property.Name);

            // Localize the current value of the property.
            object currentValue = property.GetValue(this);

            // Localize the default value of the property type.
            object typeDefault = property.PropertyType.GetDefaultValue();

            // Only apply attribute defaults when the property is still at its type default.
            if (Equals(currentValue, typeDefault) && attribute.Default is not null)
                property.SetValue(
                    this,
                    ParsedConsoleArgument.ConvertAttributeDefault(
                        attribute.Default,
                        property.PropertyType
                    )
                );
        }

        // We're done, return the updated instance.
        return this;
    }

    /// <summary>
    ///     This method converts the <paramref name="models" /> into an output string in the
    ///     <see cref="Format" /> the user chose. JSON, XML and YAML use the serializers; any other
    ///     format renders a console table with one row per model and one column per public property.
    ///     Models that are themselves collections get a single column named after their kind.
    /// </summary>
    /// <param name="models">The model, or collection of models, to convert into an output string.</param>
    /// <returns>The output string, or an empty string when there are no models.</returns>
    public string ToOutput<TModel>(TModel models)
        where TModel : class
    {
        // Define our enumerable of models.
        List<object> modelsList;

        // Check for a null then create an empty enumerable.
        if (models is null)
            modelsList = [];
        // Check for an array then localize the models into an enumerable collection.
        else if (models is TModel[] array)
            modelsList = array.Cast<object>().ToList();
        // Check for a list then localize the models into an enumerable collection.
        else if (models is List<TModel> list)
            modelsList = list.Cast<object>().ToList();
        // Check for a collection then localize the models into an enumerable collection.
        else if (models is ICollection<TModel> collection)
            modelsList = collection.Cast<object>().ToList();
        // Check for an enumerable then localize the models into an enumerable collection.
        else if (models is IEnumerable<TModel> enumerableTyped)
            modelsList = enumerableTyped.Cast<object>().ToList();
        // Check for an enumerable then localize the models into an enumerable collection.
        else if (models is IEnumerable enumerable)
            modelsList = enumerable.Cast<object>().ToList();
        // Otherwise, localize the models into an enumerable collection.
        else
            modelsList = [models];

        // Ensure we have models, return if not.
        if (!modelsList.Any())
            return string.Empty;

        // Check for JSON serialized formatting, then return the serialized output.
        if (Format is SerializerFormatEnum.Json)
            return JsonSerializer.SerializePretty(modelsList);

        // Check for XML serialized formatting, then iterate through the model list and output the XML serialization of each model.
        if (Format is SerializerFormatEnum.Xml)
            return string.Join("\n", modelsList.Select(m => XmlSerializer.SerializePretty(m)));

        // Check for YAML serialized formatting, then return the serialized output.
        if (Format is SerializerFormatEnum.Yaml)
            return YamlSerializer.SerializePretty(modelsList);

        // Define our data table.
        DataTable table = new();

        // Loop through the models and add them to the data table.
        foreach (object model in modelsList.Where(m => m is not null))
        {
            // Create a new row for the data table.
            DataRow row = table.NewRow();

            // Check for an enumerable type, then add the enumerable values to the data table.
            if (model.GetType().IsEnumerableType(false))
            {
                // Localize the column name based on the model type.
                string column = model.GetType() switch
                {
                    // Check for an array then set the column name.
                    not null when model.GetType().IsArray() => "Array",

                    // Check for a collection then set the column name.
                    not null when model.GetType().IsCollection() => "Collection",

                    // Check for a list then set the column name.
                    not null when model.GetType().IsList() => "List",

                    // Check for a tuple then set the column name.
                    not null when model.GetType().IsTuple() => "Tuple",

                    // Otherwise, set the column name to "Enumerable".
                    _ => "Enumerable",
                };

                // Ensure the column exists in the data table, if not, add it.
                if (table.Columns[column] is null)
                    table.Columns.Add(column, typeof(string));

                // Add the enumerable values to the data table.
                row[column] = model.ToStringValue();
            }
            // Loop through the properties of the model and add them to the data table.
            else
                foreach (
                    PropertyInfo property in model
                        .GetType()
                        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Where(p => p.GetIndexParameters().Length is 0)
                )
                {
                    // Ensure the column exists in the data table, if not, add it.
                    if (table.Columns[property.Name] is null)
                        table.Columns.Add(property.Name, typeof(string));

                    // Localize the property value and convert it to a string.
                    object value = property.GetValue(model);

                    // Add the property to the data table.
                    row[property.Name] =
                        value?.ToStringValue() ?? value?.ToString() ?? string.Empty;
                }

            // Add the row to the data table.
            table.Rows.Add(row);
        }

        // We're done, return the new console table from the data table.
        return ConsoleTable.From(table).ToString();
    }

    /// <summary>
    ///     This method converts the <paramref name="models" /> into an output string.
    /// </summary>
    /// <param name="models">The models to convert into an output string.</param>
    /// <returns>The output string representation of the models.</returns>
    public string ToOutput(object models) => ToOutput<object>(models);

    /// <summary>
    ///     This method validates the parsed model. The base implementation accepts everything.
    ///     The library never calls it; an application that overrides it calls it itself.
    /// </summary>
    /// <param name="stoppingToken">
    ///     Optional <see cref="CancellationToken" /> used for tracking asynchronous operations.
    /// </param>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the validation is complete.</returns>
    /// <exception cref="System.Exception">When the <see cref="ConsoleCommandInputModel" /> is invalid.</exception>
    public virtual Task ValidateAsync(CancellationToken stoppingToken = default) =>
        Task.CompletedTask;
}
