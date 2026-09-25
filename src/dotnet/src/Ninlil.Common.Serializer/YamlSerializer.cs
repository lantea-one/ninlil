using Ninlil.Common.Serializer.Converter;
using Ninlil.Common.Serializer.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// Define our namespace.
namespace Ninlil.Common.Serializer;

/// <summary>
///     This static class wraps YamlDotNet with Ninlil's defaults: camelCase names and enums,
///     double-quoted scalars, null values omitted and indented sequences when writing;
///     case-insensitive, duplicate-checked matching that ignores unknown properties when reading.
///     The shared <see cref="Converters" /> (structured strings, IP and MAC addresses, colors, URIs
///     and versions) are added to every serializer and deserializer it builds. The MVC YAML
///     formatters and <see cref="Service.SerializerService" /> use it.
/// </summary>
public static class YamlSerializer
{
    /// <summary>
    ///     This constant defines the MIME type for YAML content.
    /// </summary>
    public const string MimeType = "application/x-yaml";

    /// <summary>
    ///     This constant defines the MIME type for YAML content that represents
    ///     a problem details object, following the RFC 7807 standard.
    /// </summary>
    public const string ProblemMimeType = "application/problem+yaml";

    /// <summary>
    ///     This list maintains our custom YAML type converters.
    /// </summary>
    private static readonly List<IYamlTypeConverter> Converters =
    [
        // Add our custom Data URI YAML converter.
        new StructuredStringYamlConverter<DataUriModel>(),
        // Add our custom TimeSpan string YAML converter.
        new StructuredStringYamlConverter<TimeStringModel>(),
        // Add our custom IP Address YAML converter.
        new IpAddressYamlConverter(),
        // Add our Physical Address (MAC Address) converter.
        new PhysicalAddressYamlConverter(),
        // Add our RGB Color converter.
        new RgbColorYamlConverter(),
        // Add our URI YAML converter.
        new UriYamlConverter(),
        // Add our Version YAML converter.
        new VersionYamlConverter(),
    ];

    /// <summary>
    ///     This method adds a <paramref name="converter" /> to the list
    ///     of default YAML converters if it doesn't already exist.
    /// </summary>
    /// <param name="converter">The YAML converter to add.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the provided <paramref name="converter" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(IYamlTypeConverter converter)
    {
        // Ensure we have a converter to add.
        ArgumentNullException.ThrowIfNull(converter);

        // Ensure the converter isn't already registered.
        if (Converters.All(c => c.GetType() != converter.GetType()))
            Converters.Add(converter);
    }

    /// <summary>
    ///     This method adds a <paramref name="converterType" /> to the list
    ///     of default YAML converters if it doesn't already exist.
    /// </summary>
    /// <param name="converterType">The type of YAML converter to add.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="converterType" /> is not a YAML converter.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     When the provided <paramref name="converterType" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(Type converterType)
    {
        // Ensure we have a converter type to add.
        ArgumentNullException.ThrowIfNull(converterType);

        // Ensure the converter type is a YAML converter.
        if (!typeof(IYamlTypeConverter).IsAssignableFrom(converterType))
            throw new ArgumentException(
                $"The provided converter type must be a YAML converter.",
                nameof(converterType)
            );

        // We're done, instantiate and add the converter.
        AddConverter((IYamlTypeConverter)Activator.CreateInstance(converterType));
    }

    /// <summary>
    ///     This method adds a <typeparamref name="TYamlConverter" /> to the list
    ///     of default YAML converters if it doesn't already exist.
    /// </summary>
    /// <typeparam name="TYamlConverter">The type of YAML converter to add.</typeparam>
    public static void AddConverter<TYamlConverter>()
        where TYamlConverter : class, IYamlTypeConverter, new() =>
        AddConverter(new TYamlConverter());

    /// <summary>
    ///     This method deserializes a <paramref name="yaml"/> string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="yaml">The YAML to deserialize.</param>
    /// <param name="deserializer">Optional, YAML Deserializer override.</param>
    /// <returns><paramref name="yaml" /> deserialized into a <paramref name="type" /> object.</returns>
    public static object Deserialize(Type type, string yaml, IDeserializer deserializer = null) =>
        yaml?.Trim() is null or ""
            ? null
            : (deserializer ?? GetDeserializer()).Deserialize(yaml, type);

    /// <summary>
    ///     This method deserializes a <paramref name="yaml" /> string into a <typeparamref name="TResult"/> object.
    /// </summary>
    /// <param name="yaml">The YAML to deserialize.</param>
    /// <param name="deserializer">Optional, YAML Deserializer override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns><paramref name="yaml" /> deserialized into a <typeparamref name="TResult" /> object.</returns>
    public static TResult Deserialize<TResult>(string yaml, IDeserializer deserializer = null) =>
        yaml?.Trim() is null or ""
            ? default
            : (deserializer ?? GetDeserializer()).Deserialize<TResult>(yaml);

    /// <summary>
    ///     This method builds a deserializer with the reading defaults and shared converters.
    /// </summary>
    /// <returns>Our YAML deserializer.</returns>
    public static IDeserializer GetDeserializer()
    {
        // Define our new deserializer builder.
        DeserializerBuilder builder = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithCaseInsensitivePropertyMatching()
            .WithDuplicateKeyChecking()
            .WithEnforceRequiredMembers();

        // Iterate over our custom YAML type converters and add them to the deserializer builder.
        Converters.ForEach(c => builder.WithTypeConverter(c));

        // We're done, build and return the deserializer.
        return builder.Build();
    }

    /// <summary>
    ///     This method builds a serializer with the writing defaults and shared converters.
    /// </summary>
    /// <returns>Our YAML serializer.</returns>
    public static ISerializer GetSerializer()
    {
        // Define our new serializer builder.
        SerializerBuilder builder = new SerializerBuilder()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .WithDefaultScalarStyle(ScalarStyle.DoubleQuoted)
            .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
            .WithIndentedSequences()
            .WithNamingConvention(CamelCaseNamingConvention.Instance);

        // Iterate over our custom YAML type converters and add them to the serializer builder.
        Converters.ForEach(c => builder.WithTypeConverter(c));

        // We're done, build and return the serializer.
        return builder.Build();
    }

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" /> to be serialized.</param>
    /// <param name="payload">The value to be serialized.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <returns>YAML serialized <paramref name="payload" /> <paramref name="type" /> object instance.</returns>
    public static string Serialize(Type type, object payload, ISerializer serializer = null) =>
        payload is null ? null : (serializer ?? GetSerializer()).Serialize(payload, type);

    /// <summary>
    ///     This method serializes <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <returns>YAML serialized <paramref name="payload" /> object instance.</returns>
    public static string Serialize(object payload, ISerializer serializer = null) =>
        // A null payload has no runtime type; use typeof(object) so the three-argument
        // overload's null check returns null instead of throwing here.
        Serialize(payload?.GetType() ?? typeof(object), payload, serializer);

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <typeparam name="TSource">The type of <paramref name="payload" />.</typeparam>
    /// <returns>YAML serialized <typeparamref name="TSource" /> <paramref name="payload" />.</returns>
    public static string Serialize<TSource>(TSource payload, ISerializer serializer = null) =>
        Serialize(typeof(TSource), payload, serializer);

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <returns>YAML serialized <paramref name="payload" />.</returns>
    public static string SerializePretty(object payload, ISerializer serializer = null) =>
        // See the null-payload note on Serialize(object, ...) above.
        SerializePretty(payload?.GetType() ?? typeof(object), payload, serializer);

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <returns>YAML serialized <paramref name="type" /> <paramref name="payload" />.</returns>
    public static string SerializePretty(
        Type type,
        object payload,
        ISerializer serializer = null
    ) => Serialize(type, payload, serializer);

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="serializer">Optional, YAML Serializer override.</param>
    /// <typeparam name="TSource">The type of <paramref name="payload" />.</typeparam>
    /// <returns>YAML serialized <typeparamref name="TSource" /> <paramref name="payload" />.</returns>
    public static string SerializePretty<TSource>(TSource payload, ISerializer serializer = null) =>
        SerializePretty(typeof(TSource), payload, serializer);

    /// <summary>
    ///     This method tries to deserialize a <paramref name="yaml" /> string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="yaml">The YAML to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="deserializer">Optional, YAML Deserializer override.</param>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize(
        Type type,
        string yaml,
        out object result,
        IDeserializer deserializer = null
    )
    {
        // Try to deserialize the YAML string.
        try
        {
            // Deserialize the YAML string.
            result = Deserialize(type, yaml, deserializer);

            // We're done, return whether the result is null or not.
            return result != null;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the result to null.
            result = null;

            // We're done, we couldn't deserialize the YAML string.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to deserialize a <paramref name="yaml" /> string into a <typeparamref name="TResult" /> object.
    /// </summary>
    /// <param name="yaml">The YAML to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="deserializer">Optional, YAML Deserializer override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize<TResult>(
        string yaml,
        out TResult result,
        IDeserializer deserializer = null
    )
    {
        // Try to deserialize the YAML string.
        if (TryDeserialize(typeof(TResult), yaml, out object resultObject, deserializer))
        {
            // Reset the result to the deserialized object.
            result = (TResult)resultObject;

            // We're done, return true.
            return true;
        }

        // Reset the result to default.
        result = default;

        // We're done, return false.
        return false;
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="yaml">The serialized YAML output string.</param>
    /// <param name="serializer">Optional, YAML serializer override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        object payload,
        out string yaml,
        ISerializer serializer = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            yaml = pretty ? SerializePretty(payload, serializer) : Serialize(payload, serializer);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            yaml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into a YAML string.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="yaml">The serialized YAML output string.</param>
    /// <param name="serializer">Optional, YAML serializer override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        Type type,
        object payload,
        out string yaml,
        ISerializer serializer = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            yaml = pretty
                ? SerializePretty(type, payload, serializer)
                : Serialize(type, payload, serializer);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            yaml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into a YAML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="yaml">The serialized YAML output string.</param>
    /// <param name="serializer">Optional, YAML serializer override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <typeparam name="TPayload">The type of <paramref name="payload" />.</typeparam>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize<TPayload>(
        TPayload payload,
        out string yaml,
        ISerializer serializer = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            yaml = pretty ? SerializePretty(payload, serializer) : Serialize(payload, serializer);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            yaml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }
}
