using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This Swagger operation filter documents query parameters bound by
///     <see cref="FromXmlQueryModelBinder" /> as XML content, so Swagger/ReDoc show that the
///     parameter carries an XML document. Register it with
///     <see cref="Extension.SwaggerGenOptionsSerializerExtension.AddSerializerXmlQueryFilters" />.
/// </summary>
public class FromXmlQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method adds an XML content entry, with the parameter's schema, to every parameter of
    ///     <paramref name="operation" /> that uses the XML query binder and already has content.
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
