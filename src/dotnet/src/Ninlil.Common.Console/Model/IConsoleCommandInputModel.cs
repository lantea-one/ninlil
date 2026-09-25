using Ninlil.Common.Console.Attribute;
using Ninlil.Common.Serializer.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This interface is the contract for an input model: a class whose attributes declare the
///     verb and arguments a command line is parsed into. <see cref="ConsoleCommandInputModel" />
///     implements it and is the base class input models derive from.
/// </summary>
public interface IConsoleCommandInputModel
{
    /// <summary>
    ///     This property contains the format the output should be serialized into.
    /// </summary>
    public SerializerFormatEnum Format { get; set; }

    /// <summary>
    ///     This property indicates whether output should be suppressed or not.
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    ///     This method makes sure the output uses a serialized format (JSON, XML or YAML).
    /// </summary>
    /// <param name="format">The requested serialized format.</param>
    public void ForceSerializedOutputFormat(SerializerFormatEnum format);

    /// <summary>
    ///     This method retrieves the argument name or position for the specified property name.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the argument name or position for.</param>
    /// <returns>The argument name or position for the specified property name in a printable format.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    ///
    ///     When the <paramref name="propertyName" /> is not a named argument,
    ///     positional argument, or nested verb on the current type.
    /// </exception>
    public string GetArgumentNameOrPosition(string propertyName);

    /// <summary>
    ///     This method retrieves the <see cref="NamedConsoleArgumentAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a named argument.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the named argument attribute for.</param>
    /// <returns>
    ///     The <see cref="NamedConsoleArgumentAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    public NamedConsoleArgumentAttribute GetNamedArgument(string propertyName);

    /// <summary>
    ///     This method retrieves the <see cref="PositionalConsoleArgumentAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a positional argument.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the positional argument attribute for.</param>
    /// <returns>
    ///     The <see cref="PositionalConsoleArgumentAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public PositionalConsoleArgumentAttribute GetPositionalArgument(string propertyName);

    /// <summary>
    ///     This method retrieves the <see cref="NestedConsoleVerbAttribute" /> for the specified
    ///     <paramref name="propertyName" />, or <see langword="null" /> if the property is not a nested verb.
    /// </summary>
    /// <param name="propertyName">The property name to retrieve the nested verb attribute for.</param>
    /// <returns>
    ///     The <see cref="NestedConsoleVerbAttribute" /> for the specified
    ///     property name, or <see langword="null" /> if not found.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public NestedConsoleVerbAttribute GetNestedVerb(string propertyName);

    /// <summary>
    ///     This method determines if the current type has any named or positional arguments defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has named or positional arguments, otherwise <see langword="false" />.
    /// </returns>
    public bool HasArguments();

    /// <summary>
    ///     This method determines if the current type has any required named or positional arguments defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has required named
    ///     or positional arguments, otherwise <see langword="false" />.
    /// </returns>
    public bool HasRequiredArguments();

    /// <summary>
    ///     This method determines if the current type has a verb defined.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type has a verb defined, otherwise <see langword="false" />.
    /// </returns>
    public bool HasVerb();

    /// <summary>
    ///     This method determines if the current type is the default verb for the console application.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the current type is the default verb, otherwise <see langword="false" />.
    /// </returns>
    public bool IsDefault();

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a named argument or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a named argument or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsNamedArgument(string propertyName);

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a positional argument or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a positional argument or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsPositionalArgument(string propertyName);

    /// <summary>
    ///     This method determines if the specified <paramref name="propertyName" /> is a nested verb or not.
    /// </summary>
    /// <param name="propertyName">The property name to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the property is a nested verb or not.</returns>
    /// <exception cref="ArgumentException">
    ///     When the <paramref name="propertyName" /> does not exist in
    ///     the current type or isn't a public instance property.
    /// </exception>
    public bool IsNestedVerb(string propertyName);

    /// <summary>
    ///     This method sets the default values for all named and positional arguments along with the nested verbs in
    ///     the input model. If a property already has a value, it will not be overwritten by the default value.
    /// </summary>
    /// <returns>
    ///     The current instance of <see cref="IConsoleCommandInputModel" /> with the default
    ///     values set for all named and positional arguments along with the nested verbs.
    /// </returns>
    public IConsoleCommandInputModel SetDefaults();

    /// <summary>
    ///     This method converts the <paramref name="models" /> into an output string.
    /// </summary>
    /// <param name="models">The models to convert into an output string.</param>
    /// <returns>The output string representation of the models.</returns>
    public string ToOutput<TModel>(TModel models)
        where TModel : class;

    /// <summary>
    ///     This method converts the <paramref name="models" /> into an output string.
    /// </summary>
    /// <param name="models">The models to convert into an output string.</param>
    /// <returns>The output string representation of the models.</returns>
    public string ToOutput(dynamic models);

    /// <summary>
    ///     This method validates the parsed model.
    /// </summary>
    /// <param name="stoppingToken">
    ///     Optional <see cref="CancellationToken" /> used for tracking asynchronous operations.
    /// </param>
    /// <returns>An awaitable <see cref="Task" /> that resolves when the validation is complete.</returns>
    /// <exception cref="System.Exception">When the <see cref="ConsoleCommandInputModel" /> is invalid.</exception>
    public Task ValidateAsync(CancellationToken stoppingToken = default);
}
