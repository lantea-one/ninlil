using System.Text.Json;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="JsonElement" /> extension for reading one field of an
///     RFC 7396 JSON Merge Patch body, the format of every <c>PATCH</c> in the REST API. An
///     absent field is left as it is, a field set to JSON <c>null</c> is cleared, and any other
///     value replaces the field. Each controller's <c>PATCH</c> action applies these rules field
///     by field rather than through a generic reflection-based merger, which stays readable for
///     the number of models involved.
/// </summary>
public static class JsonElementMergePatchExtension
{
    /// <summary>
    ///     This method looks up a single field in a merge patch document.
    /// </summary>
    /// <param name="instance">The patch document to look up the field in.</param>
    /// <param name="propertyName">The field's JSON property name (camelCase).</param>
    /// <param name="value">The field's value, if present.</param>
    /// <returns>
    ///     Whether the field is present; always <see langword="false" /> when the document is not
    ///     a JSON object. The caller must still check <paramref name="value" />'s
    ///     <see cref="JsonElement.ValueKind" /> for <see cref="JsonValueKind.Null" /> to tell
    ///     "clear this field" from a replacement value.
    /// </returns>
    public static bool TryGetMergePatchField(
        this JsonElement instance,
        string propertyName,
        out JsonElement value
    )
    {
        value = default;
        return instance.ValueKind == JsonValueKind.Object
            && instance.TryGetProperty(propertyName, out value);
    }
}
