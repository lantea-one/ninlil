using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Ninlil.Common.Serializer.Converter;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer;

/// <summary>
///     This class maintains our JSON serialization capabilities.
/// </summary>
public static class JsonSerializer
{
    /// <summary>
    ///     This property contains our default JSON converters.
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
    ///     This property contains our default JSON serializer options.
    /// </summary>
    private static JsonSerializerOptions _options = GetOptions();

    /// <summary>
    ///     This property contains our regular expression used to sanitize JSON strings.
    /// </summary>
    private static readonly Regex SanitizationRegularExpression = new(
        @"(\\u(?<Value>[a-zA-Z0-9]{4}))+",
        RegexOptions.Compiled
    );

    /// <summary>
    ///     This property contains our default serializer options.
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
    ///     This method generates new options for a de/serialization.
    /// </summary>
    /// <param name="pretty">Denotes whether to indent the JSON or not.</param>
    /// <param name="fromOptions">Optional default serializer options.</param>
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

        // A JsonSerializerOptions instance becomes permanently read-only the moment it's used
        // for a (de)serialization operation. A custom JsonConverter that calls back into this
        // wrapper (e.g. ListMapJsonConverter) is handed that very same, already-in-use instance
        // by the framework -- it's already carrying every converter it needs from whichever
        // top-level call locked it, so any further mutation attempt would throw. Return it as-is.
        if (options.IsReadOnly)
            return options;

        // Reconcile the pretty option.
        options.WriteIndented = pretty ?? options.WriteIndented;

        // Add our converters to the options, skipping any converter type that's already present
        // so repeated calls against the same still-mutable options instance don't pile up
        // duplicate converter instances.
        Converters
            .Where(c => options.Converters.All(existing => existing.GetType() != c.GetType()))
            .ToList()
            .ForEach(options.Converters.Add);

        // We're done, return the options.
        return options;
    }

    /// <summary>
    ///     This method repairs a malformed <paramref name="json" /> string that contains a literal,
    ///     unescaped double-quote inside one of its own string values -- something no valid JSON
    ///     document can contain, but which real upstream data (a user-typed comment, an externally
    ///     sourced description field) occasionally does anyway, and which would otherwise make
    ///     deserialization fail outright rather than simply losing a bit of fidelity in that one field.
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
        // A null payload has no runtime type to reflect -- fall back to typeof(object) so the
        // 3-arg overload's own null check (which returns null) is reached instead of throwing a
        // NullReferenceException here on payload.GetType().
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
    ///     This method serialized <paramref name="payload" /> into an indented and pretty JSON string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, JSON Serializer Options override.</param>
    /// <returns>Pretty JSON-serialized <paramref name="payload" />.</returns>
    public static string SerializePretty(object payload, JsonSerializerOptions options = null) =>
        // See the matching null-guard note on Serialize(object, ...) above.
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
