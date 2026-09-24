using System.Text.RegularExpressions;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This interface represents a structured string model.
/// </summary>
public interface IStructuredStringModel : IXmlSerializable
{
    /// <summary>
    ///     This static method is responsible for validating the provided
    ///     <paramref name="value" /> string against the defined <see cref="Patterns" />
    ///     of the specified <paramref name="stringModelType" />.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be validated, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be validated.</param>
    /// <returns>
    ///     <see langword="true" /> when the <paramref name="value" /> matches any of
    ///     the defined <see cref="Patterns" />; otherwise, <see langword="false" />.
    /// </returns>
    public static abstract bool IsMatch(Type stringModelType, string value);

    /// <summary>
    ///     This static method is responsible for validating the provided
    ///     <paramref name="value" /> string against the defined <see cref="Patterns" />.
    /// </summary>
    /// <param name="value">The <see cref="string" /> value to be validated.</param>
    /// <typeparam name="TStringModel">
    ///     The type of the string model to be validated, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns>
    ///     <see langword="true" /> when the <paramref name="value" /> matches any of
    ///     the defined <see cref="Patterns" />; otherwise, <see langword="false" />.
    /// </returns>
    public static abstract bool IsMatch<TStringModel>(string value)
        where TStringModel : class, IStructuredStringModel;

    /// <summary>
    ///     This static method is responsible for parsing the provided input <paramref name="value" />
    ///      string into an instance of the <paramref name="stringModelType" />.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be parsed, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <returns>
    ///     An instance of the <paramref name="stringModelType" /> if
    ///     the input is valid; otherwise, <see langword="null" />.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the
    ///     defined <see cref="Patterns" /> in the <paramref name="stringModelType" />.
    /// </exception>
    public static abstract IStructuredStringModel Parse(Type stringModelType, string value);

    /// <summary>
    ///     This static method is responsible for parsing the provided input
    ///     <paramref name="value" /> string into an instance of <typeparamref name="TStringModel" />.
    /// </summary>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <typeparam name="TStringModel">
    ///     The type of the string model to be parsed, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns>
    ///     An instance of the implementing class if the input is valid; otherwise, <see langword="null" />.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the defined <see cref="Patterns" />.
    /// </exception>
    public static abstract TStringModel Parse<TStringModel>(string value)
        where TStringModel : class, IStructuredStringModel, new();

    /// <summary>
    ///     This static method is responsible for attempting to parse the provided input
    ///     <paramref name="value" /> string into an instance of <paramref name="stringModelType" />
    ///     returning a <see cref="bool" /> to indicate success or failure of the parsing operation.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be parsed, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <param name="result">
    ///     The output instance of <paramref name="stringModelType" />
    ///     if parsing is successful; otherwise, <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> if parsing is successful; otherwise, <see langword="false" />.</returns>
    public static abstract bool TryParse(
        Type stringModelType,
        string value,
        out IStructuredStringModel result
    );

    /// <summary>
    ///     This static method is responsible for attempting to parse the provided input
    ///     <paramref name="value" /> string into an instance of <typeparamref name="TStringModel" />
    ///     returning a <see cref="bool" /> to indicate success or failure of the parsing operation.
    /// </summary>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <param name="result">
    ///     The output instance of <typeparamref name="TStringModel" />
    ///     if parsing is successful; otherwise, <see langword="null" />.
    /// </param>
    /// <typeparam name="TStringModel">
    ///     The type of the string to be parsed, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns><see langword="true" /> if parsing is successful; otherwise, <see langword="false" />.</returns>
    public static abstract bool TryParse<TStringModel>(string value, out TStringModel result)
        where TStringModel : class, IStructuredStringModel, new();

    /// <summary>
    ///     This property defines the regular expression patterns responsible for validating inputs.
    /// </summary>
    public IEnumerable<Regex> Patterns { get; }

    /// <summary>
    ///     This method determines the current instance equality to the
    ///     <paramref name="other" /> object representation of a document string model.
    /// </summary>
    /// <param name="other">The other object representation of a document string model to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if <see langword="this"/> instance is equal to the
    ///     <paramref name="other" /> object; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals(object other);

    /// <summary>
    ///     This method determines the current instance equality to the <paramref name="other" /> instance.
    /// </summary>
    /// <param name="other">The other object representation of a document string model to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if the current instance is equal to the
    ///     <paramref name="other" /> object; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals(IStructuredStringModel other);

    /// <summary>
    ///     This method determines the current instance equality to the
    ///     <paramref name="other" /> string representation of a document string model.
    /// </summary>
    /// <param name="other">The other string representation of a document string model to compare against.</param>
    /// <param name="comparison">
    ///     The string comparison to use when comparing the string representations of the document string models.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the current instance is equal to the
    ///     <paramref name="other" /> string; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals(string other, StringComparison comparison);

    /// <summary>
    ///     This method determines the current instance equality to the
    ///     <paramref name="other" /> string representation of a document
    ///     string model using the specified <paramref name="comparison" />
    ///     for string comparison.
    /// </summary>
    /// <param name="other">The other string representation of a document string model to compare against.</param>
    /// <param name="comparison">
    ///     The optional string comparison to use when comparing the string representations of the document string models.
    /// </param>
    /// <typeparam name="TStringModel">
    ///     The type of the string model to be compared, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns>
    ///     <see langword="true" /> if the current instance is equal to the
    ///     <paramref name="other" /> string; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals<TStringModel>(TStringModel other, StringComparison? comparison = null)
        where TStringModel : class, IStructuredStringModel, new();

    /// <summary>
    ///     This method is responsible for generating a hash code for the current instance
    ///     of the <see cref="IStructuredStringModel" /> class.
    /// </summary>
    /// <returns>A hash code for the current instance.</returns>
    public int GetHashCode();

    /// <summary>
    ///     This method checks if the current instance is considered
    ///     <see see="string.Empty" /> based on the defined validation
    ///     rules of the model.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the instance is considered
    ///     <see see="string/Empty" />; otherwise, <see langword="false" />.
    /// </returns>
    public bool IsEmpty();

    /// <summary>
    ///     This method checks if the current instance is considered
    ///     <see langword="null" /> based on the defined validation rules of the model.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the instance is considered
    ///     <see langword="null" />; otherwise, <see langword="false" />.
    /// </returns>
    public bool IsNull();

    /// <summary>
    ///     This method checks if the current instance is valid or not.
    /// </summary>
    /// <returns><see langword="true" /> if the instance is valid; otherwise, <see langword="false" />.</returns>
    public bool IsValid();

    /// <summary>
    ///     This method is responsible for parsing the provided input <paramref name="value" />
    ///     string into the current instance of the model, updating its properties accordingly.
    /// </summary>
    /// <param name="value">The input string value to be parsed into this model.</param>
    /// <returns>
    ///     The updated instance that was parsed from the provided input <paramref name="value" />.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the defined <see name="Patterns" />.
    /// </exception>
    public IStructuredStringModel Parse(string value);

    /// <summary>
    ///     This method returns a <see cref="string" /> representation of
    ///     the document string model based on the properties of this model.
    /// </summary>
    /// <returns>A <see cref="string" /> representation of the document string model.</returns>
    public string ToString();

    /// <summary>
    ///     This method tests the provided <paramref name="value" /> string against the defined <see cref="Patterns" />
    ///     of this model and returns the first successful <see cref="Match" /> if any; otherwise, <see langword="null" />.
    /// </summary>
    /// <param name="value">
    ///     The <see cref="string" /> value to be tested against the defined <see cref="Patterns" />.
    /// </param>
    /// <returns>The first successful <see cref="Match" /> if any; otherwise, <see langword="null" />.</returns>
    public Match Test(string value);
}
