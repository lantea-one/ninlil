using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This class maintains the operation filter structure for representing
///     <code>FromXmlQuery</code> values in Swagger/ReDoc documentation.
/// </summary>
public class FromXmlQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method applies our filter to the <paramref name="operation" /> in the <paramref name="context" />.
    /// </summary>
    /// <param name="operation">The operation to filter.</param>
    /// <param name="context">The current context of the operation.</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Define our list of XML query parameters.
        List<string> xmlQueryParameters = context
            .ApiDescription.ActionDescriptor.Parameters.Where(p =>
                p.BindingInfo?.BinderType == typeof(FromXmlQueryModelBinder)
            )
            .Select(p => p.Name)
            .ToList();

        // Ensure we have properties to work with.
        if (!xmlQueryParameters.Any())
            return;

        // Iterate over the properties that are from a XML query parameter.
        foreach (
            var parameter in operation.Parameters?.Where(p => xmlQueryParameters.Contains(p.Name))
                ?? []
        )
        {
            // Ensure the parameter has content to work with.
            if (parameter?.Content is null)
                continue;

            // Move the schema to the XML content type.
            parameter.Content[MediaTypeNames.Application.Xml] = new() { Schema = parameter.Schema };
        }
    }
}
