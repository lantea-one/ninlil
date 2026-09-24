using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Attribute;

// Define the namespace for the serializer converters.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This class provides a JSON converter for a "list map" -- a JSON array of objects where one property
///     of <typeparamref name="TItem" /> (identified via the <see cref="ListMapKeyAttribute" />) is treated
///     as that item's unique key. During deserialization, it reads a JSON array, deserializes each element
///     into a <typeparamref name="TItem" /> instance in the ordinary way, and additionally extracts and
///     re-assigns the key property by reflection -- which matters specifically when the key property is
///     excluded from ordinary serialization (for example via <see cref="JsonIgnoreAttribute" />) and would
///     otherwise never be populated. During serialization, the reverse happens: each item is serialized
///     normally and then has its key value explicitly (re-)injected under the key property's JSON name, so
///     a <see cref="JsonIgnoreAttribute" />-decorated key property still round-trips correctly. This
///     converter binds directly to a <see cref="List{TItem}" />-typed property or field.
/// </summary>
/// <typeparam name="TItem">
///     The type of items in the collection that will be serialized and deserialized.
///     This type must have a property decorated with the <see cref="ListMapKeyAttribute" />,
///     which will be used as the unique key for each item in the collection. The converter
///     relies on this attribute to identify the key property and extract its value during
///     the serialization and deserialization processes.
/// </typeparam>
public class ListMapJsonConverter<TItem> : JsonConverter<List<TItem>>
{
    /// <summary>
    ///     This method locates the property of <typeparamref name="TItem" /> decorated with the
    ///     <see cref="ListMapKeyAttribute" />, which both <see cref="Read" /> and <see cref="Write" />
    ///     rely on to identify the unique key for each item in the collection.
    /// </summary>
    /// <returns>The located key <see cref="PropertyInfo" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <typeparamref name="TItem" /> does not have a property decorated with the
    ///     <see cref="ListMapKeyAttribute" />, since the converter cannot determine which property
    ///     to use as the unique key for each item in the collection without it.
    /// </exception>
    private static PropertyInfo GetKeyProperty() =>
        typeof(TItem)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(p => p.GetCustomAttributes(typeof(ListMapKeyAttribute), true).Any())
        ?? throw new InvalidOperationException(
            string.Join(
                ' ',
                $"The type {typeof(TItem).Name} does not have a property decorated with the ListMapKeyAttribute.",
                $"Please ensure that one of the properties in {typeof(TItem).Name} is decorated with the",
                "ListMapKeyAttribute to specify the key for the list mapping."
            )
        );

    /// <summary>
    ///     This method resolves the JSON property name that <paramref name="keyProperty" /> will be
    ///     written as/read from, honoring a <see cref="JsonPropertyNameAttribute" /> or a Newtonsoft.Json
    ///     <c>JsonPropertyAttribute</c> if either is present, and otherwise falling back to the
    ///     property's own name.
    /// </summary>
    /// <param name="keyProperty">The key property to resolve the JSON name of.</param>
    /// <returns>The resolved JSON property name.</returns>
    private static string GetKeyName(PropertyInfo keyProperty) =>
        keyProperty.GetCustomAttribute<JsonPropertyNameAttribute>(true)?.Name
        ?? keyProperty.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true)?.PropertyName
        ?? keyProperty.Name;

    /// <summary>
    ///     This method reads and converts a JSON array into a <see cref="List{TItem}" />, extracting and
    ///     re-assigning each item's key property by reflection so it round-trips correctly even when the
    ///     key property is excluded from ordinary serialization (for example via
    ///     <see cref="JsonIgnoreAttribute" />).
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">
    ///     The <see cref="Type" /> of object to convert. This is expected to be a
    ///     <see cref="List{TItem}" />.
    /// </param>
    /// <param name="options">
    ///     The <see cref="JsonSerializerOptions" /> to use for deserializing each individual item and its
    ///     key value.
    /// </param>
    /// <returns>
    ///     The deserialized <see cref="List{TItem}" />. Any array element that is not a JSON object, or
    ///     whose key property's value is missing or null, is skipped.
    /// </returns>
    public override List<TItem> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Localize the key property and its JSON name; both are required to extract each
        // item's key from the raw JSON element below.
        PropertyInfo keyProperty = GetKeyProperty();
        string keyName = GetKeyName(keyProperty);

        // Parse the reading element into a JsonDocument so we can extract the key from it.
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        // If the JSON document is null or not an array, return an empty list of TItem.
        if (jsonDocument.RootElement.ValueKind is not JsonValueKind.Array)
            return [];

        // Define our list of TItem to return.
        var items = new List<TItem>();

        // Iterate through each element in the JSON array.
        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            // If the element is not an object, skip it.
            if (element.ValueKind is not JsonValueKind.Object)
                continue;

            // If the element does not contain the key, skip it.
            if (!element.TryGetProperty(keyName, out var keyPropertyValue))
                continue;

            // Localize the item key value from the JSON element as its TKey type. If the key is null or empty, skip it.
            var itemKey = JsonSerializer.Deserialize(
                keyProperty.PropertyType,
                keyPropertyValue.GetRawText(),
                options
            );

            // If the item key is null or empty, skip it.
            if (itemKey is null)
                continue;

            // Deserialize the raw JSON element into an instance of TItem.
            var item = JsonSerializer.Deserialize<TItem>(element.GetRawText(), options);

            // Set the key into the property of the item using reflection -- this is what makes
            // a JsonIgnore'd key property still populate correctly, and is a harmless no-op
            // re-assignment for an ordinary (non-ignored) key property.
            keyProperty.SetValue(item, itemKey);

            // Add the item to the list of TItem.
            items.Add(item);
        }

        // We're done, return our list of TItem.
        return items;
    }

    /// <summary>
    ///     This method writes a <see cref="List{TItem}" /> as a JSON array, serializing each item normally
    ///     and then explicitly (re-)injecting its key value under the key property's JSON name -- keeping
    ///     this the exact inverse of <see cref="Read" />, including when the key property is excluded from
    ///     ordinary serialization (for example via <see cref="JsonIgnoreAttribute" />).
    /// </summary>
    /// <param name="writer">
    ///     The <see cref="Utf8JsonWriter" /> to write to. This parameter represents the output stream
    ///     where the serialized JSON data will be written. The method uses this writer to output the
    ///     JSON representation of the collection of <typeparamref name="TItem" /> instances, ensuring
    ///     that the resulting JSON is properly formatted and adheres to the specified serialization options.
    /// </param>
    /// <param name="value">
    ///     The collection of <typeparamref name="TItem" /> instances to serialize.
    ///     This parameter represents the data that will be converted into JSON format.
    /// </param>
    /// <param name="options">
    ///     The <see cref="JsonSerializerOptions" /> to use for serialization.
    ///     This parameter allows customization of the JSON serialization process,
    ///     such as specifying naming policies, converters, and other options.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     The exception is thrown if the type <typeparamref name="TItem" /> does not have
    ///     a property decorated with the <see cref="ListMapKeyAttribute" />. This indicates
    ///     that the converter cannot determine which property to use as the unique key for
    ///     each item in the collection, and therefore cannot serialize the collection correctly.
    /// </exception>
    public override void Write(
        Utf8JsonWriter writer,
        List<TItem> value,
        JsonSerializerOptions options
    )
    {
        // Localize the key property and its JSON name; both are required to (re-)inject each
        // item's key value into its serialized JSON object below.
        PropertyInfo keyProperty = GetKeyProperty();
        string keyName = GetKeyName(keyProperty);

        // Define the list of per-item dictionaries that will become the output JSON array.
        var items = new List<Dictionary<string, object>>();

        // Iterate through each item in the collection of TItem.
        foreach (var item in value ?? [])
        {
            // Serialize the item normally (respecting its own JsonIgnore/naming attributes),
            // then decode it back into a string-keyed dictionary so the key property's value
            // can be explicitly (re-)injected below.
            string itemJson = JsonSerializer.Serialize(item, options);

            // Decode the item's own serialized JSON into a dictionary we can inject the key into.
            var itemDictionary =
                JsonSerializer.Deserialize<Dictionary<string, object>>(itemJson, options) ?? [];

            // Explicitly (re-)inject the key's value under its JSON name -- this is what makes
            // a JsonIgnore'd key property still round-trip, and is a harmless overwrite with
            // the same value for an ordinary (non-ignored) key property.
            itemDictionary[keyName] = keyProperty.GetValue(item);

            // Add the item's dictionary representation to the output list.
            items.Add(itemDictionary);
        }

        // Serialize the resulting array of per-item dictionaries to JSON using the provided
        // options -- this is the exact shape Read() parses back.
        string json = JsonSerializer.Serialize(items, options);

        // We're done, write the JSON string to the writer.
        writer.WriteRawValue(json);
    }
}
