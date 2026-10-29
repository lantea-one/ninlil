using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Web.OpenApi;

/// <summary>
///     This class reshapes each operation of the published OpenAPI document so the admin console's
///     generated API client gets the names and media types the console uses. It changes only the
///     document, never what the server accepts or returns. <see cref="Program" /> registers it
///     with <c>SwaggerGenOptions.OperationFilter</c>.
///     <list type="bullet">
///         <item>
///             Query parameter names and multipart form field names start with a lower-case
///             letter (<c>Skip</c> becomes <c>skip</c>, <c>Archive</c> becomes <c>archive</c>).
///             The document may rename them because ASP.NET Core binds query and form values to
///             model properties case-insensitively, so both spellings reach the same property.
///             The client then exposes camel-case parameter keys.
///         </item>
///         <item>
///             A request body or response that lists <c>application/json</c> lists only that
///             type. Swashbuckle also lists <c>text/json</c>, <c>text/plain</c> and
///             <c>application/*+json</c> because the JSON formatters declare them; the server still
///             accepts and produces them, but one type per operation gives the client one method
///             per operation. <c>multipart/form-data</c> bodies are left alone.
///         </item>
///     </list>
/// </summary>
public class ClientShapeOperationFilter : IOperationFilter
{
    private const string JsonMediaType = "application/json";

    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (IOpenApiParameter parameter in operation.Parameters ?? [])
        {
            if (parameter.In == ParameterLocation.Query && parameter is OpenApiParameter writable)
                writable.Name = ToCamelCase(writable.Name);
        }

        if (operation.RequestBody?.Content is { } requestContent)
        {
            KeepOnlyJson(requestContent);
            foreach (OpenApiMediaType mediaType in requestContent.Values)
                RenameFormFields(mediaType);
        }

        if (operation.Responses is not null)
        {
            foreach (IOpenApiResponse response in operation.Responses.Values)
            {
                if (response.Content is { } responseContent)
                    KeepOnlyJson(responseContent);
            }
        }
    }

    /// <summary>
    ///     This method removes every media type except <c>application/json</c> when the
    ///     dictionary lists <c>application/json</c>, and does nothing otherwise.
    /// </summary>
    private static void KeepOnlyJson(IDictionary<string, OpenApiMediaType> content)
    {
        if (!content.ContainsKey(JsonMediaType))
            return;

        foreach (string mediaTypeName in content.Keys.Where(name => name != JsonMediaType).ToList())
            content.Remove(mediaTypeName);
    }

    /// <summary>
    ///     This method renames the properties of a form-style body schema and the matching
    ///     <c>encoding</c> keys, which must keep naming the same fields.
    /// </summary>
    private static void RenameFormFields(OpenApiMediaType mediaType)
    {
        if (mediaType.Schema is not OpenApiSchema schema || schema.Properties is not { Count: > 0 })
            return;

        // Only the multipart form is renamed. A JSON body keeps its schema's property names,
        // which the JSON serializer matches to the model itself.
        if (mediaType.Encoding is not { Count: > 0 } encoding)
            return;

        // Entries are re-added in their original order so the client keeps the field order.
        RenameKeys(schema.Properties);
        RenameKeys(encoding);

        if (schema.Required is { Count: > 0 } required)
        {
            string[] names = [.. required];
            required.Clear();
            foreach (string name in names)
                required.Add(ToCamelCase(name));
        }
    }

    /// <summary>
    ///     This method renames every key of a dictionary to camel case, keeping the order.
    /// </summary>
    private static void RenameKeys<TValue>(IDictionary<string, TValue> dictionary)
    {
        List<KeyValuePair<string, TValue>> entries = [.. dictionary];
        dictionary.Clear();
        foreach (KeyValuePair<string, TValue> entry in entries)
            dictionary[ToCamelCase(entry.Key)] = entry.Value;
    }

    /// <summary>
    ///     This method lower-cases the first letter of a name.
    /// </summary>
    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
