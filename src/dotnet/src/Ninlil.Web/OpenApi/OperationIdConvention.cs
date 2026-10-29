using System.Text;

// Define our namespace.
namespace Ninlil.Web.OpenApi;

/// <summary>
///     This class holds the rule that gives every operation in the published OpenAPI document its
///     <c>operationId</c>. The id is the route path, written in camel case, followed by the HTTP
///     method: <c>GET /api/zones/{id}/catalog-members</c> becomes
///     <c>apiZonesIdCatalogMembersGet</c>.
///     <para>
///         The rule must stay this one. The admin console's generated API client names its methods
///         after these ids, and the ids equal the method names the client had before the document
///         carried them, so a changed rule renames client methods and breaks every call site (and
///         any third-party client). <see cref="Program" /> registers it through
///         <c>SwaggerGenOptions.CustomOperationIds</c>.
///     </para>
/// </summary>
public static class OperationIdConvention
{
    /// <summary>
    ///     This method builds the operation id for one route and HTTP method.
    /// </summary>
    /// <param name="relativePath">
    ///     The route template without the leading slash and without a query string, such as
    ///     <c>api/zones/{id}/catalog-members</c>. A route constraint (<c>{id:guid}</c>) or an
    ///     optional marker (<c>{id?}</c>) does not change the id.
    /// </param>
    /// <param name="httpMethod">The HTTP method in any case, such as <c>GET</c>.</param>
    /// <returns>The operation id, such as <c>apiZonesIdCatalogMembersGet</c>.</returns>
    public static string Build(string relativePath, string httpMethod)
    {
        StringBuilder builder = new();

        foreach (string segment in (relativePath ?? "").Split('/'))
        {
            // A parameter segment keeps its name ("{id}" contributes "Id"); only the braces, the
            // constraint after a colon and the optional marker are dropped.
            string name = segment.Trim('{', '}');
            int constraintIndex = name.IndexOf(':');
            if (constraintIndex >= 0)
                name = name[..constraintIndex];
            name = name.TrimEnd('?');

            foreach (string word in name.Split('-', '_', '.'))
                AppendPascalCase(builder, word);
        }

        if (builder.Length > 0)
            builder[0] = char.ToLowerInvariant(builder[0]);

        // The method arrives in upper case ("GET") and is written as "Get".
        string method = (httpMethod ?? "").ToLowerInvariant();
        AppendPascalCase(builder, method);
        return builder.ToString();
    }

    /// <summary>
    ///     This method appends a word with its first letter in upper case and the rest as it is
    ///     (so <c>zoneId</c> becomes <c>ZoneId</c>, not <c>Zoneid</c>).
    /// </summary>
    private static void AppendPascalCase(StringBuilder builder, string word)
    {
        if (word.Length == 0)
            return;

        builder.Append(char.ToUpperInvariant(word[0]));
        builder.Append(word, 1, word.Length - 1);
    }
}
