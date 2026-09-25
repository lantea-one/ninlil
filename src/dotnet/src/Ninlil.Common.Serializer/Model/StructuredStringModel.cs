using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This abstract class is the base for "structured string" models: values that travel as a
///     single string in a fixed format but are held as typed properties, such as a data URI, a
///     network address or a time span string. A subclass supplies the regular expressions that
///     accept the format, <see cref="ParseMatch" /> to fill its properties from a match, and
///     <see cref="ToString(IDictionary{string, object})" /> to write the string back. The JSON and
///     YAML structured-string converters and the XML methods here make the model serialize as that
///     string. Equality compares the string forms.
/// </summary>
[XmlRoot("structuredString")]
public abstract class StructuredStringModel : IStructuredStringModel
{
    /// <summary>
    ///     This operator compares two models by their string forms. A null left operand is never
    ///     equal, so <c>null == null</c> is <see langword="false" />.
    /// </summary>
    /// <param name="left">The left instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <param name="right">The right instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <returns>
    ///     <see langword="true" /> if the two instances are considered equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator ==(StructuredStringModel left, StructuredStringModel right) =>
        left?.Equals(right) is true;

    /// <summary>
    ///     This operator negates <c>==</c>.
    /// </summary>
    /// <param name="left">The left instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <param name="right">The right instance of <see cref="StructuredStringModel" /> to be compared.</param>
    /// <returns>
    ///     <see langword="true" /> if the two instances are considered not equal; otherwise, <see langword="false" />.
    /// </returns>
    public static bool operator !=(StructuredStringModel left, StructuredStringModel right) =>
        !(left == right);

    /// <summary>
    ///     This field is a pattern that matches any single-line string, including an empty one.
    /// </summary>
    public static Regex Anything = new("^.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    ///     This method checks <paramref name="value" /> against a model type's patterns. It creates the
    ///     instance with <c>Activator.CreateInstance&lt;StructuredStringModel&gt;()</c>, which fails
    ///     for the abstract base, so it throws <see cref="MissingMethodException" /> for any
    ///     structured-string type.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be validated, which must implement <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be validated.</param>
    /// <returns>
    ///     <see langword="false" /> when <paramref name="stringModelType" /> is not a structured-string
    ///     type.
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
    ///     This method creates an instance of <paramref name="stringModelType" /> and parses
    ///     <paramref name="value" /> into it.
    /// </summary>
    /// <param name="stringModelType">
    ///     The type of the string model to be parsed, which must implement
    ///     <see cref="IStructuredStringModel" />.
    /// </param>
    /// <param name="value">The <see cref="string" /> value to be parsed.</param>
    /// <returns>
    ///     The parsed instance, or <see langword="null" /> for a blank value when the model allows
    ///     one.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When <paramref name="value" /> does not match the model's patterns, or when
    ///     <paramref name="stringModelType" /> is not a structured-string type.
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

            // A null result (an allowed blank value) counts as a failure.
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
    ///     This field indicates whether a blank value is accepted (parsing it gives
    ///     <see langword="null" />) instead of throwing.
    /// </summary>
    public readonly bool AllowNullOrEmpty;

    /// <summary>
    ///     This property contains the patterns a value may match; the first match is used.
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
    ///     This method fills the model's properties from a successful <paramref name="match" /> and
    ///     returns the model.
    /// </summary>
    /// <param name="original">The input string that matched.</param>
    /// <param name="match">The successful match, with the captured groups.</param>
    /// <returns>The populated model.</returns>
    protected abstract StructuredStringModel ParseMatch(string original, Match match);

    /// <summary>
    ///     This method writes the model back to its string form.
    /// </summary>
    /// <param name="options">Optional, subclass-specific formatting options.</param>
    /// <returns>The model as a string.</returns>
    protected abstract string ToString(IDictionary<string, object> options);

    /// <summary>
    ///     This method compares the model with another model (see
    ///     <see cref="Equals(IStructuredStringModel)" />), with a string ignoring case, or with any
    ///     other object by its <see cref="object.ToString" />.
    /// </summary>
    /// <param name="other">The object to compare against.</param>
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
    ///     This method compares two models. Two null (invalid) models are equal; otherwise the string
    ///     forms must match exactly.
    /// </summary>
    /// <param name="other">The model to compare against.</param>
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
    ///     This method hashes the trimmed string form, or returns 0 when it is blank.
    /// </summary>
    /// <returns>A hash code for the current instance.</returns>
    public override int GetHashCode() =>
        ToString()?.Trim() is null or "" ? 0 : ToString().Trim().GetHashCode();

    /// <summary>
    ///     This method returns <see langword="null" />, as <see cref="IXmlSerializable" /> recommends.
    /// </summary>
    /// <returns>Always <see langword="null" />.</returns>
    public XmlSchema GetSchema() => null;

    /// <summary>
    ///     This method is the same as <see cref="IsNull" />.
    /// </summary>
    /// <returns><see langword="true" /> when the model is not valid.</returns>
    public bool IsEmpty() => IsNull();

    /// <summary>
    ///     This method treats an invalid model as null.
    /// </summary>
    /// <returns><see langword="true" /> when <see cref="IsValid" /> is <see langword="false" />.</returns>
    public bool IsNull() => !IsValid();

    /// <summary>
    ///     This method checks if the current instance is valid or not.
    /// </summary>
    /// <returns><see langword="true" /> if the instance is valid; otherwise, <see langword="false" />.</returns>
    public abstract bool IsValid();

    /// <summary>
    ///     This method parses <paramref name="value" /> into this instance through the first matching
    ///     pattern and <see cref="ParseMatch" />.
    /// </summary>
    /// <param name="value">The input string value to be parsed into this model.</param>
    /// <returns>
    ///     The parsed model, or <see langword="null" /> for a blank value when
    ///     <see cref="AllowNullOrEmpty" /> is set.
    /// </returns>
    /// <exception cref="FormatException">
    ///     When the provided <paramref name="value" /> matches none of the <see cref="Patterns" />.
    /// </exception>
    public IStructuredStringModel Parse(string value)
    {
        // Check for a null or empty value and return null if it's allowed.
        if (value?.Trim() is null or { Length: 0 } && AllowNullOrEmpty)
            return null;

        // Check for any pattern matches with the provided value.
        Match match = Test(value);

        // Without a successful match, the value is not in this model's format.
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
    ///     This method reads the model from an XML element's text and copies the parsed properties
    ///     onto this instance. It parses through <c>Activator.CreateInstance&lt;StructuredStringModel&gt;()</c>,
    ///     which fails for the abstract base class, so it currently throws for every model.
    /// </summary>
    /// <param name="reader">The XML reader to read the structured string value from.</param>
    public void ReadXml(XmlReader reader)
    {
        // Move the reader to the content.
        reader.MoveToContent();

        // Parse the element's text into a new model instance.
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

        // Copy the parsed property values onto this instance.
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
    ///     This method writes the model's trimmed string form as the element's text.
    /// </summary>
    /// <param name="writer">The XML writer to write the structured string value to.</param>
    public void WriteXml(XmlWriter writer) => writer.WriteString(ToString()?.Trim());
}
