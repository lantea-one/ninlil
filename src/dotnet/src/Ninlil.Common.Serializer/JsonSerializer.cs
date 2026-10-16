using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Converter;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer;

/// <summary>
///     This static class is Ninlil's front door to System.Text.Json. Storage (record data, the zone
///     journal, backups), the event broker and the config files all serialize through it. Without
///     explicit options every call uses one cached set of default options, built once and reused so
///     System.Text.Json keeps its type metadata. The defaults allow trailing commas and comments,
///     ignore property-name case and include the shared <see cref="Converters" /> (structured strings,
///     IP and MAC addresses, colors, URIs and camelCase enum strings). They start from the library
///     settings, or from the options assigned to <see cref="SerializerOptions" />. Other assemblies
///     add their own converters through <see cref="AddConverter(JsonConverter)" />, for example from a
///     module initializer; a converter added after the first call is used from the next call on.
///     Explicit options are copied and completed with the shared converters for both serialize and
///     deserialize; the caller's instance is never changed. Nothing rewrites the JSON text. The
///     <c>Try*</c> methods return <see langword="false" /> instead of throwing.
/// </summary>
public static class JsonSerializer
{
    /// <summary>
    ///     This field lists the converters added to every set of options <see cref="GetOptions" />
    ///     builds. Add to it only through <see cref="AddConverter(JsonConverter)" />: that method
    ///     locks the list and drops the cached default options, which a direct change to the list
    ///     does neither.
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
    ///     This lock guards <see cref="Converters" />, the base options and the writes to the two
    ///     cached option instances. Reads of the cached instances take no lock (see
    ///     <see cref="GetDefaultOptions" />).
    /// </summary>
    private static readonly object _gate = new();

    /// <summary>
    ///     This field holds the private copy of the options assigned to <see cref="SerializerOptions" />,
    ///     or null for the library defaults. Guarded by <see cref="_gate" />.
    /// </summary>
    private static JsonSerializerOptions _baseOptions;

    /// <summary>
    ///     This field caches the completed compact default options, or null when they must be rebuilt.
    ///     Written under <see cref="_gate" /> and read with <see cref="Volatile.Read{T}(ref readonly T)" />.
    /// </summary>
    private static JsonSerializerOptions _compactOptions;

    /// <summary>
    ///     This field caches the completed indented default options, or null when they must be rebuilt.
    ///     Same access rules as <see cref="_compactOptions" />.
    /// </summary>
    private static JsonSerializerOptions _prettyOptions;

    /// <summary>
    ///     This property holds the process-wide default options used when a call passes none. The
    ///     getter returns the cached, completed instance (the same one on every read until a converter is
    ///     added or the property is set). Treat it as read-only: it is shared and becomes read-only on
    ///     first use. To change the defaults assign a new instance: the setter stores a copy (the
    ///     assigned object is never changed or kept), completes it with the shared converters and
    ///     drops the cached instances. Assigning <see langword="null" /> restores the library defaults.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions
    {
        get => GetDefaultOptions(false);
        set
        {
            lock (_gate)
            {
                // Copy, so later changes to the caller's instance (or its becoming read-only
                // through use elsewhere) cannot reach the shared defaults.
                _baseOptions = value is null ? null : new JsonSerializerOptions(value);
                _compactOptions = null;
                _prettyOptions = null;
            }
        }
    }

    /// <summary>
    ///     This method adds a <paramref name="converter" /> to the list
    ///     of default JSON converters if it doesn't already exist, and drops the cached default options
    ///     so the converter is used from the next call on. It is safe to call from several threads.
    /// </summary>
    /// <param name="converter">The JSON converter to add.</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when the provided <paramref name="converter" /> is <see langword="null" />.
    /// </exception>
    public static void AddConverter(JsonConverter converter)
    {
        // Ensure we have a converter to add.
        ArgumentNullException.ThrowIfNull(converter);

        // The list and the cache are changed together under the lock: module initializers in
        // several assemblies call this at start-up while other threads already serialize.
        lock (_gate)
        {
            // Ensure the converter isn't already registered.
            if (Converters.Any(c => c.GetType() == converter.GetType()))
                return;

            Converters.Add(converter);

            // Options become read-only once used, so the cached instances cannot take the new
            // converter. Drop them; the next call rebuilds them with it. A call that already
            // holds the old instance finishes with it.
            _compactOptions = null;
            _prettyOptions = null;
        }
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
    /// <param name="options">
    ///     Optional options. They are copied and completed with the shared converters; the instance is not changed.
    /// </param>
    /// <returns><paramref name="json" /> deserialized into a <paramref name="type" /> object.</returns>
    public static object Deserialize(
        Type type,
        string json,
        JsonSerializerOptions options = null
    ) =>
        json?.Trim() is null or ""
            ? null
            : System.Text.Json.JsonSerializer.Deserialize(
                json,
                type,
                options is null ? GetDefaultOptions(false) : GetOptions(options)
            );

    /// <summary>
    ///     This method deserializes a <paramref name="json" /> string into a <typeparamref name="TResult"/> object.
    /// </summary>
    /// <param name="json">The JSON to deserialize.</param>
    /// <param name="options">
    ///     Optional options. They are copied and completed with the shared converters; the instance is not changed.
    /// </param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns><paramref name="json" /> deserialized into a <typeparamref name="TResult" /> object.</returns>
    public static TResult Deserialize<TResult>(string json, JsonSerializerOptions options = null) =>
        json?.Trim() is null or ""
            ? default
            : System.Text.Json.JsonSerializer.Deserialize<TResult>(
                json,
                options is null ? GetDefaultOptions(false) : GetOptions(options)
            );

    /// <summary>
    ///     This method returns options ready for use: a new mutable copy of <paramref name="fromOptions" />
    ///     (or of new library defaults: trailing commas, comments skipped, case-insensitive names) with the
    ///     indent setting applied and every converter in <see cref="Converters" /> added if its type is
    ///     missing. The given options are never changed. Read-only options (already used for
    ///     serialization) are returned unchanged. A caller that passes the same options repeatedly
    ///     should keep the returned instance and pass that on, because a fresh copy per call makes
    ///     System.Text.Json rebuild its type metadata.
    /// </summary>
    /// <param name="pretty">Whether to indent the JSON; null keeps the existing setting.</param>
    /// <param name="fromOptions">Optional options to complete instead of new defaults.</param>
    /// <returns>The JSON serializer options.</returns>
    public static JsonSerializerOptions GetOptions(
        JsonSerializerOptions fromOptions = null,
        bool? pretty = null
    )
    {
        // Options become read-only once used. A converter that calls back into this class (such
        // as ListMapJsonConverter) receives such in-use options, which already carry every
        // converter; changing them would throw, so return them as they are.
        if (fromOptions is { IsReadOnly: true })
            return fromOptions;

        // Copy the caller's options, so System.Text.Json never marks the caller's own instance
        // read-only and the shared converters never leak into it.
        return Complete(
            fromOptions is null ? CreateLibraryDefaults() : new JsonSerializerOptions(fromOptions),
            pretty
        );
    }

    /// <summary>
    ///     This method returns the cached default options, building them on first use or after a
    ///     converter was added or <see cref="SerializerOptions" /> was set.
    /// </summary>
    /// <param name="pretty">Whether to return the indented instance.</param>
    /// <returns>The shared, completed default options.</returns>
    private static JsonSerializerOptions GetDefaultOptions(bool pretty)
    {
        // The fast path takes no lock: a reference read is atomic and the instance was fully
        // built before it was published.
        JsonSerializerOptions cached = Volatile.Read(
            ref pretty ? ref _prettyOptions : ref _compactOptions
        );
        if (cached is not null)
            return cached;

        lock (_gate)
        {
            // Build both under one lock, from one snapshot of the base options and the converters,
            // so the compact and indented sets never differ.
            if (_compactOptions is null || _prettyOptions is null)
            {
                _compactOptions = Complete(
                    _baseOptions is null
                        ? CreateLibraryDefaults()
                        : new JsonSerializerOptions(_baseOptions),
                    false
                );
                _prettyOptions = Complete(
                    _baseOptions is null
                        ? CreateLibraryDefaults()
                        : new JsonSerializerOptions(_baseOptions),
                    true
                );
            }

            return pretty ? _prettyOptions : _compactOptions;
        }
    }

    /// <summary>
    ///     This method applies the indent setting to mutable <paramref name="options" /> and adds every
    ///     shared converter whose type is missing.
    /// </summary>
    /// <param name="options">Mutable options owned by the caller of this method.</param>
    /// <param name="pretty">Whether to indent the JSON; null keeps the existing setting.</param>
    /// <returns>The same <paramref name="options" />.</returns>
    private static JsonSerializerOptions Complete(JsonSerializerOptions options, bool? pretty)
    {
        // Reconcile the pretty option.
        options.WriteIndented = pretty ?? options.WriteIndented;

        // Take the snapshot under the lock: AddConverter changes the list on other threads, and
        // enumerating a list while it changes throws. The type check also keeps repeated calls
        // on the same options from adding duplicates.
        JsonConverter[] shared;
        lock (_gate)
            shared = [.. Converters];

        foreach (JsonConverter converter in shared)
            if (options.Converters.All(existing => existing.GetType() != converter.GetType()))
                options.Converters.Add(converter);

        return options;
    }

    /// <summary>
    ///     This method creates new options with the library defaults.
    /// </summary>
    /// <returns>New, mutable options without converters.</returns>
    private static JsonSerializerOptions CreateLibraryDefaults() =>
        new()
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

    /// <summary>
    ///     This method serializes a <paramref name="type" /> <paramref name="payload" />
    ///     into a JSON string with optional serializer <paramref name="options" />.
    /// </summary>
    /// <param name="type">The type of <paramref name="payload" /> to be serialized.</param>
    /// <param name="payload">The value to be serialized.</param>
    /// <param name="options">
    ///     Optional options. They are copied and completed with the shared converters; the instance is not changed.
    /// </param>
    /// <returns>JSON serialized <paramref name="payload" /> <paramref name="type" /> object instance.</returns>
    public static string Serialize(
        Type type,
        object payload,
        JsonSerializerOptions options = null
    ) =>
        payload is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(
                payload,
                type,
                options is null ? GetDefaultOptions(false) : GetOptions(options)
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
    ) =>
        payload is null
            ? null
            : System.Text.Json.JsonSerializer.Serialize(
                payload,
                type,
                options is null ? GetDefaultOptions(true) : GetOptions(options, true)
            );

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
