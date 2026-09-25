using System.Reflection;
using System.Xml.Serialization;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Service;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is the tokenized form of a command line and the parser that maps it onto an
///     input model. The constructors split raw arguments into <see cref="ConsoleCommandArgument" />
///     tokens: a token starting with <c>-</c> or <c>--</c> is named (its value is the text after
///     <c>=</c>, else the next token, else <c>true</c> for a switch) and any other token is
///     positional. <see cref="Parse(System.Type)" /> then matches those tokens against the
///     attributes on an input model type and fills in a new instance.
///     <see cref="ConsoleCommandInputModel.Parse(System.Type, string[])" /> is the usual entry point.
/// </summary>
[XmlInclude(typeof(List<ConsoleCommandArgument>))]
[XmlRoot("consoleArgumentCollection")]
public class ConsoleCommandArgumentCollection : List<ConsoleCommandArgument>
{
    /// <summary>
    ///     This operator tokenizes a string array as command-line arguments.
    /// </summary>
    /// <param name="arguments">The command-line arguments to tokenize.</param>
    /// <returns>The tokenized arguments.</returns>
    public static implicit operator ConsoleCommandArgumentCollection(string[] arguments) =>
        new(arguments);

    /// <summary>
    ///     This operator tokenizes a list of strings as command-line arguments.
    /// </summary>
    /// <param name="arguments">The command-line arguments to tokenize.</param>
    /// <returns>The tokenized arguments.</returns>
    public static implicit operator ConsoleCommandArgumentCollection(List<string> arguments) =>
        new(arguments);

    /// <summary>
    ///     This operator tokenizes a single command-line string, split on spaces.
    /// </summary>
    /// <param name="input">The raw command-line input.</param>
    /// <returns>The tokenized arguments.</returns>
    public static implicit operator ConsoleCommandArgumentCollection(string input) => new(input);

    /// <summary>
    ///     This method tokenizes a sequence of command-line arguments.
    /// </summary>
    /// <param name="arguments">The command-line arguments to tokenize.</param>
    /// <returns>The tokenized arguments.</returns>
    public static ConsoleCommandArgumentCollection FromCli(IEnumerable<string> arguments) =>
        new(arguments);

    /// <summary>
    ///     This method tokenizes a single command-line string. The string is split on spaces, so
    ///     quoting is not supported.
    /// </summary>
    /// <param name="input">The raw command-line input.</param>
    /// <returns>The tokenized arguments.</returns>
    public static ConsoleCommandArgumentCollection FromCli(string input) => new(input);

    /// <summary>
    ///     This constructor creates an empty collection.
    /// </summary>
    public ConsoleCommandArgumentCollection() { }

    /// <summary>
    ///     This constructor creates an empty collection with the given initial capacity.
    /// </summary>
    /// <param name="capacity">The initial capacity of the collection.</param>
    public ConsoleCommandArgumentCollection(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor copies already tokenized arguments into a new collection.
    /// </summary>
    /// <param name="collection">The tokens to copy.</param>
    public ConsoleCommandArgumentCollection(IEnumerable<ConsoleCommandArgument> collection)
        : base(collection) { }

    /// <summary>
    ///     This constructor copies already tokenized arguments into a new collection.
    /// </summary>
    /// <param name="items">The tokens to copy.</param>
    public ConsoleCommandArgumentCollection(params ConsoleCommandArgument[] items)
        : base(items.AsEnumerable()) { }

    /// <summary>
    ///     This constructor tokenizes raw command-line arguments. Each token keeps its index in the
    ///     raw list. A positional token's <see cref="ConsoleCommandArgument.Position" /> is that raw
    ///     index, so named arguments and their values count towards positions.
    /// </summary>
    /// <param name="arguments">
    ///     The raw command-line arguments. A token starting with <c>-</c> is named (short form
    ///     <c>-x</c>, long form <c>--name</c>); any other token is positional.
    /// </param>
    public ConsoleCommandArgumentCollection(IEnumerable<string> arguments)
    {
        // Ensure we have arguments to parse.
        arguments = arguments?.ToList() ?? [];

        // Iterate through the provided arguments and parse them into ConsoleCommandArgument instances.
        for (int a = 0; a < arguments.Count(); ++a)
        {
            // Localize the argument for easier access.
            string argument = arguments.ElementAt(a).Trim();

            // Instantiate our new console argument instance.
            ConsoleCommandArgument commandArgument = new() { Index = a };

            // Check for a positional argument.
            if (!argument.StartsWith('-'))
            {
                // Ensure we have an empty character on the argument, as this is a positional argument.
                commandArgument.Character = char.MinValue;

                // Ensure we have an empty name on the argument, as this is a positional argument.
                commandArgument.Name = null;

                // This is a positional argument, so we set the position and value accordingly.
                commandArgument.Position = a;

                // Set the type to positional for this argument, as it does not have a character or name.
                commandArgument.Type = ConsoleCommandArgumentType.Positional;

                // Set the value to the current argument.
                commandArgument.Value = argument;

                // Add the parsed argument to our response list.
                Add(commandArgument);

                // We're done with this argument, so we can continue to the next one.
                continue;
            }

            // This is a named argument, so we need to parse the character from the argument string.
            commandArgument.Character = argument.StartsWith("--") ? char.MinValue : argument[1];

            // This is a named argument, so we need to parse the name from the argument string.
            commandArgument.Name = argument.StartsWith("--") ? argument[2..].Trim() : null;

            // This is a named argument, so we set the position to -1 to indicate that it's not a positional argument.
            commandArgument.Position = -1;

            // Set the type to named for this argument, as it has a character or name.
            commandArgument.Type = ConsoleCommandArgumentType.Named;

            // A value can be inline in the token ("--name=value" or "-n=value"). Test the raw token
            // for '=': at this point Name still holds the unsplit "name=value" remainder of a
            // long-form token, so it cannot be used for the check.
            if (argument.Contains('='))
            {
                // Split the argument string on the '=' character into its dash-prefixed key and its value.
                string[] argumentParts = argument.Split('=', 2, StringSplitOptions.TrimEntries);

                // A long-form token's name is the key without its leading "--"; a short-form token has
                // only its Character.
                commandArgument.Name =
                    commandArgument.Character == char.MinValue
                        ? argumentParts.FirstOrDefault()?[2..]
                        : null;

                // Split the argument string on the '=' character and set the value to the part after the '=' character.
                commandArgument.Value = argumentParts.LastOrDefault();

                // Add the parsed argument to our response list.
                Add(commandArgument);

                // We're done with this argument, so we can continue to the next one.
                continue;
            }

            // Check for a switch value.
            if (arguments.ElementAtOrDefault(a + 1)?.StartsWith('-') is not false)
            {
                // The next token is missing or is another named argument, so this is a switch: set it to true.
                commandArgument.Value = bool.TrueString.ToLower();

                // Add the parsed argument to our response list.
                Add(commandArgument);

                // We're done with this argument, so we can continue to the next one.
                continue;
            }

            // Set the value to the next argument, as this is a named argument with a value.
            commandArgument.Value = arguments.ElementAt(a + 1).Trim();

            // Add the parsed argument to our response list.
            Add(commandArgument);

            // Increment the index to skip the next argument, as we've
            // already processed it as the value for this named argument.
            ++a;
        }
    }

    /// <summary>
    ///     This constructor tokenizes a single command-line string, split on spaces. Quoting is not
    ///     supported.
    /// </summary>
    /// <param name="input">The raw command-line input.</param>
    public ConsoleCommandArgumentCollection(string input)
        : this(input?.Split(' ', StringSplitOptions.TrimEntries) ?? []) { }

    /// <summary>
    ///     This method pairs each property of <paramref name="inputModelType" /> that has a
    ///     <see cref="NamedConsoleArgumentAttribute" /> with the tokens that match its short or long
    ///     name (ignoring case).
    /// </summary>
    /// <param name="inputModelType">The input model type to inspect.</param>
    /// <returns>One <see cref="ParsedConsoleArgument" /> per named-argument property.</returns>
    public List<ParsedConsoleArgument> ParseNamedArguments(System.Type inputModelType) =>
        inputModelType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<NamedConsoleArgumentAttribute>(true) is not null)
            .Select(p => new
            {
                Property = p,
                Attribute = p.GetCustomAttribute<NamedConsoleArgumentAttribute>(true),
            })
            .Select(p => new ParsedConsoleArgument
            {
                // Match on the short name or the long name. Positional and other unmatched tokens have a
                // null Name, so the long-name comparison uses ?. to avoid a NullReferenceException when a
                // command line mixes named arguments with bare tokens (the case the
                // UnknownConsoleArgumentsAttribute capture exists for).
                Arguments = new(
                    this.Where(a =>
                        a.Character == p.Attribute.ShortName
                        || a.Name?.Equals(p.Attribute.Name, StringComparison.OrdinalIgnoreCase)
                            == true
                    )
                ),

                // Keep the attribute so the value can be converted and defaulted later.
                Attribute = p.Attribute,

                // Keep the property so the parsed value can be written to the model.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method pairs each property of <paramref name="inputModelType" /> that has a
    ///     <see cref="NestedConsoleVerbAttribute" /> with the tokens whose value is the verb's name
    ///     (ignoring case).
    /// </summary>
    /// <param name="inputModelType">The input model type to inspect.</param>
    /// <returns>One <see cref="ParsedConsoleArgument" /> per nested-verb property.</returns>
    public List<ParsedConsoleArgument> ParseNestedVerbs(System.Type inputModelType) =>
        inputModelType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<NestedConsoleVerbAttribute>(true) is not null)
            .Select(p => new
            {
                Property = p,
                Attribute = p.GetCustomAttribute<NestedConsoleVerbAttribute>(true),
            })
            .Select(p => new ParsedConsoleArgument
            {
                // Match any token whose value is the nested verb's name.
                Arguments = new(
                    this.Where(a =>
                        a.Value.Equals(p.Attribute.Name, StringComparison.OrdinalIgnoreCase)
                    )
                ),

                // Keep the attribute so the value can be converted and defaulted later.
                Attribute = p.Attribute,

                // Keep the property so the parsed value can be written to the model.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method pairs each property of <paramref name="inputModelType" /> that has a
    ///     <see cref="PositionalConsoleArgumentAttribute" /> with the token at that position.
    /// </summary>
    /// <param name="inputModelType">The input model type to inspect.</param>
    /// <returns>One <see cref="ParsedConsoleArgument" /> per positional-argument property.</returns>
    public List<ParsedConsoleArgument> ParsePositionalArguments(System.Type inputModelType) =>
        inputModelType
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<PositionalConsoleArgumentAttribute>(true) is not null)
            .Select(p => new
            {
                Property = p,
                Attribute = p.GetCustomAttribute<PositionalConsoleArgumentAttribute>(true),
            })
            .Select(p => new ParsedConsoleArgument
            {
                // Match the token whose raw position equals the attribute's position.
                Arguments = new(this.Where(a => a.Position.Equals(p.Attribute.Position))),

                // Keep the attribute so the value can be converted and defaulted later.
                Attribute = p.Attribute,

                // Keep the property so the parsed value can be written to the model.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method parses the tokens into a new instance of <paramref name="inputModelType" />.
    ///     <list type="bullet">
    ///         <item>
    ///             With no tokens it returns a model with attribute defaults applied, unless the model
    ///             has required arguments or a verb that is not the default verb.
    ///         </item>
    ///         <item>
    ///             If the model has a non-default verb, the first token must be that verb and is
    ///             removed before parsing.
    ///         </item>
    ///         <item>
    ///             If the next token is a help switch it prints the help text and exits the process.
    ///         </item>
    ///         <item>
    ///             If a required argument is missing it prints the help text with the missing
    ///             arguments and exits the process with code 1.
    ///         </item>
    ///         <item>
    ///             Otherwise it sets every argument property, and puts tokens nothing claimed into
    ///             the property marked with <see cref="UnknownConsoleArgumentsAttribute" />, if any.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <returns>
    ///     The populated input model, or <see langword="null" /> when setting the properties throws
    ///     (the exception is written to the console).
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="inputModelType" /> does not implement
    ///     <see cref="IConsoleCommandInputModel" />, when there are no tokens but the model needs
    ///     some, or when the first token does not match the model's verb.
    /// </exception>
    public object Parse(System.Type inputModelType)
    {
        // Ensure the type is valid for parsing, throw an exception if it's not.
        if (!inputModelType.IsAssignableTo(typeof(IConsoleCommandInputModel)))
            throw new InvalidOperationException(
                "The input model type must implement the IConsoleCommandInputModel interface."
            );

        // Instantiate our input model instance to populate with the parsed arguments.
        object inputModel = inputModelType.CreateInstance();

        // Check for no arguments then either return the model populated with its defaults
        // (via attribute/property defaults) or throw an exception if there are required arguments.
        if (Count is 0)
        {
            // Ensure the model has no required arguments (or a required verb), throw an exception if it does.
            if (
                (inputModel as IConsoleCommandInputModel)?.HasRequiredArguments() is not false
                || (
                    (inputModel as IConsoleCommandInputModel).HasVerb()
                    && !(inputModel as IConsoleCommandInputModel).IsDefault()
                )
            )
                throw new InvalidOperationException(
                    "No arguments were provided, but this input model requires a verb and/or required arguments."
                );

            // Build a model from defaults alone.
            try
            {
                // Parse and localize the named arguments into their defaults.
                List<ParsedConsoleArgument> namedArguments = ParseNamedArguments(inputModelType);

                // Parse and localize the nested verbs into their defaults.
                List<ParsedConsoleArgument> nestedVerbs = ParseNestedVerbs(inputModelType);

                // Parse and localize the positional arguments into their defaults.
                List<ParsedConsoleArgument> positionalArguments = ParsePositionalArguments(
                    inputModelType
                );

                // Reset our input model instance to a new one.
                inputModel = inputModelType.CreateInstance();

                // Apply attribute defaults only where one is defined (or for nested verbs), so property
                // initializers are not overwritten.
                foreach (
                    ParsedConsoleArgument argument in namedArguments
                        .Concat(nestedVerbs)
                        .Concat(positionalArguments)
                )
                    if (
                        argument.Attribute is NestedConsoleVerbAttribute
                        || argument.GetAttribute<NamedConsoleArgumentAttribute>()?.Default
                            is not null
                        || argument.GetAttribute<PositionalConsoleArgumentAttribute>()?.Default
                            is not null
                    )
                    {
                        // Localize the current value of the object.
                        object currentValue = argument.Property.GetValue(inputModel);

                        // Localize the type's default value.
                        object typeDefault = argument.Property.PropertyType.GetDefaultValue();

                        // If the current value and type's default value are equal then
                        // override the value with the default value from the attribute.
                        if (Equals(currentValue, typeDefault))
                            argument.SetValue(inputModel);
                    }

                // We're done, return the parsed default input model.
                return inputModel;
            }
            // Catch any exceptions that occur.
            catch (System.Exception exception)
            {
                // Write a warning to the console indicating the exception that occurred.
                ConsoleService.WriteLine(exception);

                // Rethrow so TryParse reports the failure.
                throw;
            }
        }

        // Check for a verb on the input model.
        if (
            (inputModel as IConsoleCommandInputModel)?.HasVerb() is true
            && !(inputModel as IConsoleCommandInputModel).IsDefault()
        )
        {
            // Localize the verb name from the type.
            string verb = inputModelType
                .GetCustomAttribute<ConsoleVerbAttribute>(true)
                ?.Name?.Trim();

            // When the verb has a name, the first token must match it. Remove it so the remaining
            // tokens can be parsed.
            if (verb?.Trim() is not (null or { Length: 0 }))
                if (
                    !this.FirstOrDefault()?.Value.Equals(verb, StringComparison.OrdinalIgnoreCase)
                    is true
                )
                    throw new InvalidOperationException(
                        $"The first argument in the collection must match the verb name '{verb}' defined in the input model type."
                    );
                else
                    RemoveAt(0);
        }

        // Check the first token for a help request. A positional token has a null Name, and
        // SanitizeArgumentName throws for null, so only compare the name when there is one.
        if (
            (
                this[0].Name is not null
                && ConsoleCommand.HelpRequestSwitches.Contains(
                    NamedConsoleArgumentAttribute.SanitizeArgumentName(this[0].Name),
                    StringComparer.OrdinalIgnoreCase
                )
            )
            || ConsoleCommand.HelpRequestSwitches.Contains(
                this[0].Character.ToString(),
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            // Generate the help text for the type.
            ConsoleCommand.HelpText(inputModelType);

            // Exit the process once the help text is printed.
            Environment.Exit(0);

            // Unreachable after Environment.Exit, but the compiler needs a return.
            return null;
        }

        // Try to parse the various types of arguments from the CLI into the input model.
        try
        {
            // Localize the named console arguments from the input model type for later access.
            List<ParsedConsoleArgument> namedArguments = ParseNamedArguments(inputModelType);

            // Localize the nested console verbs from the input model type for later access.
            List<ParsedConsoleArgument> nestedVerbs = ParseNestedVerbs(inputModelType);

            // Localize the positional console arguments from the input model type for later access.
            List<ParsedConsoleArgument> positionalArguments = ParsePositionalArguments(
                inputModelType
            );

            // Localize our missing arguments.
            IEnumerable<string> missing = positionalArguments
                .Where(a => a.Required && !a.Found)
                .Select(a =>
                    $"Missing argument at index: <{a.GetAttribute<PositionalConsoleArgumentAttribute>().Position}>"
                )
                .Concat(
                    namedArguments
                        .Where(a => a.Required && !a.Found)
                        .Select(a => a.GetAttribute<NamedConsoleArgumentAttribute>())
                        .Select(a =>
                            a.ShortName is not null
                                ? $"Missing argument: -{a.ShortName}, --{a.Name} ({a.Description})"
                                : $"Missing argument: --{a.Name} ({a.Description})"
                        )
                )
                .ToList();

            // Check for any missing required arguments.
            if (missing.Any())
            {
                // Display the help text with the missing arguments.
                ConsoleCommandInputModel.HelpText(
                    inputModelType,
                    string.Join(Environment.NewLine, missing)
                );

                // Exit the process with a failure code once the help text is printed.
                Environment.Exit(1);

                // Unreachable after Environment.Exit, but the compiler needs a return.
                return null;
            }

            // Reset our input model instance to a new one.
            inputModel = inputModelType.CreateInstance();

            // If the model has a ConsoleCommandArgumentCollection property marked with
            // UnknownConsoleArgumentsAttribute, fill it with every token no argument or nested verb
            // claimed, in their original order.
            inputModelType
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p =>
                    p.GetCustomAttribute<UnknownConsoleArgumentsAttribute>(true) is not null
                    && typeof(ConsoleCommandArgumentCollection).IsAssignableFrom(p.PropertyType)
                )
                ?.SetValue(
                    inputModel,
                    new ConsoleCommandArgumentCollection(
                        this.Where(a =>
                                !namedArguments
                                    .Concat(nestedVerbs)
                                    .Concat(positionalArguments)
                                    .SelectMany(p => p.Arguments.Select(c => c.Index))
                                    .Contains(a.Index)
                            )
                            .OrderBy(a => a.Index)
                    )
                );

            // Set every argument property on the model. The full token collection is passed along so
            // a nested verb can parse the tokens that follow its own verb token instead of falling back
            // to its default.
            foreach (
                ParsedConsoleArgument argument in namedArguments
                    .Concat(nestedVerbs)
                    .Concat(positionalArguments)
            )
                argument.SetValue(inputModel, this);

            // We're done, return the parsed and populated input model instance.
            return inputModel;
        }
        // Catch any exceptions that occur during parsing.
        catch (System.Exception exception)
        {
            // Write the exception to the console in red text.
            ConsoleService.WriteLine(exception, true);

            // Return a null result.
            return null;
        }
    }

    /// <summary>
    ///     This method parses the tokens into a new <typeparamref name="TInputModel" />. See
    ///     <see cref="Parse(System.Type)" /> for the rules.
    /// </summary>
    /// <typeparam name="TInputModel">The input model type to create and populate.</typeparam>
    /// <returns>The populated input model, or <see langword="null" /> when parsing failed.</returns>
    /// <exception cref="InvalidOperationException">
    ///     In the same cases as <see cref="Parse(System.Type)" />.
    /// </exception>
    public IConsoleCommandInputModel Parse<TInputModel>()
        where TInputModel : class, IConsoleCommandInputModel =>
        Parse(typeof(TInputModel)) as TInputModel;

    /// <summary>
    ///     This method joins the tokens back into a command line, in their original order. Tokens
    ///     with no character, name or position are left out.
    /// </summary>
    /// <returns>The tokens as a space-separated command line.</returns>
    public override string ToString() =>
        string.Join(
            ' ',
            this.Where(a => a is not null)
                .Where(a =>
                    a.Character != char.MinValue
                    || a.Name?.Trim() is not (null or { Length: 0 })
                    || a.Position > -1
                )
                .OrderBy(a => a.Index)
                .Select(a => a.ToString())
        );

    /// <summary>
    ///     This method tries to parse the tokens into a new instance of
    ///     <paramref name="inputModelType" /> with <see cref="Parse(System.Type)" />.
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <param name="model">The populated model, or <see langword="null" /> on failure.</param>
    /// <returns>
    ///     <see langword="true" /> when parsing produced a model; <see langword="false" /> when it
    ///     threw or returned <see langword="null" />.
    /// </returns>
    public bool TryParse(System.Type inputModelType, out object model)
    {
        // Try to parse the input model type, returning true if successful, otherwise false.
        try
        {
            // Parse the CLI arguments into the input model type.
            model = Parse(inputModelType);

            // A null result means Parse caught and reported an error.
            return model is not null;
        }
        // Catch any exceptions that occur during parsing, returning false to indicate that parsing failed.
        catch (System.Exception)
        {
            // Set the output model to null in case of an exception.
            model = null;

            // Return false to indicate that parsing failed.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to parse the tokens into a new <typeparamref name="TInputModel" />.
    /// </summary>
    /// <param name="model">The populated model, or <see langword="null" /> on failure.</param>
    /// <typeparam name="TInputModel">The input model type to create and populate.</typeparam>
    /// <returns><see langword="true" /> when parsing produced a model; otherwise <see langword="false" />.</returns>
    public bool TryParse<TInputModel>(out TInputModel model)
        where TInputModel : class, IConsoleCommandInputModel
    {
        // Try to parse the input model type.
        if (TryParse(typeof(TInputModel), out object result))
        {
            // Reset the output model to the generic type parameter if parsing was successful.
            model = result as TInputModel;

            // We're done, return true to indicate that parsing was successful.
            return true;
        }

        // Set the output model to null in case of a parsing failure.
        model = null;

        // Return false to indicate that parsing failed.
        return false;
    }
}
