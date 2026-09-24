using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class provides a base construct for a structured string model, which is a string that follows a
///     specific format defined by a regular expression pattern. The <see cref="StructuredStringModel" />
///     class provides a common interface for validating and parsing structured strings, allowing derived classes
///     to implement specific patterns and parsing logic as needed.
/// </summary>
[XmlRoot("structuredString")]
public abstract class StructuredStringModel : IStructuredStringModel
{
    /// <summary>
    ///     This operator overload allows for the equality comparison of two instances of <see cref="StructuredStringModel" />
    ///     by comparing their string representations. The operator first checks for <see langword="null" /> values on
    ///     both sides and then compares the string representations of the two instances using the overridden
    ///     <see cref="ToString()" /> method. This provides a convenient way to compare two structured string models
    ///     for equality based on their content rather than their reference.
    /// </summary>
    /// <param name="left">The left instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <param name="right">The right instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <returns>
    ///     <see langword="true" /> if the two instances are considered equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator ==(StructuredStringModel left, StructuredStringModel right) =>
        left?.Equals(right) is true;

    /// <summary>
    ///     This operator overload allows for the inequality comparison of two instances of <see cref="StructuredStringModel" />
    ///     by negating the result of the equality operator. It provides a convenient way to compare two structured
    ///     string models for inequality based on their content rather than their reference.
    /// </summary>
    /// <param name="left">The left instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <param name="right">The right instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <returns>
    ///     <see langword="true" /> if the two instances are considered not equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator !=(StructuredStringModel left, StructuredStringModel right) =>
        !(left == right);

    /// <summary>
    ///     This static property defines a regular expression pattern that matches any string,
    ///     including <see langword="null" /> and <see cref="string.Empty" /> values.
    /// </summary>
    public static Regex Anything = new("^.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     This static method is responsible for validating the provided
    ///     <paramref name="value" /> string against the defined <see cref="Patterns" />
    ///     of the specified <paramref name="stringModelType" />.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be validated, which must implement <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be validated.</param>
    /// <returns>
    ///     <see langword="true" /> when the <paramref name="value" /> matches any of
    ///     the defined <see cref="Patterns" />; otherwise, <see langword="false" />.
    /// </returns>
    public static bool IsMatch(Type stringModelType, string value) =>
        stringModelType.IsAssignableTo(typeof(IStructuredStringModel))
        && Activator.CreateInstance<StructuredStringModel>()?.Patterns.Any(p => p.IsMatch(value))
            is true;

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
    public static bool IsMatch<TStringModel>(string value)
        where TStringModel : class, IStructuredStringModel => IsMatch(typeof(TStringModel), value);

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
    public static IStructuredStringModel Parse(Type stringModelType, string value) =>
        stringModelType.IsAssignableTo(typeof(IStructuredStringModel))
            ? (Activator.CreateInstance(stringModelType) as StructuredStringModel)?.Parse(value)
            : throw new FormatException(
                string.Format(
                    "The provided value '{0}' does not match any of the defined patterns for the specified string model type '{1}'.",
                    value,
                    stringModelType.Name
                )
            );

    /// <summary>
    ///     This static method is responsible for parsing the provided input
    ///     <paramref name="value" /> string into an instance of the implementing class.
    /// </summary>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <returns>
    ///     An instance of the implementing class if the input is valid; otherwise, <see langword="null" />.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the defined <see cref="Patterns" />.
    /// </exception>
    public static TStringModel Parse<TStringModel>(string value)
        where TStringModel : class, IStructuredStringModel, new() =>
        Parse(typeof(TStringModel), value) as TStringModel;

    /// <summary>
    ///     This static method is responsible for attempting to parse the provided input
    ///     <paramref name="value" /> string into an instance of <paramref name="stringModelType" />
    ///     returning a <see cref="bool" /> to indicate success or failure of the parsing operation.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be parsed, which must implement <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <param name="result">
    ///     The output instance of <paramref name="stringModelType" />
    ///     if parsing is successful; otherwise, <see langword="null" />.
    /// </param>
    /// <returns><see langword="true" /> if parsing is successful; otherwise, <see langword="false" />.</returns>
    public static bool TryParse(
        Type stringModelType,
        string value,
        out IStructuredStringModel result
    )
    {
        // Try to parse the value into an instance.
        try
        {
            // Attempt to parse the value.
            result = Parse(stringModelType, value);

            // We're done, return the null state of the result.
            return result is not null;
        }
        // Catch any format exceptions that occur during parsing.
        catch (FormatException)
        {
            // Reset the output model.
            result = null;

            // We're done, we couldn't parse the value, return false.
            return false;
        }
    }

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
    ///     The type of the string model to be parsed, which must implement <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns><see langword="true" /> if parsing is successful; otherwise, <see langword="false" />.</returns>
    public static bool TryParse<TStringModel>(string value, out TStringModel result)
        where TStringModel : class, IStructuredStringModel, new()
    {
        // Try to parse the base value into a structure.
        if (TryParse(typeof(TStringModel), value, out IStructuredStringModel parsed))
        {
            // Reset the result to the parsed value.
            result = parsed as TStringModel;

            // We're done, return the null state of the result.
            return result is not null;
        }

        // Reset the output model.
        result = null;

        // We're done, we couldn't parse the value, return false.
        return false;
    }

    /// <summary>
    ///     This property indicates whether the structured string model allows
    ///     for <see langword="null" /> or <see cref="string.Empty" /> string
    ///     values to be considered valid or not.
    /// </summary>
    public readonly bool AllowNullOrEmpty;

    /// <summary>
    ///     This property defines the regular expression patterns responsible for validating inputs.
    /// </summary>
    public IEnumerable<Regex> Patterns { get; private set; }

    /// <summary>
    ///     This constructor is responsible for initializing a new instance of the
    ///     <see cref="StructuredStringModel" /> class with the
    ///     provided regular expression <paramref name="patterns" /> for validating inputs.
    /// </summary>
    /// <param name="allowNullOrEmpty">
    ///     Indicates whether <see langword="null" /> or <see cref="string.Empty" /> values should be considered valid for this model.
    /// </param>
    /// <param name="patterns">The regular expression patterns to be used for validating inputs.</param>
    protected StructuredStringModel(bool allowNullOrEmpty, IEnumerable<Regex> patterns) =>
        (AllowNullOrEmpty, Patterns) = (allowNullOrEmpty, patterns.Where(p => p is not null));

    /// <summary>
    ///     This constructor is responsible for initializing a new instance of the
    ///     <see cref="StructuredStringModel" /> class with the
    ///     provided regular expression <paramref name="patterns" /> for validating inputs.
    /// </summary>
    /// <param name="allowNullOrEmpty">
    ///     Indicates whether <see langword="null" /> or <see cref="string.Empty" /> values should be considered valid for this model.
    /// </param>
    /// <param name="patterns">The regular expression patterns to be used for validating inputs.</param>
    protected StructuredStringModel(bool allowNullOrEmpty, params Regex[] patterns)
        : this(allowNullOrEmpty, patterns.AsEnumerable()) { }

    /// <summary>
    ///     This constructor is responsible for initializing a new instance of the
    ///     <see cref="StructuredStringModel" /> class with the
    ///     provided input <paramref name="value" /> string and the regular
    ///     expression <paramref name="patterns" />
    /// </summary>
    /// <param name="allowNullOrEmpty">
    ///     Indicates whether <see langword="null" /> or <see cref="string.Empty" /> values should be considered valid for this model.
    /// </param>
    /// <param name="value">The input string value to be parsed into this model.</param>
    /// <param name="patterns">The regular expression patterns to be used for validating the input value.</param>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the defined <paramref name="patterns" />.
    /// </exception>
    protected StructuredStringModel(
        bool allowNullOrEmpty,
        string value,
        IEnumerable<Regex> patterns
    )
        : this(allowNullOrEmpty, patterns) => Parse(value);

    /// <summary>
    ///     This constructor is responsible for initializing a new instance of the
    ///     <see cref="StructuredStringModel" /> class with the
    ///     provided input <paramref name="value" /> string and the regular
    ///     expression <paramref name="patterns" />
    /// </summary>
    /// <param name="allowNullOrEmpty">
    ///     Indicates whether <see langword="null" /> or <see cref="string.Empty" /> values should be considered valid for this model.
    /// </param>
    /// <param name="value">The input string value to be parsed into this model.</param>
    /// <param name="patterns">The regular expression patterns to be used for validating the input value.</param>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> does not match any of the defined <paramref name="patterns" />.
    /// </exception>
    protected StructuredStringModel(bool allowNullOrEmpty, string value, params Regex[] patterns)
        : this(allowNullOrEmpty, value, patterns.AsEnumerable()) { }

    /// <summary>
    ///     This abstract method is responsible for parsing the provided <paramref name="match" />
    ///     Regular Expression <see cref="Match" /> object into the properties of this model.
    ///     The <paramref name="match" /> object is expected to contain the captured groups that correspond
    ///     to the properties of this model, and the implementation of this method should extract those
    ///     values and assign them to the appropriate properties.
    /// </summary>
    /// <param name="original">
    ///     The original input string that was matched against the defined <see cref="Patterns" />.
    ///     This can be useful for reference or for cases where the original string is needed for
    ///     parsing logic that may not be fully captured by the regular expression groups.
    /// </param>
    /// <param name="match">
    ///     The Regular Expression <see cref="Match" /> object that contains
    ///     the captured groups to be parsed into the properties of this model.
    /// </param>
    protected abstract StructuredStringModel ParseMatch(string original, Match match);

    /// <summary>
    ///     This abstract method is responsible for generating a <see cref="string" />
    ///     representation of the document string model based on the properties of this model,
    ///     potentially using the provided <paramref name="options" /> to customize the output format.
    /// </summary>
    /// <param name="options">
    ///     An optional dictionary of options that can be used to customize the string representation of the model.
    ///     The specific options and their effects are determined by the implementation of this method in derived classes.
    /// </param>
    /// <returns>
    ///     A <see cref="string" /> representation of the document string model based on the properties of this model.
    /// </returns>
    protected abstract string ToString(IDictionary<string, object> options);

    /// <summary>
    ///     This method determines the current instance equality to the
    ///     <paramref name="other" /> object representation of a document string model.
    /// </summary>
    /// <param name="other">The other object representation of a document string model to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if <see langword="this"/> instance is equal to the
    ///     <paramref name="other" /> object; otherwise, <see langword="false" />.
    /// </returns>
    public override bool Equals(object other) =>
        other switch
        {
            // Check for equality against another instance of the same model.
            IStructuredStringModel o => Equals(o),

            // Check for equality against a string representation of a document string model.
            string o => Equals(o, StringComparison.OrdinalIgnoreCase),

            // We're not sure what this is, try to compare their string representations.
            _ => other?.ToString()?.Equals(ToString()) is true,
        };

    /// <summary>
    ///     This method determines the current instance equality to the <paramref name="other" /> instance.
    /// </summary>
    /// <param name="other">The other object representation of a document string model to compare against.</param>
    /// <returns>
    ///     <see langword="true" /> if the current instance is equal to the
    ///     <paramref name="other" /> object; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals(IStructuredStringModel other) =>
        IsNull() ? other?.IsNull() is not false
        : other?.IsNull() is not false ? IsNull()
        : ToString()?.Equals(other.ToString()) is true;

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
    public bool Equals(string other, StringComparison comparison) =>
        ToString()?.Equals(other, comparison) is true;

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
    ///     The type of the string model to be compared, which must implement <see cref="IStructuredStringModel" />.
    /// </typeparam>
    /// <returns>
    ///     <see langword="true" /> if the current instance is equal to the
    ///     <paramref name="other" /> string; otherwise, <see langword="false" />.
    /// </returns>
    public bool Equals<TStringModel>(TStringModel other, StringComparison? comparison = null)
        where TStringModel : class, IStructuredStringModel, new() =>
        comparison is null
            ? Equals(other as IStructuredStringModel)
            : Equals(other?.ToString(), comparison.Value);

    /// <summary>
    ///     This method is responsible for generating a hash code for the current instance
    ///     of the <see cref="StructuredStringModel" /> class.
    /// </summary>
    /// <returns>A hash code for the current instance.</returns>
    public override int GetHashCode() =>
        ToString()?.Trim() is null or "" ? 0 : ToString().Trim().GetHashCode();

    /// <summary>
    ///     This method retrieves the XML schema for the structured string model.
    /// </summary>
    /// <returns>
    ///     The XML schema for the structured string model, or <see langword="null" /> if no schema is available.
    /// </returns>
    public XmlSchema GetSchema() => null;

    /// <summary>
    ///     This method checks if the current instance is considered
    ///     <see see="string.Empty" /> based on the defined validation
    ///     rules of the model.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the instance is considered
    ///     <see see="string/Empty" />; otherwise, <see langword="false" />.
    /// </returns>
    public bool IsEmpty() => IsNull();

    /// <summary>
    ///     This method checks if the current instance is considered
    ///     <see langword="null" /> based on the defined validation rules of the model.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the instance is considered
    ///     <see langword="null" />; otherwise, <see langword="false" />.
    /// </returns>
    public bool IsNull() => !IsValid();

    /// <summary>
    ///     This method checks if the current instance is valid or not.
    /// </summary>
    /// <returns><see langword="true" /> if the instance is valid; otherwise, <see langword="false" />.</returns>
    public abstract bool IsValid();

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
    public IStructuredStringModel Parse(string value)
    {
        // Check for a null or empty value and return null if it's allowed.
        if (value?.Trim() is null or { Length: 0 } && AllowNullOrEmpty)
            return null;

        // Check for any pattern matches with the provided value.
        Match match = Test(value);

        // If we have a successful match, parse the value into the properties of this model.
        if (match?.Success is not true)
            throw new FormatException(
                string.Format(
                    "The provided value '{0}' does not match any of the defined patterns for {1}:{2}\t{3}",
                    GetType().Name,
                    Environment.NewLine,
                    value,
                    string.Join($"{Environment.NewLine}\t", Patterns.Select(p => p.ToString()))
                )
            );

        // We're done, parse the match into the properties of this model.
        return ParseMatch(value, match);
    }

    /// <summary>
    ///     This method parses an XML string value into the instance using the XML <paramref name="reader" />.
    /// </summary>
    /// <param name="reader">The XML reader to read the structured string value from.</param>
    public void ReadXml(XmlReader reader)
    {
        // Move the reader to the content.
        reader.MoveToContent();

        // Attempt to create an instance of the structured string model using the string value read from the XML.
        if (
            Activator.CreateInstance<StructuredStringModel>().Parse(reader.ReadString())
                is not StructuredStringModel model
            || model.IsNull()
        )
        {
            // Check for allowed null or empty values.
            if (AllowNullOrEmpty)
                return;

            // We're done, throw a format exception since we couldn't parse the value into a valid model instance.
            throw new FormatException(
                string.Format(
                    "The provided XML value '{0}' does not match any of the defined patterns for {1}:{2}\t{3}",
                    reader.ReadString(),
                    Environment.NewLine,
                    reader.ReadString(),
                    string.Join($"{Environment.NewLine}\t", Patterns.Select(p => p.ToString()))
                )
            );
        }

        // If the instance was successfully created, copy the property values from the new model to this instance.
        foreach (
            var property in GetType()
                .GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(p => p.CanRead && p.CanWrite)
        )
            property.SetValue(this, property.GetValue(model));
    }

    /// <summary>
    ///     This method tests the provided <paramref name="value" /> string against the defined <see cref="Patterns" />
    ///     of this model and returns the first successful <see cref="Match" /> if any; otherwise, <see langword="null" />.
    /// </summary>
    /// <param name="value">
    ///     The <see cref="string" /> value to be tested against the defined <see cref="Patterns" />.
    /// </param>
    /// <returns>The first successful <see cref="Match" /> if any; otherwise, <see langword="null" />.</returns>
    public Match Test(string value) =>
        Patterns.Select(p => p.Match(value)).FirstOrDefault(m => m.Success);

    /// <summary>
    ///     This method returns a <see cref="string" /> representation of
    ///     the document string model based on the properties of this model.
    /// </summary>
    /// <returns>A <see cref="string" /> representation of the document string model.</returns>
    public override string ToString() => ToString(new Dictionary<string, object>());

    /// <summary>
    ///     This method writes the XML representation of the structured
    ///     string model using the provided XML <paramref name="writer" />.
    /// </summary>
    /// <param name="writer">The XML writer to write the structured string value to.</param>
    public void WriteXml(XmlWriter writer) => writer.WriteString(ToString()?.Trim());
}
