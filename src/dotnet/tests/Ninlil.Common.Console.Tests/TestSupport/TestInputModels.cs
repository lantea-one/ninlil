using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.TestSupport;

/// <summary>
///     This model exercises basic named-argument parsing: a required string, an optional short/long
///     named integer with a default, and a trailing boolean switch with a default.
/// </summary>
public class SimpleNamedArgsModel : ConsoleCommandInputModel
{
    /// <summary>The required name argument.</summary>
    [NamedConsoleArgument("name", "The name of the thing.")]
    public string Name { get; set; }

    /// <summary>The optional age argument, with a short name and a default value.</summary>
    [NamedConsoleArgument('a', "age", "The age of the thing.", false, 0)]
    public int Age { get; set; }

    /// <summary>The optional verbose switch, defaulting to false.</summary>
    [NamedConsoleArgument('v', "verbose", "Whether to enable verbose output.", false, false)]
    public bool Verbose { get; set; }
}

/// <summary>
///     This model exercises positional-argument parsing: a required source followed by an optional
///     destination carrying a string default value.
/// </summary>
public class PositionalArgsModel : ConsoleCommandInputModel
{
    /// <summary>The required source positional argument.</summary>
    [PositionalConsoleArgument(0, "The source file.")]
    public string Source { get; set; }

    /// <summary>The optional destination positional argument, defaulting to "out.txt".</summary>
    [PositionalConsoleArgument(1, "The destination file.", false, "out.txt")]
    public string Destination { get; set; }
}

/// <summary>
///     This model exercises verb-attribute parsing: a non-default verb whose name must match the
///     first CLI argument, combined with a single required named argument.
/// </summary>
[ConsoleVerb("add", "Adds a new item.")]
public class VerbModel : ConsoleCommandInputModel
{
    /// <summary>The required name of the item to add.</summary>
    [NamedConsoleArgument("name", "The name of the item to add.")]
    public string Name { get; set; }
}

/// <summary>
///     This model exercises the default-verb behavior, where the first CLI argument need not match
///     the verb's name because <see cref="ConsoleVerbAttribute.Default" /> is set.
/// </summary>
[ConsoleVerb("run", "The default command.", true)]
public class DefaultVerbModel : ConsoleCommandInputModel
{
    /// <summary>The required target of the default command.</summary>
    [NamedConsoleArgument("target", "The target to run.")]
    public string Target { get; set; }
}

/// <summary>
///     This model is the argument shape of a nested verb: one optional named value, used to check
///     that a parent model's nested-verb property is populated.
/// </summary>
public class ChildInputModel : ConsoleCommandInputModel
{
    /// <summary>The optional value carried by the nested child command.</summary>
    [NamedConsoleArgument("value", "The child value.", false, "default-child-value")]
    public string Value { get; set; }
}

/// <summary>
///     This model exercises nested-verb parsing: a single <see cref="NestedConsoleVerbAttribute" />
///     property that should be populated with a parsed <see cref="ChildInputModel" /> whenever the
///     "child" token appears among the CLI arguments.
/// </summary>
public class ParentWithNestedVerbModel : ConsoleCommandInputModel
{
    /// <summary>The nested child command, matched by the literal token "child".</summary>
    [NestedConsoleVerb("child", "The nested child command.")]
    public ChildInputModel Child { get; set; }
}

/// <summary>
///     This model exercises typed-value parsing for collection-shaped named arguments (lists,
///     dictionaries and tuples), which relies on the type-inspection and conversion helpers in
///     Ninlil.Common.Extension.
/// </summary>
public class TypedArgsModel : ConsoleCommandInputModel
{
    /// <summary>An optional list-typed named argument.</summary>
    [NamedConsoleArgument("tags", "The tags to apply.", false)]
    public List<string> Tags { get; set; }

    /// <summary>An optional dictionary-typed named argument.</summary>
    [NamedConsoleArgument("counts", "The named counts.", false)]
    public Dictionary<string, int> Counts { get; set; }

    /// <summary>An optional tuple-typed named argument.</summary>
    [NamedConsoleArgument("pair", "A key/value pair.", false)]
    public Tuple<string, int> Pair { get; set; }
}

/// <summary>
///     This model exercises the <see cref="UnknownConsoleArgumentsAttribute" />, capturing any CLI
///     arguments that don't match a declared named/positional/nested-verb property.
/// </summary>
public class UnknownArgsModel : ConsoleCommandInputModel
{
    /// <summary>The single declared, required named argument.</summary>
    [NamedConsoleArgument("known", "The known argument.")]
    public string Known { get; set; }

    /// <summary>The collection of CLI arguments that didn't match any declared argument.</summary>
    [UnknownConsoleArguments]
    public ConsoleCommandArgumentCollection Remaining { get; set; }
}

/// <summary>
///     This model declares no required arguments and no verb, so it can be parsed from an empty
///     CLI argument list without reaching the process-exit path a required argument or verb would
///     take.
/// </summary>
public class AllOptionalModel : ConsoleCommandInputModel
{
    /// <summary>An optional named argument with a default.</summary>
    [NamedConsoleArgument("label", "An optional label.", false, "default-label")]
    public string Label { get; set; }
}
