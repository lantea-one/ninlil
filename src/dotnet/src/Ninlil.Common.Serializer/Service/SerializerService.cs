using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using Ninlil.Common.Serializer.Enum;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Service;

/// <summary>
///     This class dispatches serialization by <see cref="SerializerFormatEnum" /> to
///     <see cref="JsonSerializer" />, <see cref="XmlSerializer" /> or <see cref="YamlSerializer" />,
///     so a caller can pick the format at run time (for example from a user's output-format
///     choice). The <c>options</c> arguments are passed to the chosen serializer when they are of
///     its type and ignored otherwise. <see cref="SerializerFormatEnum.None" /> is not supported
///     and throws <see cref="NotSupportedException" />. All members are static.
/// </summary>
public class SerializerService
{
    /// <summary>
    ///     This method adds a <paramref name="converter" /> to the list
    ///     of default JSON converters if it doesn't already exist.
    /// </summary>
    /// <param name="converter">The JSON converter to add.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the provided <paramref name="converter" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(JsonConverter converter) =>
        JsonSerializer.AddConverter(converter);

    /// <summary>
    ///     This method adds a <paramref name="converter" /> to the list
    ///     of default YAML converters if it doesn't already exist.
    /// </summary>
    /// <param name="converter">The YAML converter to add.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the provided <paramref name="converter" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(IYamlTypeConverter converter) =>
        YamlSerializer.AddConverter(converter);

    /// <summary>
    ///     This method adds a <paramref name="converterType" /> to the list
    ///     of default JSON or YAML converters if it doesn't already exist.
    /// </summary>
    /// <param name="converterType">The type of JSON or YAML converter to add.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="converterType" /> is not a JSON or YAML converter.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     When the provided <paramref name="converterType" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(Type converterType)
    {
        // Ensure we have a converter type to add.
        ArgumentNullException.ThrowIfNull(converterType);

        // Ensure the converter type is a JSON converter.
        if (
            !typeof(JsonConverter).IsAssignableFrom(converterType)
            && !typeof(IYamlTypeConverter).IsAssignableFrom(converterType)
        )
            throw new ArgumentException(
                $"The provided converter type '{converterType.FullName}' is not a JSON or YAML converter.",
                nameof(converterType)
            );

        // Check if the converter type is a JSON converter and add it if so.
        if (typeof(JsonConverter).IsAssignableFrom(converterType))
            AddConverter(Activator.CreateInstance(converterType) as JsonConverter);

        // Check if the converter type is a YAML converter and add it if so.
        if (typeof(IYamlTypeConverter).IsAssignableFrom(converterType))
            AddConverter(Activator.CreateInstance(converterType) as IYamlTypeConverter);
    }

    /// <summary>
    ///     This method adds a <paramref name="converterType" /> to the list
    ///     of default JSON or YAML converters if it doesn't already exist.
    /// </summary>
    /// <param name="converterType">The type of JSON or YAML converter to add.</param>
    /// <param name="format">The serialization format the converter type supports.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="converterType" /> is not a JSON or YAML converter.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     When the provided <paramref name="converterType" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(Type converterType, SerializerFormatEnum format)
    {
        // Check if the converter type is a JSON converter and add it if so.
        if (format is SerializerFormatEnum.Json)
            JsonSerializer.AddConverter(converterType);
        // Check if the converter type is a YAML converter and add it if so.
        else if (format is SerializerFormatEnum.Yaml)
            YamlSerializer.AddConverter(converterType);
        // Otherwise, throw an exception.
        else
            throw new NotSupportedException(
                $"The serializer format '{format}' is not supported for this operation."
            );
    }

    /// <summary>
    ///     This method adds a <typeparamref name="TConverter" /> to the list
    ///     of default JSON or YAML converters if it doesn't already exist.
    /// </summary>
    /// <typeparam name="TConverter">The type of JSON or YAML converter to add.</typeparam>
    /// <exception cref="ArgumentException">
    ///     When the provided <typeparamref name="TConverter" /> is not a JSON or YAML converter.
    /// </exception>
    public static void AddConverter<TConverter>()
        where TConverter : class, new()
    {
        // Ensure TConverter derives from JsonConverter or implements IYamlTypeConverter.
        // base.IsAssignableFrom(derived) is the correct direction for that check.
        if (
            !typeof(JsonConverter).IsAssignableFrom(typeof(TConverter))
            && !typeof(IYamlTypeConverter).IsAssignableFrom(typeof(TConverter))
        )
            throw new ArgumentException(
                $"The provided converter type '{typeof(TConverter).FullName}' is not a JSON or YAML converter.",
                nameof(TConverter)
            );

        // Check if the converter type is a JSON converter and add it if so.
        if (typeof(JsonConverter).IsAssignableFrom(typeof(TConverter)))
            AddConverter(new TConverter() as JsonConverter);

        // Check if the converter type is a YAML converter and add it if so.
        if (typeof(IYamlTypeConverter).IsAssignableFrom(typeof(TConverter)))
            AddConverter(new TConverter() as IYamlTypeConverter);
    }

    /// <summary>
    ///     This method deserializes an XML or JSON <paramref name="serialized" />
    ///     string into a <paramref name="type" /> object instance.
    /// </summary>
    /// <param name="type">The expected type of the resulting object instance.</param>
    /// <param name="serialized">The XML or JSON serialized type.</param>
    /// <param name="format">The expected serialization format.</param>
    /// <param name="options">Optional, serializer options.</param>
    /// <returns><paramref name="type" /> object instance from <paramref name="serialized" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static object Deserialize(
        Type type,
        string serialized,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.Deserialize(
                type,
                serialized,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.Deserialize(
                type,
                serialized,
                options as XmlReaderSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.Deserialize(
                type,
                serialized,
                options as IDeserializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method deserializes an XML or JSON <paramref name="serialized" />
    ///     string into a <typeparamref name="TResult" /> object instance.
    /// </summary>
    /// <param name="serialized">The XML or JSON serialized type.</param>
    /// <param name="format">The expected serialization format.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TResult">The expected type of the resulting object instance.</typeparam>
    /// <returns><typeparamref name="TResult" /> object instance from <paramref name="serialized" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static TResult Deserialize<TResult>(
        string serialized,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.Deserialize<TResult>(
                serialized,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.Deserialize<TResult>(
                serialized,
                options as XmlReaderSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.Deserialize<TResult>(
                serialized,
                options as IDeserializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serializes <paramref name="payload" /> into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns><paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string Serialize(
        object payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.Serialize(
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.Serialize(
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.Serialize(payload, options as ISerializer),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serialized <paramref name="payload" /> of <paramref name="type" />
    ///     into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="type">The expected type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns><paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string Serialize(
        Type type,
        object payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.Serialize(
                type,
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.Serialize(
                type,
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.Serialize(
                type,
                payload,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serialized <paramref name="payload" /> of type <typeparamref name="TSource"/>
    ///     into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TSource">The expected type of <paramref name="payload" />.</typeparam>
    /// <returns><paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string Serialize<TSource>(
        TSource payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.Serialize(
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.Serialize(
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.Serialize(payload, options as ISerializer),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serializes <paramref name="payload" /> into an indented <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>Indented <paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string SerializePretty(
        object payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.SerializePretty(
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.SerializePretty(
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.SerializePretty(
                payload,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serializes <paramref name="payload" /> of <paramref name="type" />
    ///     into an indented <paramref name="format" /> serialized string.
    /// </summary>
    /// <param name="type">The expected type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>Indented <paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string SerializePretty(
        Type type,
        object payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.SerializePretty(
                type,
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.SerializePretty(
                type,
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.SerializePretty(
                type,
                payload,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method serializes <paramref name="payload" /> of type
    ///     <typeparamref name="TSource" /> into an indented <paramref name="format" /> serialized string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TSource">The expected type of <paramref name="payload" />.</typeparam>
    /// <returns>Indented <paramref name="format" /> serialized <paramref name="payload" />.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static string SerializePretty<TSource>(
        TSource payload,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.SerializePretty(
                payload,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.SerializePretty(
                payload,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.SerializePretty(
                payload,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to deserialize a <paramref name="serialized" />
    ///     string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="serialized">The serialized string to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="format">The serialization format <paramref name="serialized" /> uses.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TryDeserialize(
        Type type,
        string serialized,
        out object result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TryDeserialize(
                type,
                serialized,
                out result,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TryDeserialize(
                type,
                serialized,
                out result,
                options as XmlReaderSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TryDeserialize(
                type,
                serialized,
                out result,
                options as IDeserializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to deserialize a <paramref name="serialized" />
    ///     string into a <typeparamref name="TResult" /> object.
    /// </summary>
    /// <param name="serialized">The serialized string to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="format">The serialization format <paramref name="serialized" /> uses.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TryDeserialize<TResult>(
        string serialized,
        out TResult result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TryDeserialize(
                serialized,
                out result,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TryDeserialize(
                serialized,
                out result,
                options as XmlReaderSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TryDeserialize(
                serialized,
                out result,
                options as IDeserializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" /> into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerialize(
        object payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                payload,
                out result,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                payload,
                out result,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                payload,
                out result,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" /> into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="type">The expected type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerialize(
        Type type,
        object payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" /> into a <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TPayload">The expected type of <paramref name="payload" />.</typeparam>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerialize<TPayload>(
        TPayload payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                payload,
                out result,
                options as JsonSerializerOptions
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                payload,
                out result,
                options as XmlWriterSettings
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                payload,
                out result,
                options as ISerializer
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" />
    ///     into an indented <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerializePretty(
        object payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                payload,
                out result,
                options as JsonSerializerOptions,
                true
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                payload,
                out result,
                options as XmlWriterSettings,
                true
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                payload,
                out result,
                options as ISerializer,
                true
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" />
    ///     into an indented <paramref name="format" /> string.
    /// </summary>
    /// <param name="type">The expected type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerializePretty(
        Type type,
        object payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as JsonSerializerOptions,
                true
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as XmlWriterSettings,
                true
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                type,
                payload,
                out result,
                options as ISerializer,
                true
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };

    /// <summary>
    ///     This method tries to serialize a <paramref name="payload" />
    ///     into an indented <paramref name="format" /> string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="result">The serialized string representation of the <paramref name="payload" />.</param>
    /// <param name="format">The serialization format to use.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <typeparam name="TPayload">The expected type of <paramref name="payload" />.</typeparam>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    /// <exception cref="NotSupportedException">Thrown if the <paramref name="format" /> is not supported.</exception>
    public static bool TrySerializePretty<TPayload>(
        TPayload payload,
        out string result,
        SerializerFormatEnum format,
        object options = null
    ) =>
        format switch
        {
            // Check for a JSON or JSONC format.
            SerializerFormatEnum.Json => JsonSerializer.TrySerialize(
                payload,
                out result,
                options as JsonSerializerOptions,
                true
            ),

            // Check for an XML format.
            SerializerFormatEnum.Xml => XmlSerializer.TrySerialize(
                payload,
                out result,
                options as XmlWriterSettings,
                true
            ),

            // Check for a YAML format.
            SerializerFormatEnum.Yaml => YamlSerializer.TrySerialize(
                payload,
                out result,
                options as ISerializer,
                true
            ),

            // Otherwise, throw an exception.
            _ => throw new NotSupportedException(
                $"The serializer format '{format}' is not supported."
            ),
        };
}
