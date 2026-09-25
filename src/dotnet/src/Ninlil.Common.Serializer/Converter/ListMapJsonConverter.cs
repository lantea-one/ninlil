using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This class is a JSON converter for a "list map": a JSON array of objects in which one
///     property of <typeparamref name="TItem" /> (marked with <see cref="ListMapKeyAttribute" />) is
///     the item's unique key. Reading deserializes each element normally, then sets the key
///     property by reflection. Writing serializes each item normally, then adds the key value under
///     the key's JSON name. Both steps matter when the key property is hidden from ordinary
///     serialization with <see cref="JsonIgnoreAttribute" />; for a visible key they are a
///     harmless re-assignment. Apply it to a <see cref="List{TItem}" /> property or field.
/// </summary>
/// <typeparam name="TItem">
///     The item type. It must have a property marked with <see cref="ListMapKeyAttribute" />.
/// </typeparam>
public class ListMapJsonConverter<TItem> : JsonConverter<List<TItem>>
{
    /// <summary>
    ///     This method finds the public property of <typeparamref name="TItem" /> marked with
    ///     <see cref="ListMapKeyAttribute" />.
    /// </summary>
    /// <returns>The located key <see cref="PropertyInfo" />.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <typeparamref name="TItem" /> has no property marked with
    ///     <see cref="ListMapKeyAttribute" />.
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
    ///     property name.
    /// </summary>
    /// <param name="keyProperty">The key property to resolve the JSON name of.</param>
    /// <returns>The resolved JSON property name.</returns>
    private static string GetKeyName(PropertyInfo keyProperty) =>
        keyProperty.GetCustomAttribute<JsonPropertyNameAttribute>(true)?.Name
        ?? keyProperty.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true)?.PropertyName
        ?? keyProperty.Name;

    /// <summary>
    ///     This method reads a JSON array into a <see cref="List{TItem}" />, setting each item's key
    ///     property by reflection from the element's key value.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">
    ///     The <see cref="Type" /> of object to convert, a <see cref="List{TItem}" />.
    /// </param>
    /// <param name="options">
    ///     The <see cref="JsonSerializerOptions" /> to use for deserializing each individual item and its
    ///     key value.
    /// </param>
    /// <returns>
    ///     The deserialized <see cref="List{TItem}" />, or an empty list when the JSON is not an array.
    ///     Any element that is not a JSON object, or whose key is missing or null, is skipped.
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

        // Parse the value into a JsonDocument so each element's key can be read.
        using var jsonDocument = JsonDocument.ParseValue(ref reader);

        // If the JSON value is not an array, return an empty list.
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

            // Deserialize the key value as the key property's type.
            var itemKey = JsonSerializer.Deserialize(
                keyProperty.PropertyType,
                keyPropertyValue.GetRawText(),
                options
            );

            // If the item key is null, skip it.
            if (itemKey is null)
                continue;

            // Deserialize the raw JSON element into an instance of TItem.
            var item = JsonSerializer.Deserialize<TItem>(element.GetRawText(), options);

            // Set the key by reflection. This populates a JsonIgnore'd key property and is a
            // harmless re-assignment for a visible one.
            keyProperty.SetValue(item, itemKey);

            // Add the item to the list of TItem.
            items.Add(item);
        }

        // We're done, return our list of TItem.
        return items;
    }

    /// <summary>
    ///     This method writes a <see cref="List{TItem}" /> as a JSON array, serializing each item
    ///     normally and then adding its key value under the key property's JSON name, so the output is
    ///     what <see cref="Read" /> expects.
    /// </summary>
    /// <param name="writer">
    ///     The <see cref="Utf8JsonWriter" /> to write to.
    /// </param>
    /// <param name="value">
    ///     The items to serialize. Null writes an empty array.
    /// </param>
    /// <param name="options">
    ///     The <see cref="JsonSerializerOptions" /> to use for each item.
    /// </param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown when <typeparamref name="TItem" /> has no property marked with
    ///     <see cref="ListMapKeyAttribute" />.
    /// </exception>
    public override void Write(
        Utf8JsonWriter writer,
        List<TItem> value,
        JsonSerializerOptions options
    )
    {
        // Localize the key property and its JSON name, needed to add each item's key below.
        PropertyInfo keyProperty = GetKeyProperty();
        string keyName = GetKeyName(keyProperty);

        // Define the list of per-item dictionaries that will become the output JSON array.
        var items = new List<Dictionary<string, object>>();

        // Iterate through each item in the collection of TItem.
        foreach (var item in value ?? [])
        {
            // Serialize the item normally (respecting its JsonIgnore and naming attributes).
            string itemJson = JsonSerializer.Serialize(item, options);

            // Decode the item's JSON into a dictionary so the key can be added.
            var itemDictionary =
                JsonSerializer.Deserialize<Dictionary<string, object>>(itemJson, options) ?? [];

            // Add the key's value under its JSON name. This makes a JsonIgnore'd key round-trip
            // and overwrites a visible key with the same value.
            itemDictionary[keyName] = keyProperty.GetValue(item);

            // Add the item's dictionary representation to the output list.
            items.Add(itemDictionary);
        }

        // Serialize the array of per-item dictionaries, the shape Read() parses back.
        string json = JsonSerializer.Serialize(items, options);

        // We're done, write the JSON string to the writer.
        writer.WriteRawValue(json);
    }
}
