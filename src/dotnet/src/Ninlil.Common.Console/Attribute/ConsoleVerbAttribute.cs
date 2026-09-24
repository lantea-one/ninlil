using System.Diagnostics.CodeAnalysis;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This class maintains the metadata for a console verb, which is used to define commands in a console application.
/// </summary>
/// <param name="name">The name of the verb, which is used to invoke the command.</param>
/// <param name="description">The description of the verb, which is displayed in the help text.</param>
/// <param name="isDefault">
///     Optional <see cref="bool" /> indicating whether this verb
///     is the default verb for the console application or not.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ConsoleVerbAttribute(
    [NotNull] string name,
    string description = null,
    bool isDefault = false
) : ConsoleArgumentAttribute
{
    /// <summary>
    ///     This property indicates whether this verb is the default verb for the console application.
    /// </summary>
    public readonly bool Default = isDefault;

    /// <summary>
    ///     This property contains the description of the verb, which is displayed in the help text.
    /// </summary>
    public readonly string Description = description?.Trim();

    /// <summary>
    ///     This property contains the name of the verb, which is used to invoke the command in the console application.
    /// </summary>
    public readonly string Name =
        NamedConsoleArgumentAttribute.SanitizeArgumentName(name)
        ?? throw new ArgumentNullException(
            nameof(name),
            "A Verb must have a name, but the provided name was null or empty."
        );

    /// <summary>
    ///     This method converts the instance into its help text model representation.
    /// </summary>
    /// <returns>The <see cref="ConsoleHelpTextModel" /> representation of the instance.</returns>
    public ConsoleHelpTextModel ToHelpTextModel() =>
        new()
        {
            Description = Description,
            IsDefault = Default,
            Name = Name,
            Verb = true,
        };
}
