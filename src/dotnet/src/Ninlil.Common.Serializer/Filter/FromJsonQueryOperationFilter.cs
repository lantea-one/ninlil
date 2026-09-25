using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This Swagger operation filter documents query parameters bound by
///     <see cref="FromJsonQueryModelBinder" /> as JSON content, so Swagger/ReDoc show that the
///     parameter carries a JSON document. Register it with
///     <see cref="Extension.SwaggerGenOptionsSerializerExtension.AddSerializerJsonQueryFilters" />.
/// </summary>
public class FromJsonQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method adds a JSON content entry, with the parameter's schema, to every parameter of
    ///     <paramref name="operation" /> that uses the JSON query binder and already has content.
    /// </summary>
    /// <param name="operation">The operation to filter.</param>
    /// <param name="context">The current context of the operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Define our list of JSON query parameters.
        List<string> jsonQueryParameters = context
            .ApiDescription.ActionDescriptor.Parameters.Where(p =>
                p.BindingInfo?.BinderType == typeof(FromJsonQueryModelBinder)
            )
            .Select(p => p.Name)
            .ToList();

        // Ensure we have properties to work with.
        if (!jsonQueryParameters.Any())
            return;

        // Iterate over the properties that are from a JSON query parameter.
        foreach (
            var parameter in operation.Parameters?.Where(p => jsonQueryParameters.Contains(p.Name))
                ?? []
        )
        {
            // Ensure we have content to work with.
            if (parameter?.Content is null)
                continue;

            // Move the schema to the JSON content type.
            parameter.Content[MediaTypeNames.Application.Json] = new()
            {
                Schema = parameter.Schema,
            };
        }
    }
}
