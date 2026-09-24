using System.Text.Json;

// Define our namespace.
namespace Ninlil.Web.Extension;

/// <summary>
///     This class provides our <see cref="JsonElement" /> extension for reading a single field
///     out of an RFC 7396 JSON Merge Patch request body -- the PATCH format every mutable
///     resource this project's REST API exposes. A field absent from the patch document is left
///     untouched; a field present with a JSON <c>null</c> clears it; a field present with any
///     other value overwrites it -- exactly RFC 7396's own merge semantics, applied field-by-field
///     by each controller's own PATCH action rather than through a generic reflection-based
///     merger, since this project's DTOs are few enough that hand-mapping each field stays
///     readable.
/// </summary>
public static class JsonElementMergePatchExtension
{
    /// <summary>
    ///     This method looks up a single field in a merge patch document.
    /// </summary>
    /// <param name="instance">The patch document to look up the field in.</param>
    /// <param name="propertyName">The field's own JSON property name (camelCase).</param>
    /// <param name="value">The field's value, if present.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether the field was present at all -- the
    ///     caller must still check <paramref name="value" />'s own
    ///     <see cref="JsonElement.ValueKind" /> for <see cref="JsonValueKind.Null" /> to tell a
    ///     "clear this field" instruction apart from a genuine replacement value.
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
