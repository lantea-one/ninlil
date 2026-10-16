using System.Reflection;
using System.Xml.Serialization;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is the tokenized form of a command line and the parser that maps it onto an
///     input model. The constructors split raw arguments into <see cref="ConsoleCommandArgument" />
///     tokens: a token starting with <c>-</c> or <c>--</c> is named (its value is the text after
///     <c>=</c>, else the next token, else <c>true</c> for a switch) and any other token is
///     positional; a lone <c>-</c> is positional too. <see cref="GetParseResult(System.Type)" /> then
///     matches those tokens against the attributes on an input model type, fills in a new instance
///     and reports the outcome as a <see cref="ConsoleParseResult" />. The parser never prints and
///     never ends the process: help requests and errors are results, and the host owns the output
///     and the exit code. <see cref="Parse(System.Type)" /> and <see cref="TryParse(System.Type, out object)" />
///     are shortcuts over it; <see cref="ConsoleCommandInputModel.Parse(System.Type, string[])" /> is
///     the usual entry point.
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
            // Localize the argument for easier access. A null element is read as an empty token.
            string argument = arguments.ElementAt(a)?.Trim() ?? string.Empty;

            // Instantiate our new console argument instance.
            ConsoleCommandArgument commandArgument = new() { Index = a };

            // Check for a positional argument. A lone "-" is the usual stdin marker, so it is a value,
            // not a named argument with no name.
            if (!IsNamedToken(argument))
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

            // Check for a switch value. The next token is this argument's value unless it is missing
            // or is itself named; a lone "-" counts as a value by the same rule as above.
            string next = arguments.ElementAtOrDefault(a + 1)?.Trim();
            if (next is null || IsNamedToken(next))
            {
                // The next token is missing or is another named argument, so this is a switch: set it to true.
                commandArgument.Value = bool.TrueString.ToLower();

                // Add the parsed argument to our response list.
                Add(commandArgument);

                // We're done with this argument, so we can continue to the next one.
                continue;
            }

            // Set the value to the next argument, as this is a named argument with a value.
            commandArgument.Value = next;

            // Add the parsed argument to our response list.
            Add(commandArgument);

            // Increment the index to skip the next argument, as we've
            // already processed it as the value for this named argument.
            ++a;
        }
    }

    /// <summary>
    ///     This method says whether a raw token is a named argument: it starts with <c>-</c> and has
    ///     at least one more character. A lone <c>-</c> is a positional value.
    /// </summary>
    /// <param name="argument">The trimmed raw token.</param>
    /// <returns><see langword="true" /> for a named token; otherwise <see langword="false" />.</returns>
    private static bool IsNamedToken(string argument) =>
        argument.Length > 1 && argument.StartsWith('-');

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
    ///     This method parses the tokens into a new instance of <paramref name="inputModelType" /> and
    ///     reports how it went. It never prints, never ends the process and never changes this
    ///     collection: the steps run on a copy of the tokens, so the same collection can be parsed
    ///     again or against another model. The steps, in order:
    ///     <list type="number">
    ///         <item>
    ///             If the model has a verb that is not the default verb, the first token must be a
    ///             positional token equal to it (ignoring case), else the result is
    ///             <see cref="ConsoleParseOutcomeEnum.NotMatched" />. The verb token is then dropped.
    ///         </item>
    ///         <item>
    ///             If the next token is a named help switch
    ///             (<see cref="ConsoleCommand.HelpRequestSwitches" />), the result is
    ///             <see cref="ConsoleParseOutcomeEnum.HelpRequested" /> with the model's help text.
    ///         </item>
    ///         <item>
    ///             With no tokens left, the model gets its attribute defaults without overwriting
    ///             property initializers; a missing required argument makes the result
    ///             <see cref="ConsoleParseOutcomeEnum.Invalid" />.
    ///         </item>
    ///         <item>
    ///             Otherwise the tokens are matched to the model's arguments. A missing required
    ///             argument, a value that does not convert to its property type, or an invalid
    ///             nested verb makes the result <see cref="ConsoleParseOutcomeEnum.Invalid" />,
    ///             with one error per problem. Tokens nothing claimed go to the property marked
    ///             with <see cref="UnknownConsoleArgumentsAttribute" />, if any.
    ///         </item>
    ///         <item>
    ///             An exception while creating or filling the model makes the result
    ///             <see cref="ConsoleParseOutcomeEnum.Invalid" /> with
    ///             <see cref="ConsoleParseResult.Exception" /> set.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <returns>The parse result. The model is in <see cref="ConsoleParseResult.Model" /> when it parsed.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="inputModelType" /> is null.</exception>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="inputModelType" /> does not implement
    ///     <see cref="IConsoleCommandInputModel" />. This is a programmer error, not a parse failure.
    /// </exception>
    public ConsoleParseResult GetParseResult(System.Type inputModelType)
    {
        // Ensure the type is valid for parsing; a wrong type is a bug in the caller, so it throws.
        ArgumentNullException.ThrowIfNull(inputModelType);
        if (!inputModelType.IsAssignableTo(typeof(IConsoleCommandInputModel)))
            throw new InvalidOperationException(
                "The input model type must implement the IConsoleCommandInputModel interface."
            );

        // Work on a copy so dropping the verb token leaves this collection intact for the next model.
        ConsoleCommandArgumentCollection tokens = new(this);

        // A model with a non-default verb only owns a command line that starts with that verb.
        string verb = GetRequiredVerb(inputModelType);
        if (verb is not null)
        {
            if (
                tokens.FirstOrDefault() is not { Type: ConsoleCommandArgumentType.Positional } first
                || first.Value?.Equals(verb, StringComparison.OrdinalIgnoreCase) is not true
            )
                return new(
                    ConsoleParseOutcomeEnum.NotMatched,
                    inputModelType,
                    errors: [$"The first argument must be the verb '{verb}'."]
                );

            // Drop the verb so the remaining tokens are the model's own arguments.
            tokens.RemoveAt(0);
        }

        // A help switch right after the verb (or first, without one) asks for this model's help.
        if (tokens.Count > 0 && ConsoleCommand.IsHelpRequest(tokens[0]))
            return new(
                ConsoleParseOutcomeEnum.HelpRequested,
                inputModelType,
                helpText: ConsoleCommand.GetHelpText(inputModelType)
            );

        // Any exception from here on (a constructor or setter that throws) is a failed parse, not a
        // crash: the host decides what to show.
        try
        {
            return tokens.Count is 0
                ? tokens.GetDefaultsResult(inputModelType)
                : tokens.GetMatchedResult(inputModelType);
        }
        catch (System.Exception exception)
        {
            // A setter called through reflection wraps its exception; report the setter's own one.
            System.Exception cause = exception
                is TargetInvocationException { InnerException: not null } invocation
                ? invocation.InnerException
                : exception;

            // Report the exception as the single error.
            return CreateInvalidResult(inputModelType, [cause.Message], cause);
        }
    }

    /// <summary>
    ///     This method parses the tokens into a new <typeparamref name="TInputModel" /> and reports
    ///     how it went. See <see cref="GetParseResult(System.Type)" /> for the rules.
    /// </summary>
    /// <typeparam name="TInputModel">The input model type to create and populate.</typeparam>
    /// <returns>The parse result.</returns>
    public ConsoleParseResult GetParseResult<TInputModel>()
        where TInputModel : class, IConsoleCommandInputModel => GetParseResult(typeof(TInputModel));

    /// <summary>
    ///     This method parses the tokens into a new instance of <paramref name="inputModelType" />
    ///     with <see cref="GetParseResult(System.Type)" />. It never prints and never ends the
    ///     process, and it never returns <see langword="null" />.
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <returns>The populated input model.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="inputModelType" /> is null.</exception>
    /// <exception cref="ConsoleParseException">
    ///     When the result is anything but <see cref="ConsoleParseOutcomeEnum.Parsed" />: help was
    ///     requested, the model's verb did not match, or the arguments are invalid. The exception
    ///     carries the result, including the help text to show.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    ///     When <paramref name="inputModelType" /> does not implement
    ///     <see cref="IConsoleCommandInputModel" />.
    /// </exception>
    public object Parse(System.Type inputModelType)
    {
        // Parse the tokens and localize the result.
        ConsoleParseResult result = GetParseResult(inputModelType);

        // Only a parsed result has a model; anything else is reported to the caller.
        return result.Succeeded ? result.Model : throw new ConsoleParseException(result);
    }

    /// <summary>
    ///     This method parses the tokens into a new <typeparamref name="TInputModel" />. See
    ///     <see cref="Parse(System.Type)" /> for the rules.
    /// </summary>
    /// <typeparam name="TInputModel">The input model type to create and populate.</typeparam>
    /// <returns>The populated input model.</returns>
    /// <exception cref="ConsoleParseException">
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
    ///     <paramref name="inputModelType" /> with <see cref="GetParseResult(System.Type)" />.
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <param name="model">The populated model, or <see langword="null" /> on failure.</param>
    /// <returns>
    ///     <see langword="true" /> exactly when the result is
    ///     <see cref="ConsoleParseOutcomeEnum.Parsed" />; <see langword="false" /> for any other
    ///     outcome or an invalid type.
    /// </returns>
    public bool TryParse(System.Type inputModelType, out object model)
    {
        // Try to parse the input model type, returning true if successful, otherwise false.
        try
        {
            // Parse the CLI arguments into the input model type.
            ConsoleParseResult result = GetParseResult(inputModelType);

            // The result only carries a model when it parsed.
            model = result.Model;

            // We're done, report whether a model was produced.
            return result.Succeeded;
        }
        // An invalid type is the only exception left; it is a failure like any other here.
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

    /// <summary>
    ///     This method returns the verb a command line must start with for
    ///     <paramref name="inputModelType" />: the name of its <see cref="ConsoleVerbAttribute" /> when
    ///     that verb is not the default verb and has a name, otherwise <see langword="null" />.
    /// </summary>
    /// <param name="inputModelType">The input model type to inspect.</param>
    /// <returns>The required verb, or <see langword="null" />.</returns>
    private static string GetRequiredVerb(System.Type inputModelType) =>
        inputModelType.GetCustomAttribute<ConsoleVerbAttribute>(true)
            is { Default: false, Name: { } name }
        && name.Trim() is { Length: > 0 } verb
            ? verb
            : null;

    /// <summary>
    ///     This method lists one error per required argument that no token matched, in the form the
    ///     help text preface uses (<c>Missing argument: -x, --name (description)</c>).
    /// </summary>
    /// <param name="namedArguments">The named arguments of the model.</param>
    /// <param name="positionalArguments">The positional arguments of the model.</param>
    /// <returns>The errors, possibly empty.</returns>
    private static List<string> GetMissingArgumentErrors(
        IEnumerable<ParsedConsoleArgument> namedArguments,
        IEnumerable<ParsedConsoleArgument> positionalArguments
    ) =>
        positionalArguments
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

    /// <summary>
    ///     This method builds the error for a matched argument whose value does not convert to its
    ///     property type, naming the argument, the value given and the expected type.
    /// </summary>
    /// <param name="argument">The argument whose tokens did not convert.</param>
    /// <returns>The error line.</returns>
    private static string GetConversionError(ParsedConsoleArgument argument)
    {
        // Name the argument the way the user typed it.
        string name = argument.Attribute switch
        {
            NamedConsoleArgumentAttribute { ShortName: not null } named =>
                $"-{named.ShortName}, --{named.Name}",
            NamedConsoleArgumentAttribute named => $"--{named.Name}",
            PositionalConsoleArgumentAttribute positional => $"<{positional.Position}>",
            _ => argument.Property?.Name,
        };

        // A repeated argument is converted as one joined value, so show every value given.
        string value = string.Join(",", argument.Arguments.Select(a => a.Value));

        // Name the underlying type of a nullable property, which is what the value had to be.
        System.Type expected =
            Nullable.GetUnderlyingType(argument.Property.PropertyType)
            ?? argument.Property.PropertyType;

        // We're done, return the error line.
        return $"Invalid value '{value}' for {name}: expected {expected.Name}.";
    }

    /// <summary>
    ///     This method creates an <see cref="ConsoleParseOutcomeEnum.Invalid" /> result whose help
    ///     text is the errors followed by the model's help, the text a user needs to fix the command.
    /// </summary>
    /// <param name="inputModelType">The input model type that was parsed.</param>
    /// <param name="errors">One line per problem.</param>
    /// <param name="exception">The unexpected exception behind the failure, if any.</param>
    /// <returns>The invalid result.</returns>
    private static ConsoleParseResult CreateInvalidResult(
        System.Type inputModelType,
        IReadOnlyCollection<string> errors,
        System.Exception exception = null
    ) =>
        new(
            ConsoleParseOutcomeEnum.Invalid,
            inputModelType,
            errors: errors,
            helpText: ConsoleCommandInputModel.GetHelpText(
                inputModelType,
                string.Join(Environment.NewLine, errors)
            ),
            exception: exception
        );

    /// <summary>
    ///     This method builds the model from attribute defaults alone, for a command line with no
    ///     tokens left after the verb. Defaults are applied only where an attribute defines one (and
    ///     to nested verbs), so property initializers are not overwritten.
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <returns>A parsed result, or an invalid one listing the missing required arguments.</returns>
    private ConsoleParseResult GetDefaultsResult(System.Type inputModelType)
    {
        // Localize the model's arguments; with no tokens none of them is found.
        List<ParsedConsoleArgument> namedArguments = ParseNamedArguments(inputModelType);
        List<ParsedConsoleArgument> nestedVerbs = ParseNestedVerbs(inputModelType);
        List<ParsedConsoleArgument> positionalArguments = ParsePositionalArguments(inputModelType);

        // A required argument cannot be satisfied by a default.
        List<string> missing = GetMissingArgumentErrors(namedArguments, positionalArguments);
        if (missing.Count > 0)
            return CreateInvalidResult(inputModelType, missing);

        // Instantiate the model to receive its defaults.
        object inputModel = inputModelType.CreateInstance();

        // Apply attribute defaults only where one is defined (or for nested verbs), so property
        // initializers are not overwritten.
        foreach (
            ParsedConsoleArgument argument in namedArguments
                .Concat(nestedVerbs)
                .Concat(positionalArguments)
        )
            if (
                argument.Attribute is NestedConsoleVerbAttribute
                || argument.GetAttribute<NamedConsoleArgumentAttribute>()?.Default is not null
                || argument.GetAttribute<PositionalConsoleArgumentAttribute>()?.Default is not null
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

        // We're done, return the model built from its defaults.
        return new(
            ConsoleParseOutcomeEnum.Parsed,
            inputModelType,
            inputModel as IConsoleCommandInputModel
        );
    }

    /// <summary>
    ///     This method matches the tokens to the model's arguments, converts them and fills a new
    ///     model. Every problem is collected, so one result lists them all.
    /// </summary>
    /// <param name="inputModelType">The input model type to create and populate.</param>
    /// <returns>A parsed, help-requested (from a nested verb) or invalid result.</returns>
    private ConsoleParseResult GetMatchedResult(System.Type inputModelType)
    {
        // Localize the model's arguments and the tokens each one matched.
        List<ParsedConsoleArgument> namedArguments = ParseNamedArguments(inputModelType);
        List<ParsedConsoleArgument> nestedVerbs = ParseNestedVerbs(inputModelType);
        List<ParsedConsoleArgument> positionalArguments = ParsePositionalArguments(inputModelType);

        // Collect every problem instead of stopping at the first.
        List<string> errors = [];

        // Parse each nested verb that was given from the tokens after it. This runs first so a help
        // request inside the nested verb wins over the parent's own missing arguments.
        Dictionary<ParsedConsoleArgument, IConsoleCommandInputModel> nestedModels = [];
        foreach (ParsedConsoleArgument nestedVerb in nestedVerbs.Where(v => v.Found))
        {
            // Localize the nested verb's name for its errors.
            string name = nestedVerb.GetAttribute<NestedConsoleVerbAttribute>().Name;

            // Parse the tokens that follow the nested verb token into the nested model.
            ConsoleParseResult nestedResult = new ConsoleCommandArgumentCollection(
                this.Where(a => a.Index > nestedVerb.Arguments[0].Index)
            ).GetParseResult(nestedVerb.Property.PropertyType);

            // Help for the nested verb is the answer for the whole command line.
            if (nestedResult.Outcome is ConsoleParseOutcomeEnum.HelpRequested)
                return new(
                    ConsoleParseOutcomeEnum.HelpRequested,
                    inputModelType,
                    helpText: nestedResult.HelpText
                );

            // Keep the nested model, or report its errors under the nested verb's name.
            if (nestedResult.Succeeded)
                nestedModels[nestedVerb] = nestedResult.Model;
            else
                errors.AddRange(
                    nestedResult
                        .Errors.DefaultIfEmpty("The arguments are not valid.")
                        .Select(e => $"{name}: {e}")
                );
        }

        // A required argument no token matched is an error.
        errors.AddRange(GetMissingArgumentErrors(namedArguments, positionalArguments));

        // Instantiate the model to receive the values.
        object inputModel = inputModelType.CreateInstance();

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

        // Convert and set each named and positional argument. A value the user typed that does not
        // convert is an error, never a silent default: running with a value nobody typed is worse
        // than saying what was rejected.
        foreach (ParsedConsoleArgument argument in namedArguments.Concat(positionalArguments))
            if (argument.TryGetParsedValue(this, out object value))
                argument.Property?.SetValue(inputModel, value);
            else
                errors.Add(GetConversionError(argument));

        // Set each nested verb: the model parsed above, or its default when the verb was not given.
        foreach (ParsedConsoleArgument nestedVerb in nestedVerbs)
            if (nestedModels.TryGetValue(nestedVerb, out IConsoleCommandInputModel nestedModel))
                nestedVerb.Property?.SetValue(inputModel, nestedModel);
            else if (!nestedVerb.Found)
                nestedVerb.SetValue(inputModel, this);

        // Any problem makes the whole parse invalid; a half-filled model is never returned.
        return errors.Count > 0
            ? CreateInvalidResult(inputModelType, errors)
            : new(
                ConsoleParseOutcomeEnum.Parsed,
                inputModelType,
                inputModel as IConsoleCommandInputModel
            );
    }
}
