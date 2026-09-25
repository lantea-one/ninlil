using System.Diagnostics.CodeAnalysis;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute marks an input model property as a nested verb (a sub-command). When the
///     command line contains the verb's name, the tokens after it are parsed into a new instance
///     of the property's type; otherwise the property gets a default instance.
/// </summary>
/// <param name="name">The name of the verb, which is used to invoke the command.</param>
/// <param name="description">The description of the verb, which is displayed in the help text.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NestedConsoleVerbAttribute([NotNull] string name, string description = null)
    : ConsoleVerbAttribute(name, description);
