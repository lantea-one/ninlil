using System.Diagnostics.CodeAnalysis;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This class maintains the metadata for a nested console verb,
///     which is used to define commands in a console application.
/// </summary>
/// <param name="name">The name of the verb, which is used to invoke the command.</param>
/// <param name="description">The description of the verb, which is displayed in the help text.</param>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NestedConsoleVerbAttribute([NotNull] string name, string description = null)
    : ConsoleVerbAttribute(name, description);
