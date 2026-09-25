using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute marks an input model property as a positional argument. The position is the
///     token's index in the raw argument list, so the verb and any named arguments (with their
///     values) before it count towards it.
/// </summary>
/// <param name="position">The index position of the console argument in the command line.</param>
/// <param name="description">The description of the console argument to be displayed in the help text.</param>
/// <param name="required">
///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
/// </param>
/// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
/// <param name="group">Optional group name for the console argument.</param>
/// <exception cref="ArgumentOutOfRangeException">When the <paramref name="position" /> is negative.</exception>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class PositionalConsoleArgumentAttribute(
    int position,
    string description = null,
    bool required = true,
    object defaultValue = null,
    string group = null
) : ConsoleArgumentAttribute
{
    /// <summary>
    ///     This field contains the default value for the console argument.
    /// </summary>
    public readonly object Default = defaultValue;

    /// <summary>
    ///     This field contains the description of the console argument to be displayed in the help text.
    /// </summary>
    public readonly string Description = description?.Trim();

    /// <summary>
    ///     This field contains the group name for the console argument, which can be used to categorize related arguments.
    /// </summary>
    public readonly string Group = group?.Trim();

    /// <summary>
    ///     This field contains the index position of the console argument in the command line.
    /// </summary>
    public readonly int Position =
        position >= 0
            ? position
            : throw new ArgumentOutOfRangeException(
                nameof(position),
                "Position must be a non-negative integer."
            );

    /// <summary>
    ///     This field indicates whether the console argument is required.
    /// </summary>
    public readonly bool Required = required;

    /// <summary>
    ///     This method converts the instance into its help text model representation.
    /// </summary>
    /// <returns>The <see cref="ConsoleHelpTextModel" /> representation of the instance.</returns>
    public ConsoleHelpTextModel ToHelpTextModel() =>
        new()
        {
            Default = Default,
            Description = Description,
            Group = Group,
            Position = Position,
            Required = Required,
        };
}

/// <summary>
///     This attribute is the generic form of <see cref="PositionalConsoleArgumentAttribute" />.
///     The type parameter documents the argument's value type; parsing uses the property type.
/// </summary>
/// <param name="position">The index position of the console argument in the command line.</param>
/// <param name="description">The description of the console argument to be displayed in the help text.</param>
/// <param name="required">
///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
/// </param>
/// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
/// <param name="group">Optional group name for the console argument.</param>
/// <exception cref="ArgumentOutOfRangeException">When the <paramref name="position" /> is negative.</exception>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class PositionalConsoleArgumentAttribute<TValue>(
    int position,
    string description = null,
    bool required = true,
    object defaultValue = null,
    string group = null
) : PositionalConsoleArgumentAttribute(position, description, required, defaultValue, group);
