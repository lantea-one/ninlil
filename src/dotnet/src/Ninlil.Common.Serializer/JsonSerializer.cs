using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ninlil.Common.Serializer.Converter;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer;

/// <summary>
///     This static class is Ninlil's front door to System.Text.Json. Every call builds options that
///     allow trailing commas and comments, ignore property-name case, and include the shared
///     <see cref="Converters" /> (structured strings, IP and MAC addresses, colors, URIs and camelCase
///     enum strings). Other assemblies add their own converters through <see cref="AddConverter(JsonConverter)" />,
///     for example from a module initializer. Both serialized output and input to deserialize pass
///     through <see cref="SanitizeJsonString" />. Deserializing with explicit options uses them as
///     given, without adding the shared converters. The <c>Try*</c> methods return
///     <see langword="false" /> instead of throwing.
/// </summary>
public static class JsonSerializer
{
    /// <summary>
    ///     This field lists the converters added to every set of options <see cref="GetOptions" />
    ///     builds. Add to it through <see cref="AddConverter(JsonConverter)" />.
    /// </summary>
    public static readonly List<JsonConverter> Converters =
    [
        // Add our data URI converter.
        new StructuredStringJsonConverter<DataUriModel>(),
        // Add our TimeSpanString converter.
        new StructuredStringJsonConverter<TimeStringModel>(),
        // Add our IP Address converter.
        new IpAddressJsonConverter(),
        // Add our Nullable RGB Color converter.
        new NullableRgbColorJsonConverter(),
        // Add our Physical Address (MAC Address) converter.
        new PhysicalAddressJsonConverter(),
        // Add our RGB Color converter.
        new RgbColorJsonConverter(),
        // Add our URI converter.
        new UriJsonConverter(),
        // Add our enum converter to the options.
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        // Add our enum converter for serializer formats to the options.
        new JsonStringEnumConverter<SerializerFormatEnum>(),
    ];

    /// <summary>
    ///     This field holds the options exposed through <see cref="SerializerOptions" />.
    /// </summary>
    private static JsonSerializerOptions _options = GetOptions();

    /// <summary>
    ///     This field matches runs of <c>\uXXXX</c> escapes. Nothing in this class uses it.
    /// </summary>
    private static readonly Regex SanitizationRegularExpression = new(
        @"(\\u(?<Value>[a-zA-Z0-9]{4}))+",
        RegexOptions.Compiled
    );

    /// <summary>
    ///     This property holds process-wide default options, completed by <see cref="GetOptions" /> on
    ///     set. The serialize and deserialize methods do not read it: without explicit options they
    ///     build fresh defaults.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions
    {
        get => _options;
        set => _options = GetOptions(value);
    }

    /// <summary>
    ///     This method adds a <paramref name="converter" /> to the list
    ///     of default JSON converters if it doesn't already exist.
    /// </summary>
    /// <param name="converter">The JSON converter to add.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the provided <paramref name="converter" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(JsonConverter converter)
    {
        // Ensure we have a converter to add.
        ArgumentNullException.ThrowIfNull(converter);

        // Ensure the converter isn't already registered.
        if (Converters.All(c => c.GetType() != converter.GetType()))
            Converters.Add(converter);
    }

    /// <summary>
    ///     This method adds a <paramref name="converterType" /> to the list
    ///     of default JSON converters if it doesn't already exist.
    /// </summary>
    /// <param name="converterType">The type of JSON converter to add.</param>
    /// <exception cref="ArgumentException">
    ///     When the provided <paramref name="converterType" /> is not a JSON converter.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    ///     When the provided <paramref name="converterType" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(Type converterType)
    {
        // Ensure we have a converter type to add.
        ArgumentNullException.ThrowIfNull(converterType);

        // Ensure the converter type is a JSON converter.
        if (!typeof(JsonConverter).IsAssignableFrom(converterType))
            throw new ArgumentException(
                $"The provided converter type '{converterType.FullName}' is not a JSON converter.",
                nameof(converterType)
            );

        // We're done, instantiate and add the converter.
        AddConverter((JsonConverter)Activator.CreateInstance(converterType));
    }

    /// <summary>
    ///     This method adds a <typeparamref name="TJsonConverter" /> to the list
    ///     of default JSON converters if it doesn't already exist.
    /// </summary>
    /// <typeparam name="TJsonConverter">The type of JSON converter to add.</typeparam>
    public static void AddConverter<TJsonConverter>()
        where TJsonConverter : JsonConverter, new() => AddConverter(new TJsonConverter());

    /// <summary>
    ///     This method deserializes a <paramref name="json"/> string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="json">The JSON to deserialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns><paramref name="json" /> deserialized into a <paramref name="type" /> object.</returns>
    public static object Deserialize(
        Type type,
        string json,
        JsonSerializerOptions options = null
    ) =>
        json?.Trim() is null or ""
            ? null
            : System.Text.Json.JsonSerializer.Deserialize(
                SanitizeJsonString(json),
                type,
                options ?? GetOptions()
            );

    /// <summary>
    ///     This method deserializes a <paramref name="json" /> string into a <typeparamref name="TResult"/> object.
    /// </summary>
    /// <param name="json">The JSON to deserialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns><paramref name="json" /> deserialized into a <typeparamref name="TResult" /> object.</returns>
    public static TResult Deserialize<TResult>(string json, JsonSerializerOptions options = null) =>
        json?.Trim() is null or ""
            ? default
            : System.Text.Json.JsonSerializer.Deserialize<TResult>(
                SanitizeJsonString(json),
                options ?? GetOptions()
            );

    /// <summary>
    ///     This method returns options ready for use: <paramref name="fromOptions" />, or new defaults
    ///     (trailing commas, comments skipped, case-insensitive names), with the indent setting applied
    ///     and every converter in <see cref="Converters" /> added if its type is missing. The given
    ///     options are changed in place. Read-only options (already used for serialization) are
    ///     returned unchanged.
    /// </summary>
    /// <param name="pretty">Whether to indent the JSON; null keeps the existing setting.</param>
    /// <param name="fromOptions">Optional options to complete instead of new defaults.</param>
    /// <returns>The JSON serializer options.</returns>
    public static JsonSerializerOptions GetOptions(
        JsonSerializerOptions fromOptions = null,
        bool? pretty = null
    )
    {
        // Define our options response.
        JsonSerializerOptions options =
            fromOptions
            ?? new()
            {
                // We'll want to allow trailing commas.
                AllowTrailingCommas = true,

                // We'll want to disable property case sensitivity.
                PropertyNameCaseInsensitive = true,

                // We'll want to allow [and ignore] comments in the JSON.
                ReadCommentHandling = JsonCommentHandling.Skip,

                // Default the pretty option to false.
                WriteIndented = false,
            };

        // Options become read-only once used. A converter that calls back into this class (such
        // as ListMapJsonConverter) receives such in-use options, which already carry every
        // converter; changing them would throw, so return them as they are.
        if (options.IsReadOnly)
            return options;

        // Reconcile the pretty option.
        options.WriteIndented = pretty ?? options.WriteIndented;

        // Add each shared converter whose type is not already present, so repeated calls on the
        // same options do not add duplicates.
        Converters
            .Where(c => options.Converters.All(existing => existing.GetType() != c.GetType()))
            .ToList()
            .ForEach(options.Converters.Add);

        // We're done, return the options.
        return options;
    }

    /// <summary>
    ///     This method repairs JSON that contains an unescaped double quote inside a string value, which
    ///     makes the document invalid, by escaping quotes inside every <c>"key": "value"</c> pair.
    ///     It is a best-effort repair for hand-typed or external data.
    /// </summary>
    /// <param name="json">The JSON string to sanitize.</param>
    /// <returns>The sanitized <paramref name="json" /> string.</returns>
    public static string SanitizeJsonString(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        // Pattern to match all JSON string values: "key": "value"
        string pattern = "\"(.*?)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"";

        // Replace unescaped quotes inside the value part
        json = Regex.Replace(
            json,
            pattern,
            match =>
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                // Escape unescaped quotes inside value
                string fixedValue = Regex.Replace(value, "(?<!\\\\)\"", "\\\"");

                return $"\"{key}\":\"{fixedValue}\"";
            },
            RegexOptions.Singleline
        );
        return json;
    }

    /// <summary>
    ///     This method serializes a <paramref name="type" /> <paramref name="payload" />
    ///     into a JSON string with optional serializer <paramref name="options" />.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" /> to be serialized.</param>
    /// <param name="payload">The value to be serialized.</param>
    /// <param name="options">Optional, serializer options override.</param>
    /// <returns>JSON serialized <paramref name="payload" /> <paramref name="type" /> object instance.</returns>
    public static string Serialize(
        Type type,
        object payload,
        JsonSerializerOptions options = null
    ) =>
        payload is null
            ? null
            : SanitizeJsonString(
                System.Text.Json.JsonSerializer.Serialize(payload, type, GetOptions(options))
            );

    /// <summary>
    ///     This method serializes <paramref name="payload" /> into a JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns>JSON serialized <paramref name="payload" /> object instance.</returns>
    public static string Serialize(object payload, JsonSerializerOptions options = null) =>
        // A null payload has no runtime type; use typeof(object) so the three-argument
        // overload's null check returns null instead of throwing here.
        Serialize(payload?.GetType() ?? typeof(object), payload, options);

    /// <summary>
    ///     This method serializes <typeparamref name="TSource" /> <paramref name="payload" />
    ///     into a JSON string with optional serializer <paramref name="options" /> override.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <typeparam name="TSource">The type of <paramref name="payload" />.</typeparam>
    /// <returns>JSON serialized <typeparamref name="TSource" /> <paramref name="payload" />.</returns>
    public static string Serialize<TSource>(
        TSource payload,
        JsonSerializerOptions options = null
    ) => Serialize(typeof(TSource), payload, options);

    /// <summary>
    ///     This method serializes <paramref name="payload" /> into an indented JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns>Pretty JSON-serialized <paramref name="payload" />.</returns>
    public static string SerializePretty(object payload, JsonSerializerOptions options = null) =>
        // See the null-payload note on Serialize(object, ...) above.
        SerializePretty(payload?.GetType() ?? typeof(object), payload, options);

    /// <summary>
    ///     This method serializes <paramref name="type" />
    ///     <paramref name="payload"/> into an indented and pretty JSON string.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns>Pretty JSON-serialized <paramref name="type" /> <paramref name="payload" />.</returns>
    public static string SerializePretty(
        Type type,
        object payload,
        JsonSerializerOptions options = null
    ) => Serialize(type, payload, GetOptions(options, true));

    /// <summary>
    ///     This method serializes <typeparamref name="TSource" />
    ///     <paramref name="payload" /> into an indented and pretty JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <typeparam name="TSource">The type of <paramref name="payload" />.</typeparam>
    /// <returns>Pretty JSON-serialized <typeparamref name="TSource" /> <paramref name="payload" />.</returns>
    public static string SerializePretty<TSource>(
        TSource payload,
        JsonSerializerOptions options = null
    ) => SerializePretty(typeof(TSource), payload, options);

    /// <summary>
    ///     This method tries to deserialize a <paramref name="json" /> string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="json">The JSON to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize(
        Type type,
        string json,
        out object result,
        JsonSerializerOptions options = null
    )
    {
        // Try to deserialize the JSON string.
        try
        {
            // Deserialize the JSON string.
            result = Deserialize(type, json, options);

            // We're done, return whether the result is null or not.
            return result != null;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the result to null.
            result = null;

            // We're done, we couldn't deserialize the JSON string.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to deserialize a <paramref name="json" />
    ///     string into a <typeparamref name="TResult" /> object.
    /// </summary>
    /// <param name="json">The JSON to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize<TResult>(
        string json,
        out TResult result,
        JsonSerializerOptions options = null
    )
    {
        // Try to deserialize the JSON string.
        if (TryDeserialize(typeof(TResult), json, out object resultObject, options))
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
    ///     This method tries to serialize the <paramref name="payload" /> string into a JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="json">The serialized JSON output string.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        object payload,
        out string json,
        JsonSerializerOptions options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            json = pretty ? SerializePretty(payload, options) : Serialize(payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            json = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into a JSON string.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" />.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="json">The serialized JSON output string.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        Type type,
        object payload,
        out string json,
        JsonSerializerOptions options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            json = pretty
                ? SerializePretty(type, payload, options)
                : Serialize(type, payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            json = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into a JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="json">The serialized JSON output string.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <typeparam name="TPayload">The type of <paramref name="payload" />.</typeparam>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize<TPayload>(
        TPayload payload,
        out string json,
        JsonSerializerOptions options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            json = pretty ? SerializePretty(payload, options) : Serialize(payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            json = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }
}
