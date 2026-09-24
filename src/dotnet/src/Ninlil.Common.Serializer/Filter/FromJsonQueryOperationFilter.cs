using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This class maintains the operation filter structure for representing
///     <code>FromJsonQuery</code> values in Swagger/ReDoc documentation.
/// </summary>
public class FromJsonQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method applies our filter to the <paramref name="operation" /> in the <paramref name="context" />.
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
