using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Enum;

/// <summary>
///     This enumeration represents the different types of console command arguments that can be used in a command-line
///     interface. Each value in the enumeration corresponds to a specific type of argument, which can be used to
///     define the structure and behavior of console commands. The types include named arguments, nested verbs,
///     positional arguments, and verbs, each serving a distinct purpose in how commands are structured and executed.
///     This enumeration is essential for parsing and handling console command input effectively, allowing for a
///     flexible and user-friendly command-line experience.
/// </summary>
[DefaultValue(nameof(Unknown))]
[JsonConverter(typeof(JsonStringEnumConverter))]
[XmlType("consoleCommandArgumentType")]
public enum ConsoleCommandArgumentType
{
    /// <summary>
    ///     This value indicates that the console command argument type is unknown, which means that the type of the
    ///     argument has not been specified or recognized. This can occur when the argument does not match any of the
    ///     defined types in the enumeration, or when there is an error in parsing the command-line input. When an
    ///     argument is classified as unknown, it may require special handling or may be ignored depending on the
    ///     context of the command-line application. It is important to handle unknown arguments gracefully to ensure
    ///     that the application can continue to function properly and provide feedback to the user about any
    ///     unrecognized input.
    /// </summary>
    [JsonStringEnumMemberName("unknown")]
    [XmlEnum("unknown")]
    [YamlMember(Alias = "unknown")]
    Unknown,

    /// <summary>
    ///     This value indicates that the console command argument is a named argument, which means it is specified
    ///     with a name or key followed by its value. Named arguments are typically used to provide more clarity and
    ///     flexibility in command-line interfaces, allowing users to specify arguments in any order and making it
    ///     easier to understand the purpose of each argument. For example, a named argument might be specified as
    ///     "--name John" or "-n John", where "name" is the key and "John" is the value.
    /// </summary>
    [JsonStringEnumMemberName("named")]
    [XmlEnum("named")]
    [YamlMember(Alias = "named")]
    Named,

    /// <summary>
    ///     This value indicates that the console command argument is a nested verb, which means it is a sub-command or
    ///     a secondary command that is nested within a primary command. Nested verbs are often used in command-line
    ///     interfaces to create a hierarchical structure of commands, allowing for more complex and organized command
    ///     structures. For example, in a command-line tool for managing a database, you might have a primary command
    ///     called "database" and a nested verb called "backup", which would be invoked as "database backup". This
    ///     allows users to easily navigate and execute specific actions within the command-line interface while
    ///     maintaining a clear and logical command structure.
    /// </summary>
    [JsonStringEnumMemberName("nestedVerb")]
    [XmlEnum("nestedVerb")]
    [YamlMember(Alias = "nestedVerb")]
    NestedVerb,

    /// <summary>
    ///     This value indicates that the console command argument is a positional argument, which means it is
    ///     specified based on its position in the command-line input rather than being associated with a specific
    ///     name or key. Positional arguments are typically used when the order of the arguments is important and when
    ///     there is a clear and consistent structure to the command-line input. For example, in a command-line tool
    ///     for copying files, you might have a command that takes two positional arguments: the source file and the
    ///     destination file. The command might be invoked as "copy source.txt destination.txt", where "source.txt" is
    ///     the first positional argument and "destination.txt" is the second positional argument. Positional arguments
    ///     can be convenient for simple commands with a fixed number of arguments, but they may require users to
    ///     remember the correct order of the arguments, which can lead to confusion if there are many arguments or
    ///     if the command structure is complex.
    /// </summary>
    [JsonStringEnumMemberName("positional")]
    [XmlEnum("positional")]
    [YamlMember(Alias = "positional")]
    Positional,

    /// <summary>
    ///     This value indicates that the console command argument is a verb, which means it represents an action
    ///     or command that the user wants to execute. Verbs are typically used in command-line interfaces to specify
    ///     the primary action that the user wants to perform, and they often serve as the main entry point for
    ///     executing a command. For example, in a command-line tool for managing a to-do list, you might have verbs
    ///     such as "add", "remove", and "list", which would be invoked as "todo add", "todo remove", and "todo list".
    ///     Verbs help to clearly indicate the intended action and can be used to organize commands into a logical
    ///     structure, making it easier for users to understand and navigate the command-line interface.
    /// </summary>
    [JsonStringEnumMemberName("verb")]
    [XmlEnum("verb")]
    [YamlMember(Alias = "verb")]
    Verb,
}
