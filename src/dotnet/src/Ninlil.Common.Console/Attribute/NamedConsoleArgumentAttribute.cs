using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Attribute;

/// <summary>
///     This attribute is used to define console arguments for a command-line application.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NamedConsoleArgumentAttribute : ConsoleArgumentAttribute
{
    /// <summary>
    ///     This method sanitizes the argument <paramref name="name" />
    ///     by removing leading dashes and trimming whitespace.
    /// </summary>
    /// <param name="name">The argument name to sanitize.</param>
    /// <returns>The sanitized argument <paramref name="name" />, or null if the input is null or empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the <paramref name="name" /> is null or empty.</exception>
    public static string SanitizeArgumentName(string name) =>
        name?.Trim() is null or ""
            ? throw new ArgumentNullException(nameof(name), "Console Arguments must have a name.")
        : name.StartsWith("--") ? name[2..].Trim()
        : name.StartsWith("-") ? name[1..].Trim()
        : name.Trim();

    /// <summary>
    ///     This property contains the default value for the console argument.
    /// </summary>
    public readonly object Default;

    /// <summary>
    ///     This property contains the description of the console argument to be displayed in the help text.
    /// </summary>
    public readonly string Description;

    /// <summary>
    ///     This property contains the group name of the console argument, if applicable.
    /// </summary>
    public readonly string Group;

    /// <summary>
    ///     This property is used to set the name of the console argument.
    /// </summary>
    public readonly string Name;

    /// <summary>
    ///     This property indicates whether the console argument is required.
    /// </summary>
    public readonly bool Required;

    /// <summary>
    ///     This property contains the short name of the console argument, if applicable.
    /// </summary>
    public readonly char? ShortName;

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NamedConsoleArgumentAttribute" />
    ///     class with the specified <paramref name="name" />, <paramref name="description" />, optional
    ///     <paramref name="required" /> flag and optional <paramref name="defaultValue" />.
    /// </summary>
    /// <param name="name">The name of the console argument.</param>
    /// <param name="description">The description of the console argument to be displayed in the help text.</param>
    /// <param name="required">
    ///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
    /// </param>
    /// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
    /// <param name="group">Optional group name for the console argument.</param>
    /// <exception cref="ArgumentNullException">When the <paramref name="name" /> is null or empty.</exception>
    public NamedConsoleArgumentAttribute(
        string name,
        string description = null,
        bool required = true,
        object defaultValue = null,
        string group = null
    )
    {
        // Set the default value of the console argument into the instance.
        Default = defaultValue;

        // Set the description of the console argument into the instance.
        Description = description?.Trim();

        // Set the group name of the console argument into the instance.
        Group = group?.Trim();

        // Set the name of the console argument into the instance.
        Name = SanitizeArgumentName(name);

        // Set the required flag of the console argument into the instance.
        Required = required;
    }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NamedConsoleArgumentAttribute" />
    ///     class with the specified <paramref name="shortName" />, <paramref name="name" />,
    ///     <paramref name="description" />, optional <paramref name="required" /> flag
    ///     and optional <paramref name="defaultValue" />.
    /// </summary>
    /// <param name="shortName">The short name of the console argument.</param>
    /// <param name="name">The name of the console argument.</param>
    /// <param name="description">The description of the console argument to be displayed in the help text.</param>
    /// <param name="required">
    ///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
    /// </param>
    /// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
    /// <param name="group">Optional group name for the console argument.</param>
    /// <exception cref="ArgumentNullException">When the <paramref name="name" /> is null or empty.</exception>
    public NamedConsoleArgumentAttribute(
        char shortName,
        string name,
        string description = null,
        bool required = true,
        object defaultValue = null,
        string group = null
    )
        : this(name, description, required, defaultValue, group)
    {
        // Set the short name of the console argument into the instance.
        ShortName = shortName;
    }

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
            Name = Name,
            Required = Required,
            ShortName = ShortName,
        };
}

/// <summary>
///     This attribute is used to define console arguments for a command-line application.
/// </summary>
/// <typeparam name="TValue">The type of the value for the console argument.</typeparam>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NamedConsoleArgumentAttribute<TValue> : NamedConsoleArgumentAttribute
{
    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NamedConsoleArgumentAttribute" />
    ///     class with the specified <paramref name="name" />, <paramref name="description" />, optional
    ///     <paramref name="required" /> flag and optional <paramref name="defaultValue" />.
    /// </summary>
    /// <param name="name">The name of the console argument.</param>
    /// <param name="description">The description of the console argument to be displayed in the help text.</param>
    /// <param name="required">
    ///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
    /// </param>
    /// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
    /// <param name="group">Optional group name for the console argument.</param>
    /// <exception cref="ArgumentNullException">When the <paramref name="name" /> is null or empty.</exception>
    public NamedConsoleArgumentAttribute(
        string name,
        string description = null,
        bool required = true,
        TValue defaultValue = default,
        string group = null
    )
        : base(name, description, required, defaultValue, group) { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="NamedConsoleArgumentAttribute" />
    ///     class with the specified <paramref name="shortName" />, <paramref name="name" />,
    ///     <paramref name="description" />, optional <paramref name="required" /> flag
    ///     and optional <paramref name="defaultValue" />.
    /// </summary>
    /// <param name="shortName">The short name of the console argument.</param>
    /// <param name="name">The name of the console argument.</param>
    /// <param name="description">The description of the console argument to be displayed in the help text.</param>
    /// <param name="required">
    ///     Optional <see cref="bool" /> indicating whether the console argument is required or not.
    /// </param>
    /// <param name="defaultValue">Optional value to set as the default for the console argument.</param>
    /// <param name="group">Optional group name for the console argument.</param>
    /// <exception cref="ArgumentNullException">When the <paramref name="name" /> is null or empty.</exception>
    public NamedConsoleArgumentAttribute(
        char shortName,
        string name,
        string description = null,
        bool required = true,
        TValue defaultValue = default,
        string group = null
    )
        : base(shortName, name, description, required, defaultValue, group) { }
}
