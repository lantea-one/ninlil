using System.Diagnostics.CodeAnalysis;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute names the verb (command) that an input model class handles. The first
///     command-line token must match the name for the model to be chosen, unless the verb is the
///     default verb. The description appears in the help text.
/// </summary>
/// <param name="name">The name of the verb, which is used to invoke the command.</param>
/// <param name="description">The description of the verb, which is displayed in the help text.</param>
/// <param name="isDefault">
///     Whether this verb is the default, which the model accepts without the verb token.
/// </param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class ConsoleVerbAttribute(
    [NotNull] string name,
    string description = null,
    bool isDefault = false
) : ConsoleArgumentAttribute
{
    /// <summary>
    ///     This field indicates whether this verb is the default verb for the console application.
    /// </summary>
    public readonly bool Default = isDefault;

    /// <summary>
    ///     This field contains the description of the verb, which is displayed in the help text.
    /// </summary>
    public readonly string Description = description?.Trim();

    /// <summary>
    ///     This field contains the verb's name, with leading dashes and whitespace removed.
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
