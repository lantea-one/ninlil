using System.Reflection;
using System.Xml.Serialization;
using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Service;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class represents a collection of console command arguments, which is used to store and manage the
///     arguments that can be passed to a command-line application when executing a command. It inherits from
///     the <see cref="List{T}" /> class, allowing it to function as a standard list of
///     <see cref="ConsoleCommandArgument" /> instances, while also providing additional functionality specific to
///     console command arguments if needed in the future. The <see cref="ConsoleCommandArgumentCollection" /> can be
///     used to easily access and manipulate the collection of arguments when executing a command-line application.
/// </summary>
[XmlInclude(typeof(List<ConsoleCommandArgument>))]
[XmlRoot("consoleArgumentCollection")]
public class ConsoleCommandArgumentCollection : List<ConsoleCommandArgument>
{
    /// <summary>
    ///     This implicit operator allows for the conversion of a string array to a
    ///     <see cref="ConsoleCommandArgumentCollection" /> by parsing the array of strings as command-line arguments
    ///     and creating a new instance of <see cref="ConsoleCommandArgumentCollection" /> with the parsed arguments.
    ///     This provides a convenient way to create a <see cref="ConsoleCommandArgumentCollection" /> directly from
    ///     an array of strings, such as when receiving command-line input or when working with predefined sets of
    ///     arguments in code, without needing to manually instantiate the collection and add each argument
    ///     individually. The implicit operator simplifies the process of converting raw string input into a
    ///     structured collection of console command arguments that can be easily accessed and manipulated when
    ///     executing a command-line application.
    /// </summary>
    /// <param name="arguments">
    ///     The array of strings representing the command-line arguments to
    ///     be parsed into a <see cref="ConsoleCommandArgumentCollection" />.
    /// </param>
    /// <returns>
    ///     The <see cref="ConsoleCommandArgumentCollection" /> containing the parsed console command arguments.
    /// </returns>
    public static implicit operator ConsoleCommandArgumentCollection(string[] arguments) =>
        new(arguments);

    /// <summary>
    ///     This implicit operator allows for the conversion of a list of strings to a
    ///     <see cref="ConsoleCommandArgumentCollection" /> by parsing the list of strings as command-line arguments
    ///     and creating a new instance of <see cref="ConsoleCommandArgumentCollection" /> with the parsed arguments.
    ///     This provides a convenient way to create a <see cref="ConsoleCommandArgumentCollection" /> directly from
    ///     a list of strings, such as when receiving command-line input or when working with predefined sets of
    ///     arguments in code, without needing to manually instantiate the collection and add each argument
    ///     individually. The implicit operator simplifies the process of converting raw string input into a structured
    ///     collection of console command arguments that can be easily accessed and manipulated when executing a
    ///     command-line application.
    /// </summary>
    /// <param name="arguments">
    ///     The list of strings representing the command-line arguments to
    ///     be parsed into a <see cref="ConsoleCommandArgumentCollection" />.
    /// </param>
    /// <returns>
    ///     The <see cref="ConsoleCommandArgumentCollection" /> containing the parsed console command arguments.
    /// </returns>
    public static implicit operator ConsoleCommandArgumentCollection(List<string> arguments) =>
        new(arguments);

    /// <summary>
    ///     This implicit operator allows for the conversion of a single string to a
    ///     <see cref="ConsoleCommandArgumentCollection" /> by parsing the string as command-line input and creating
    ///     a new instance of <see cref="ConsoleCommandArgumentCollection" /> with the parsed arguments. This provides
    ///     a convenient way to create a <see cref="ConsoleCommandArgumentCollection" /> directly from a single string
    ///     of command-line input, such as when reading input from the console or from a configuration file, without
    ///     needing to manually instantiate the collection and add each argument individually. The implicit operator
    ///     simplifies the process of converting raw string input into a structured collection of console command
    ///     arguments that can be easily accessed and manipulated when executing a command-line application, especially
    ///     in scenarios where the command-line input is provided as a single string and needs to be parsed into
    ///     individual arguments for processing.
    /// </summary>
    /// <param name="input">
    ///     The raw command-line input string to be parsed into a collection of console command arguments.
    /// </param>
    /// <returns>
    ///     The <see cref="ConsoleCommandArgumentCollection" /> containing the parsed console command arguments.
    /// </returns>
    public static implicit operator ConsoleCommandArgumentCollection(string input) => new(input);

    /// <summary>
    ///     This method provides a factory for parsing a list of command-line arguments into a
    ///     <see cref="ConsoleCommandArgumentCollection" />. It takes an <see cref="IEnumerable{String}" />
    ///     representing the command-line arguments and processes them to create a structured representation
    ///     of the arguments, including handling both positional and named arguments, as well as switch arguments.
    ///     The resulting <see cref="ConsoleCommandArgumentCollection" /> can then be used to easily access the
    ///     argument values and their associated metadata when executing a command-line application.
    /// </summary>
    /// <param name="arguments">
    ///     The <see cref="IEnumerable{String}" /> representing the command-line arguments to be parsed.
    /// </param>
    /// <returns>
    ///     A <see cref="ConsoleCommandArgumentCollection" /> containing the parsed console command arguments.
    /// </returns>
    public static ConsoleCommandArgumentCollection FromCli(IEnumerable<string> arguments) =>
        new(arguments);

    /// <summary>
    ///     This method provides a factory for parsing a single string of command-line input into a
    ///     <see cref="ConsoleCommandArgumentCollection" />. It takes a single string representing the command-line
    ///     <paramref name="input" />, splits it into individual arguments based on spaces, and then processes those
    ///     arguments to create a structured representation of the arguments, including handling both positional and
    ///     named arguments, as well as switch arguments. The resulting <see cref="ConsoleCommandArgumentCollection" />
    ///     can then be used to easily access the argument values and their associated metadata when executing a
    ///     command-line application. This method is particularly useful for scenarios where the command-line input is
    ///     provided as a single string, such as when reading input from the console or from a configuration file,
    ///     allowing for convenient parsing of the input into a structured collection of console command arguments.
    /// </summary>
    /// <param name="input">
    ///     The raw command-line input string to be parsed into a collection of console command arguments.
    /// </param>
    /// <returns>
    ///     The <see cref="ConsoleCommandArgumentCollection" /> containing the parsed console command arguments.
    /// </returns>
    public static ConsoleCommandArgumentCollection FromCli(string input) => new(input);

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" /> class.
    /// </summary>
    public ConsoleCommandArgumentCollection() { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" />
    ///     class with the specified <paramref name="capacity" />, which sets the initial capacity of the
    ///     underlying list to optimize performance when the number of arguments is known in advance.
    /// </summary>
    /// <param name="capacity">The initial capacity of the collection.</param>
    public ConsoleCommandArgumentCollection(int capacity)
        : base(capacity) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" />
    ///     class with the specified <paramref name="collection" />, which allows for creating a new collection based
    ///     on an existing collection of <see cref="ConsoleCommandArgument" /> instances, effectively copying the
    ///     elements from the provided collection into the new instance. This is useful for scenarios where you want
    ///     to create a new collection that is a copy of an existing one, allowing for modifications to the new
    ///     collection without affecting the original collection. The constructor takes an
    ///     <see cref="IEnumerable{ConsoleCommandArgument}" /> as a parameter, which can be any collection that
    ///     implements the IEnumerable interface, such as a List, an array, or any other collection type that can be
    ///     enumerated. The elements from the provided collection are added to the new instance of
    ///     <see cref="ConsoleCommandArgumentCollection" />, allowing for easy initialization of the collection with
    ///     a predefined set of console command arguments.
    /// </summary>
    /// <param name="collection">
    ///     The collection of <see cref="ConsoleCommandArgument" /> instances to initialize the new collection with.
    /// </param>
    public ConsoleCommandArgumentCollection(IEnumerable<ConsoleCommandArgument> collection)
        : base(collection) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" />
    ///     class with the specified <paramref name="items" />, which allows for creating a new collection based
    ///     on an existing collection of <see cref="ConsoleCommandArgument" /> instances, effectively copying the
    ///     elements from the provided collection into the new instance. This is useful for scenarios where you want
    ///     to create a new collection that is a copy of an existing one, allowing for modifications to the new
    ///     collection without affecting the original collection. The constructor takes a variadic array of
    ///     <see cref="ConsoleCommandArgument" /> as a parameter using the params keyword, which allows for passing
    ///     a variable number of arguments directly to the constructor without needing to create an intermediate
    ///     collection. The elements from the provided array are added to the new instance of
    ///     <see cref="ConsoleCommandArgumentCollection" />, allowing for easy initialization of the collection with
    ///     a predefined set of console command arguments in a more concise and convenient manner, especially when
    ///     the number of arguments is known at compile time and can be directly specified in the constructor call.
    /// </summary>
    /// <param name="items">
    ///     The variadic array of <see cref="ConsoleCommandArgument" /> instances to initialize the new collection with.
    /// </param>
    public ConsoleCommandArgumentCollection(params ConsoleCommandArgument[] items)
        : base(items.AsEnumerable()) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" />
    ///     class by parsing the provided <paramref name="arguments" /> as command-line input. It processes the list
    ///     of strings to create a structured representation of the console command arguments, including handling both
    ///     positional and named arguments, as well as switch arguments. The resulting collection contains instances
    ///     of <see cref="ConsoleCommandArgument" /> that represent each parsed argument, allowing for easy access
    ///     to their values and associated metadata when executing a command-line application. This constructor is
    ///     particularly useful for scenarios where the command-line arguments are provided as a list of strings,
    ///     such as when receiving input from the console or from a configuration file, allowing for convenient
    ///     parsing of the input into a structured collection of console command arguments that can be easily
    ///     accessed and manipulated when executing a command-line application.
    /// </summary>
    /// <param name="arguments">
    ///     The list of strings representing the command-line arguments to be parsed into a
    ///     <see cref="ConsoleCommandArgumentCollection" />. Each string in the list is expected to represent a single
    ///     argument, which can be either a positional argument (not starting with '-') or a named argument (starting
    ///     with '-' or '--'). The constructor processes the list of arguments to create a structured representation
    ///     of the console command arguments, allowing for easy access to their values and associated metadata when
    ///     executing a command-line application. The parsing logic handles both positional and named arguments, as
    ///     well as switch arguments, to ensure that the resulting collection accurately represents the command-line
    ///     input provided by the user.
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

            // Check for an inclusive value for this named argument, which is indicated by an '=' character
            // appearing somewhere in the token itself (e.g. "--name=value" or "-n=value"). We check the
            // raw token for '=' directly rather than re-deriving it from Character/Name - at this point
            // Name still holds the un-split "name=value" remainder for a long-form argument, and Character
            // is a plain letter for a short-form one, so neither can ever be found as a prefix of the
            // dash-prefixed raw token.
            if (argument.Contains('='))
            {
                // Split the argument string on the '=' character into its dash-prefixed key and its value.
                string[] argumentParts = argument.Split('=', 2, StringSplitOptions.TrimEntries);

                // For a long-form argument (no short character), the name is the key with its leading
                // "--" stripped; a short-form argument has no separate name, only its Character.
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
                // Reset the value to true for this switch argument, as the next argument is another named argument.
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
    ///     This constructor initializes a new instance of the <see cref="ConsoleCommandArgumentCollection" />
    ///     class by parsing the provided <paramref name="input" /> string as command-line input. It splits the input
    ///     string into individual arguments based on spaces, and then processes those arguments to create a structured
    ///     representation of the console command arguments, including handling both positional and named arguments,
    ///     as well as switch arguments. The resulting collection contains instances of
    ///     <see cref="ConsoleCommandArgument" /> that represent each parsed argument, allowing for easy access to
    ///     their values and associated metadata when executing a command-line application. This constructor is
    ///     particularly useful for scenarios where the command-line input is provided as a single string, such as
    ///     when reading input from the console or from a configuration file, allowing for convenient parsing of
    ///     the input into a structured collection of console command arguments that can be easily accessed and
    ///     manipulated when executing a command-line application. The parsing logic handles both positional and named
    ///     arguments, as well as switch arguments, to ensure that the resulting collection accurately represents the
    ///     command-line input provided by the user, even when the input is given as a single string that needs to be
    ///     split into individual arguments for processing.
    /// </summary>
    /// <param name="input">
    ///     The raw command-line input string to be parsed into a collection of console command arguments. The input
    ///     string is expected to contain individual arguments separated by spaces, where each argument can be either
    ///     a positional argument (not starting with '-') or a named argument (starting with '-' or '--'). The
    ///     constructor splits the input string into individual arguments based on spaces, and then processes those
    ///     arguments to create a structured representation of the console command arguments, allowing for easy access
    ///     to their values and associated metadata when executing a command-line application.
    /// </param>
    public ConsoleCommandArgumentCollection(string input)
        : this(input?.Split(' ', StringSplitOptions.TrimEntries) ?? []) { }

    /// <summary>
    ///     This method parses the named arguments from the provided <paramref name="inputModelType" />
    ///     and returns a list of <see cref="ParsedConsoleArgument" /> instances that represent the named
    ///     arguments defined in the <paramref name="inputModelType" /> by using reflection to inspect the
    ///     properties of the input model type and their associated attributes. It filters the properties to
    ///     find those that are decorated with the <see cref="NamedConsoleArgumentAttribute" />, and then
    ///     creates a new instance of <see cref="ParsedConsoleArgument" /> for each matching property, setting
    ///     the associated arguments, attribute, and property information accordingly. The resulting list of
    ///     <see cref="ParsedConsoleArgument" /> instances can then be used to easily access the named arguments
    ///     defined in the input model type and their associated metadata when executing a command-line application.
    ///     This method is particularly useful for scenarios where the input model type defines named arguments
    ///     using attributes, allowing for convenient parsing and access to those arguments when processing
    ///     command-line input in a structured manner.
    /// </summary>
    /// <param name="inputModelType">
    ///     The <see cref="Type" /> representing the input model type that defines the named arguments using
    ///     attributes. The method uses reflection to inspect the properties of this type and their associated
    ///     attributes to identify the named arguments and create corresponding <see cref="ParsedConsoleArgument" />
    ///     instances for each matching property. The input model type is expected to have properties decorated with
    ///     the <see cref="NamedConsoleArgumentAttribute" />, which indicates that the property represents a named
    ///     argument that can be parsed from command-line input. The method processes the properties of the input
    ///     model type to create a structured representation of the named arguments defined in the type, allowing
    ///     for easy access to their values and associated metadata when executing a command-line application.
    /// </param>
    /// <returns>
    ///     The list of <see cref="ParsedConsoleArgument" /> instances that represent the named arguments defined in
    ///     the <paramref name="inputModelType" />. Each instance contains the collection of CLI arguments associated
    ///     with the named argument, the attribute associated with the named argument, and the property information of
    ///     the associated input model property that corresponds to the CLI argument. This list can be used to easily
    ///     access the named arguments defined in the input model type and their associated metadata when executing a
    ///     command-line application, allowing for convenient parsing and processing of command-line input based on
    ///     the structure defined in the <paramref name="inputModelType" />. The resulting list provides a structured
    ///     representation of the named arguments defined in the input model type, making it easier to work with those
    ///     arguments when processing command-line input in a command-line application.
    /// </returns>
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
                // Set the related arguments for this named argument by filtering the collection to
                // find arguments that match either the short name or the long name of the attribute.
                // A positional (or otherwise unmatched) argument in the collection always has a null
                // Name, so the long-name comparison is guarded with a null-conditional access instead
                // of calling .Equals directly on it - otherwise any CLI input that mixes a named
                // argument with an unmatched bare token (exactly the scenario the
                // UnknownConsoleArgumentsAttribute capture path exists for) throws a
                // NullReferenceException here.
                Arguments = new(
                    this.Where(a =>
                        a.Character == p.Attribute.ShortName
                        || a.Name?.Equals(p.Attribute.Name, StringComparison.OrdinalIgnoreCase)
                            == true
                    )
                ),

                // Set the attribute for this named argument to the attribute retrieved from the property.
                Attribute = p.Attribute,

                // Set the property information for this named argument
                // to the property retrieved from the input model type.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method parses the nested verbs from the provided <paramref name="inputModelType" /> and returns
    ///     a list of <see cref="ParsedConsoleArgument" /> instances that represent the nested verbs defined in the
    ///     <paramref name="inputModelType" /> by using reflection to inspect the properties of the input model type
    ///     and their associated attributes. It filters the properties to find those that are decorated with the
    ///     <see cref="NestedConsoleVerbAttribute" />, and then creates a new instance of
    ///     <see cref="ParsedConsoleArgument" /> for each matching property, setting the associated arguments,
    ///     attribute, and property information accordingly. The resulting list of <see cref="ParsedConsoleArgument" />
    ///     instances can then be used to easily access the nested verbs defined in the input model type and their
    ///     associated metadata when executing a command-line application. This method is particularly useful for
    ///     scenarios where the input model type defines nested verbs using attributes, allowing for convenient parsing
    ///     and access to those nested verbs when processing command-line input in a structured manner. The method
    ///     processes the properties of the input model type to create a structured representation of the nested verbs
    ///     defined in the type, allowing for easy access to their values and associated metadata when executing a
    ///     command-line application. The resulting list provides a structured representation of the nested verbs
    ///     defined in the input model type, making it easier to work with those nested verbs when processing
    ///     command-line input in a command-line application.
    /// </summary>
    /// <param name="inputModelType">
    ///     The <see cref="Type" /> representing the input model type that defines the nested verbs using attributes.
    ///     The method uses reflection to inspect the properties of this type and their associated attributes to
    ///     identify the nested verbs and create corresponding <see cref="ParsedConsoleArgument" /> instances for each
    ///     matching property. The input model type is expected to have properties decorated with the
    ///     <see cref="NestedConsoleVerbAttribute" />, which indicates that the property represents a nested verb that
    ///     can be parsed from command-line input. The method processes the properties of the input model type to
    ///     create a structured representation of the nested verbs defined in the type, allowing for easy access to
    ///     their values and associated metadata when executing a command-line application. The resulting list provides
    ///     a structured representation of the nested verbs defined in the <paramref name="inputModelType" />, making
    ///     it easier to work with those nested verbs when processing command-line input in a command-line application.
    /// </param>
    /// <returns>
    ///     The list of <see cref="ParsedConsoleArgument" /> instances that represent the nested verbs defined in the
    ///     <paramref name="inputModelType" />. Each instance contains the collection of CLI arguments associated with
    ///     the nested verb, the attribute associated with the nested verb, and the property information of the
    ///     associated input model property that corresponds to the CLI argument. This list can be used to easily
    ///     access the nested verbs defined in the input model type and their associated metadata when executing a
    ///     command-line application, allowing for convenient parsing and processing of command-line input based on the
    ///     structure defined in the <paramref name="inputModelType" />. The resulting list provides a structured
    ///     representation of the nested verbs defined in the input model type, making it easier to work with those
    ///     nested verbs when processing command-line input in a command-line application.
    /// </returns>
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
                // Set the related arguments for this named argument by filtering the collection to
                // find arguments that match either the short name or the long name of the attribute.
                Arguments = new(
                    this.Where(a =>
                        a.Value.Equals(p.Attribute.Name, StringComparison.OrdinalIgnoreCase)
                    )
                ),

                // Set the attribute for this named argument to the attribute retrieved from the property.
                Attribute = p.Attribute,

                // Set the property information for this named argument
                // to the property retrieved from the input model type.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method parses the positional arguments from the provided <paramref name="inputModelType" />
    ///     and returns a list of <see cref="ParsedConsoleArgument" /> instances that represent the positional
    ///     arguments defined in the <paramref name="inputModelType" /> by using reflection to inspect the properties
    ///     of the input model type and their associated attributes. It filters the properties to find those that
    ///     are decorated with the <see cref="PositionalConsoleArgumentAttribute" />, and then creates a new instance
    ///     of <see cref="ParsedConsoleArgument" /> for each matching property, setting the associated arguments,
    ///     attribute, and property information accordingly. The resulting list of <see cref="ParsedConsoleArgument" />
    ///     instances can then be used to easily access the positional arguments defined in the input model type and
    ///     their associated metadata when executing a command-line application. This method is particularly useful
    ///     for scenarios where the input model type defines positional arguments using attributes, allowing for
    ///     convenient parsing and access to those arguments when processing command-line input in a structured manner.
    ///     The method processes the properties of the input model type to create a structured representation of the
    ///     positional arguments defined in the type, allowing for easy access to their values and associated metadata
    ///     when executing a command-line application. The resulting list provides a structured representation of the
    ///     positional arguments defined in the input model type, making it easier to work with those arguments when
    ///     processing command-line input in a command-line application.
    /// </summary>
    /// <param name="inputModelType">
    ///     The <see cref="Type" /> representing the input model type that defines the positional arguments using
    ///     attributes. The method uses reflection to inspect the properties of this type and their associated
    ///     attributes to identify the positional arguments and create corresponding <see cref="ParsedConsoleArgument" />
    ///     instances for each matching property. The input model type is expected to have properties decorated with
    ///     the <see cref="PositionalConsoleArgumentAttribute" />, which indicates that the property represents a
    ///     positional argument that can be parsed from command-line input. The method processes the properties of the
    ///     input model type to create a structured representation of the positional arguments defined in the type,
    ///     allowing for easy access to their values and associated metadata when executing a command-line application.
    ///     The resulting list provides a structured representation of the positional arguments defined in the
    ///     <paramref name="inputModelType" />, making it easier to work with those arguments when processing
    ///     command-line input in a command-line application.
    /// </param>
    /// <returns>
    ///     The list of <see cref="ParsedConsoleArgument" /> instances that represent the positional arguments defined
    ///     in the <paramref name="inputModelType" />. Each instance contains the collection of CLI arguments
    ///     associated with the positional argument, the attribute associated with the positional argument, and the
    ///     property information of the associated input model property that corresponds to the CLI argument. This
    ///     list can be used to easily access the positional arguments defined in the <paramref name="inputModelType" />
    ///     and their associated metadata when executing a command-line application, allowing for convenient parsing and
    ///     processing of command-line input based on the structure defined in the <paramref name="inputModelType" />.
    ///     The resulting list provides a structured representation of the positional arguments defined in the
    ///     <paramref name="inputModelType" />, making it easier to work with those arguments when processing
    ///     command-line input in a command-line application.
    /// </returns>
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
                // Set the related arguments for this named argument by filtering the collection to
                // find arguments that match either the short name or the long name of the attribute.
                Arguments = new(this.Where(a => a.Position.Equals(p.Attribute.Position))),

                // Set the attribute for this named argument to the attribute retrieved from the property.
                Attribute = p.Attribute,

                // Set the property information for this named argument
                // to the property retrieved from the input model type.
                Property = p.Property,
            })
            .ToList();

    /// <summary>
    ///     This method parses the console command arguments from the collection into an instance of the specified
    ///     <paramref name="inputModelType" />. It uses reflection to inspect the properties of the input model
    ///     type and their associated attributes to identify the named arguments, nested verbs, and positional
    ///     arguments defined in the type, and then sets the corresponding properties on an instance of the input
    ///     model type based on the parsed console command arguments in the collection. The method also handles help
    ///     requests by checking for specific switches in the arguments and generating help text for the input model
    ///     type if a help request is detected. The resulting instance of the input model type is populated with the
    ///     parsed console command arguments, allowing for easy access to their values and associated metadata when
    ///     executing a command-line application. This method is particularly useful for scenarios where you want to
    ///     parse command-line input into a structured input model based on the definition of the input model type,
    ///     allowing for convenient processing of command-line arguments in a command-line application.
    /// </summary>
    /// <param name="inputModelType">
    ///     The <see cref="Type" /> representing the input model type that defines the structure of the console
    ///     command arguments using attributes. The method uses reflection to inspect the properties of this type and
    ///     their associated attributes to identify the named arguments, nested verbs, and positional arguments defined
    ///     in the type, and then sets the corresponding properties on an instance of the input model type based on the
    ///     parsed console command arguments in the collection. The input model type is expected to have properties
    ///     decorated with the <see cref="NamedConsoleArgumentAttribute" />, <see cref="NestedConsoleVerbAttribute" />,
    ///     and <see cref="PositionalConsoleArgumentAttribute" />, which indicate that the properties represent named
    ///     arguments, nested verbs, and positional arguments that can be parsed from command-line input. The method
    ///     processes the properties of the input model type to create a structured representation of the console
    ///     command arguments defined in the type, allowing for easy access to their values and associated metadata
    ///     when executing a command-line application. The resulting instance of the input model type is populated
    ///     with the parsed console command arguments, making it easier to work with those arguments when processing
    ///     command-line input in a command-line application. The method also handles help requests by checking for
    ///     specific switches in the arguments and generating help text for the input model type if a help request
    ///     is detected, allowing for convenient access to help information about the command-line arguments defined
    ///     in the input model type when processing command-line input in a command-line application.
    /// </param>
    /// <returns>
    ///     The instance of the specified <paramref name="inputModelType" /> that is populated with the parsed console
    ///     command arguments from the collection. The method uses reflection to set the corresponding properties on
    ///     the input model instance based on the parsed console command arguments, allowing for easy access to their
    ///     values and associated metadata when executing a command-line application. The resulting instance of the
    ///     input model type provides a structured representation of the console command arguments defined in the type,
    ///     making it easier to work with those arguments when processing command-line input in a command-line
    ///     application. If a help request is detected in the arguments, the method generates help text for the input
    ///     model type and exits the application, returning null as the result. If any exceptions occur during parsing,
    ///     the method catches the exception, writes it to the console in red text, and returns null as the result
    ///     allowing for graceful handling of parsing errors when processing command-line input in a command-line
    ///     application. The method ensures that the console command arguments are parsed and processed based on the
    ///     structure defined in the <paramref name="inputModelType" />, allowing for convenient access to the parsed
    ///     arguments and their associated metadata when executing a command-line application.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     When the command argument collection is null or empty, when the input model type does not implement the
    ///     <see cref="IConsoleCommandInputModel" /> interface, or when the first argument in the collection does not
    ///     match the verb name defined in the input model type (if a verb is defined). The method performs validation
    ///     checks to ensure that the command argument collection is not null or empty, that the input model type is
    ///     valid for parsing, and that the first argument in the collection matches the verb name defined in the input
    ///     model type (if a verb is defined). If any of these validation checks fail, the method throws an
    ///     <see cref="InvalidOperationException" /> with an appropriate error message indicating the reason for the
    ///     failure. This ensures that the method is used correctly and that the input provided for parsing is valid,
    ///     allowing for proper handling of command-line arguments based on the structure defined in the
    ///     <paramref name="inputModelType" /> when executing a command-line application. The method relies on these
    ///     validation checks to ensure that the parsing process can proceed successfully and that the resulting
    ///     instance of the input model type is populated with valid console command arguments, allowing for convenient
    ///     access to those arguments and their associated metadata when executing a command-line application. If any
    ///     of the validation checks fail, the method throws an exception to indicate the issue, allowing for proper
    ///     error handling and feedback to the user when processing command-line input in a command-line application.
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

            // Try JIT a default input model to send downstream.
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

                // Apply defaults only when explicitly defined (or for nested verbs), so we don't overwrite property initializers.
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

                        // Localize the type's default value/
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

                // Bubble up so TryParse can correctly report parsing failure.
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

            // If the verb is not null or empty, ensure that the first argument in the collection matches the verb name,
            // throw an exception if it doesn't, otherwise remove the first argument from the collection as it's the verb
            //  name and we don't need it for parsing.
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

        // Check the first argument for a help request. A positional first argument (e.g. a plain
        // filename passed to a verb-less, name-less model) has a null Name, and
        // SanitizeArgumentName throws for a null input by design - so the name-based comparison is
        // only attempted once we know a Name is actually present, otherwise every command whose
        // first argument happens to be positional would crash here instead of simply not matching.
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

            // We're done, exit the application entirely.
            Environment.Exit(0);

            // Return a null result.
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

            // Check for any missing or unknown arguments.
            if (missing.Any())
            {
                // Display the help text with the missing arguments.
                ConsoleCommandInputModel.HelpText(
                    inputModelType,
                    string.Join(Environment.NewLine, missing)
                );

                // We're done, exit the application entirely.
                Environment.Exit(1);

                // We're done, return a null result.
                return null;
            }

            // Reset our input model instance to a new one.
            inputModel = inputModelType.CreateInstance();

            // Localize the first property on the input model type that is decorated with the
            // UnknownConsoleArgumentsAttribute and a type of ConsoleCommandArgumentCollection, if it exists.
            // If we have an unknown arguments property, set its value to a new collection of the arguments that were
            // not parsed as named arguments, nested verbs, or positional arguments, ordered by their index in the
            // original collection.
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

            // Iterate through all the parsed arguments and set the corresponding properties on the input model instance.
            // The full collection is forwarded through so that a nested-verb argument can locate its own
            // sub-collection of CLI arguments (by index, relative to where its own verb token appeared)
            // instead of always resolving to its default value.
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
    ///     This method is a generic version of the <see cref="Parse(Type)" /> method, which allows for parsing the
    ///     console command arguments from the collection into an instance of the specified generic type parameter
    ///     <typeparamref name="TInputModel" />. It simply calls the non-generic <see cref="Parse(Type)" /> method,
    ///     passing in the type of the generic parameter, to perform the parsing and return the resulting instance
    ///     of the input model type populated with the parsed console command arguments. This method provides a
    ///     convenient way to parse the console command arguments into a strongly-typed input model instance without
    ///     having to explicitly specify the type as a parameter, allowing for more concise and readable code when
    ///     working with command-line arguments in a command-line application. The generic version of the parse method
    ///     can be used in scenarios where the type of the input model is known at compile time, providing a more
    ///     convenient and type-safe way to parse the console command arguments into an instance of the input model
    ///     type when executing a command-line application.
    /// </summary>
    /// <typeparam name="TInputModel">
    ///     The generic type parameter representing the input model type that defines the structure of the console
    ///     command arguments using attributes. The method uses reflection to inspect the properties of this type
    ///     and their associated attributes to identify the named arguments, nested verbs, and positional arguments
    ///     defined in the type, and then sets the corresponding properties on an instance of the input model type
    ///     based on the parsed console command arguments in the collection. The input model type is expected to have
    ///     properties decorated with the <see cref="NamedConsoleArgumentAttribute" />,
    ///     <see cref="NestedConsoleVerbAttribute" />, and <see cref="PositionalConsoleArgumentAttribute" />, which
    ///     indicate that the properties represent named arguments, nested verbs, and positional arguments that can
    ///     be parsed from command-line input. The method processes the properties of the input model type to create
    ///     a structured representation of the console command arguments defined in the type, allowing for easy access
    ///     to their values and associated metadata when executing a command-line application. The resulting instance
    ///     of the input model type is populated with the parsed console command arguments, making it easier to work
    ///     with those arguments when processing command-line input in a command-line application. The method also
    ///     handles help requests by checking for specific switches in the arguments and generating help text for the
    ///     input model type if a help request is detected, allowing for convenient access to help information about
    ///     the command-line arguments defined in the input model type when processing command-line input in a
    ///     command-line application. The generic version of the parse method provides a convenient way to parse the
    ///     console command arguments into a strongly-typed input model instance without having to explicitly specify
    ///     the type as a parameter, allowing for more concise and readable code when working with command-line
    ///     arguments in a command-line application. The generic type parameter is constrained to be a class that
    ///     implements the <see cref="IConsoleCommandInputModel" /> interface, ensuring that the type used for parsing
    ///     is valid and can be properly processed by the method when executing a command-line application.
    /// </typeparam>
    /// <returns>
    ///     The instance of the specified generic type parameter <typeparamref name="TInputModel" /> that is populated
    ///     with the parsed console command arguments from the collection. The method uses reflection to set the
    ///     corresponding properties on the input model instance based on the parsed console command arguments,
    ///     allowing for easy access to their values and associated metadata when executing a command-line application.
    ///     The resulting instance of the input model type provides a structured representation of the console command
    ///     arguments defined in the type, making it easier to work with those arguments when processing command-line
    ///     input in a command-line application. If a help request is detected in the arguments, the method generates
    ///     help text for the input model type and exits the application, returning null as the result. If any
    ///     exceptions occur during parsing, the method catches the exception, writes it to the console in red text,
    ///     and returns null as the result allowing for graceful handling of parsing errors when processing
    ///     command-line input in a command-line application. The method ensures that the console command arguments
    ///     are parsed and processed based on the structure defined in the <typeparamref name="TInputModel" />,
    ///     allowing for convenient access to the parsed arguments and their associated metadata when executing a
    ///     command-line application. The generic version of the parse method provides a convenient way to parse the
    ///     console command arguments into a strongly-typed input model instance without having to explicitly specify
    ///     the type as a parameter, allowing for more concise and readable code when working with command-line
    ///     arguments in a command-line application.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     When the command argument collection is null or empty, when the input model type does not implement the
    ///     <see cref="IConsoleCommandInputModel" /> interface, or when the first argument in the collection does not
    ///     match the verb name defined in the input model type (if a verb is defined). The method performs validation
    ///     checks to ensure that the command argument collection is not null or empty, that the input model type is
    ///     valid for parsing, and that the first argument in the collection matches the verb name defined in the input
    ///     model type (if a verb is defined). If any of these validation checks fail, the method throws an
    ///     <see cref="InvalidOperationException" /> with an appropriate error message indicating the reason for the
    ///     failure. This ensures that the method is used correctly and that the input provided for parsing is valid,
    ///     allowing for proper handling of command-line arguments based on the structure defined in the
    ///     <typeparamref name="TInputModel" /> when executing a command-line application. The method relies on these
    ///     validation checks to ensure that the parsing process can proceed successfully and that the resulting
    ///     instance of the input model type is populated with valid console command arguments, allowing for convenient
    ///     access to those arguments and their associated metadata when executing a command-line application. If any
    ///     of the validation checks fail, the method throws an exception to indicate the issue, allowing for proper
    ///     error handling and feedback to the user when processing command-line input in a command-line application.
    /// </exception>
    public IConsoleCommandInputModel Parse<TInputModel>()
        where TInputModel : class, IConsoleCommandInputModel =>
        Parse(typeof(TInputModel)) as TInputModel;

    /// <summary>
    ///     This method returns a string representation of the console command argument collection, which includes the
    ///     string representations of each individual console command argument in the collection. It filters out any
    ///     null arguments and arguments that do not have a valid character, name, or position, and then concatenates
    ///     the string representations of the remaining arguments into a single string, separated by spaces.
    ///     This provides a convenient way to visualize the contents of the collection, especially for debugging or
    ///     logging purposes, allowing for a clear and concise representation of the console command arguments
    ///     contained within the collection. The resulting string can be used to easily understand the structure and
    ///     values of the arguments in the collection when executing a command-line application.
    /// </summary>
    /// <returns>
    ///     The string representation of the console command argument collection, which includes the string
    ///     representations of each individual console command argument in the collection, filtered to include only
    ///     valid arguments and concatenated into a single string separated by spaces.
    /// </returns>
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
    ///     This method attempts to parse the console command arguments from the collection into an instance of the
    ///     specified <paramref name="inputModelType" />. It calls the <see cref="Parse(Type)" /> method to perform
    ///     the parsing and returns true if the parsing was successful, along with the resulting instance of the input
    ///     model type populated with the parsed console command arguments. If any exceptions occur during parsing,
    ///     the method catches the exception, sets the output model to null, and returns false to indicate that
    ///     parsing failed. This method provides a convenient way to attempt parsing the console command arguments
    ///     into an instance of the specified input model type without having to handle exceptions directly, allowing
    ///     for more graceful error handling when working with command-line arguments in a command-line application.
    ///     The method ensures that the console command arguments are parsed and processed based on the structure
    ///     defined in the <paramref name="inputModelType" />, allowing for convenient access to the parsed arguments
    ///     and their associated metadata when executing a command-line application.
    /// </summary>
    /// <param name="inputModelType">
    ///     The <see cref="Type" /> representing the input model type that defines the structure of the console command
    ///     arguments using attributes. The method uses reflection to inspect the properties of this type and their
    ///     associated attributes to identify the named arguments, nested verbs, and positional arguments defined in
    ///     the type, and then sets the corresponding properties on an instance of the input model type based on the
    ///     parsed console command arguments in the collection. The input model type is expected to have properties
    ///     decorated with the <see cref="NamedConsoleArgumentAttribute" />, <see cref="NestedConsoleVerbAttribute" />,
    ///     and <see cref="PositionalConsoleArgumentAttribute" />, which indicate that the properties represent named
    ///     arguments, nested verbs, and positional arguments that can be parsed from command-line input.
    /// </param>
    /// <param name="model">
    ///     The output parameter that will hold the resulting instance of the input model type populated with the
    ///     parsed console command arguments if parsing is successful. If any exceptions occur during parsing, this
    ///     output parameter will be set to null, and the method will return false to indicate that parsing failed.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the parsing was successful and the resulting instance of the input model type
    ///     is populated with the parsed console command arguments; otherwise, <see langword="false" /> if any
    ///     exceptions occur during parsing, in which case the output model will be set to <see langword="null" />.
    /// </returns>
    public bool TryParse(System.Type inputModelType, out object model)
    {
        // Try to parse the input model type, returning true if successful, otherwise false.
        try
        {
            // Parse the CLI arguments into the input model type.
            model = Parse(inputModelType);

            // Return true if the model is not null, indicating that parsing was successful.
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
    ///     This method attempts to parse the console command arguments from the collection into an instance of the
    ///     specified <typeparamref name="TInputModel" />. It calls the <see cref="Parse(Type)" /> method to perform
    ///     the parsing and returns true if the parsing was successful, along with the resulting instance of the input
    ///     model type populated with the parsed console command arguments. If any exceptions occur during parsing,
    ///     the method catches the exception, sets the output model to null, and returns false to indicate that
    ///     parsing failed. This method provides a convenient way to attempt parsing the console command arguments
    ///     into an instance of the specified input model type without having to handle exceptions directly, allowing
    ///     for more graceful error handling when working with command-line arguments in a command-line application.
    ///     The method ensures that the console command arguments are parsed and processed based on the structure
    ///     defined in the <typeparamref name="TInputModel" />, allowing for convenient access to the parsed arguments
    ///     and their associated metadata when executing a command-line application.
    /// </summary>
    /// <param name="model">
    ///     The output parameter that will hold the resulting instance of the input model type populated with the
    ///     parsed console command arguments if parsing is successful. If any exceptions occur during parsing, this
    ///     output parameter will be set to null, and the method will return false to indicate that parsing failed.
    /// </param>
    /// <typeparam name="TInputModel">
    ///     The generic type parameter representing the input model type that defines the structure of the console
    ///     command arguments using attributes. The method uses reflection to inspect the properties of this type and
    ///     their associated attributes to identify the named arguments, nested verbs, and positional arguments defined
    ///     in the type, and then sets the corresponding properties on an instance of the input model type based on the
    ///     parsed console command arguments in the collection. The input model type is expected to have properties
    ///     decorated with the <see cref="NamedConsoleArgumentAttribute" />, <see cref="NestedConsoleVerbAttribute" />,
    ///     and <see cref="PositionalConsoleArgumentAttribute" />, which indicate that the properties represent named
    ///     arguments, nested verbs, and positional arguments that can be parsed from command-line input. The method
    ///     processes the properties of the input model type to create a structured representation of the console
    ///     command arguments defined in the type, allowing for easy access to their values and associated metadata
    ///     when executing a command-line application. The resulting instance of the input model type is populated with
    ///     the parsed console command arguments, making it easier to work with those arguments when processing
    ///     command-line input in a command-line application.
    /// </typeparam>
    /// <returns>
    ///     <see langword="true" /> if the parsing was successful and the resulting instance of the input model type
    ///     is populated with the parsed console command arguments; otherwise, <see langword="false" /> if any
    ///     exceptions occur during parsing, in which case the output model will be set to <see langword="null" />.
    /// </returns>
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
