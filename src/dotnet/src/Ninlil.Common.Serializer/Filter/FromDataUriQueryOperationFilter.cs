using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This class maintains the operation filter structure for representing
///     <code>FromDataUriQuery</code> values in Swagger/ReDoc documentation.
/// </summary>
public class FromDataUriQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method applies our filter to the <paramref name="operation" /> in the <paramref name="context" />.
    /// </summary>
    /// <param name="operation">The operation to filter.</param>
    /// <param name="context">The current context of the operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Define our list of our query parameters.
        List<string> queryParameters = context
            .ApiDescription.ActionDescriptor.Parameters.Where(p =>
                p.BindingInfo?.BinderType == typeof(FromDataUriQueryModelBinder)
            )
            .Select(p => p.Name)
            .ToList();

        // Ensure we have properties to work with.
        if (!queryParameters.Any())
            return;

        // Iterate over the properties that are from our query parameter.
        foreach (
            var parameter in operation.Parameters?.Where(p => queryParameters.Contains(p.Name))
                ?? []
        )
        {
            // Ensure we have content to work with.
            if (parameter?.Content is null)
                continue;

            // Clear the content for the parameter.
            parameter.Content.Clear();

            // Move the schema to our content type.
            parameter.Content[MediaTypeNames.Text.Plain] = new() { Schema = parameter.Schema };
        }
    }
}
