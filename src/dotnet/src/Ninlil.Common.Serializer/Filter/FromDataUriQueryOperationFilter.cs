using System.Net.Mime;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Common.Serializer.Filter;

/// <summary>
///     This Swagger operation filter documents query parameters bound by
///     <see cref="FromDataUriQueryModelBinder" /> as plain-text content (a data URI string).
///     Register it with
///     <see cref="Extension.SwaggerGenOptionsSerializerExtension.AddSerializerDataUriQueryFilters" />.
/// </summary>
public class FromDataUriQueryOperationFilter : IOperationFilter
{
    /// <summary>
    ///     This method replaces the content of every parameter of <paramref name="operation" /> that
    ///     uses the data URI binder (and already has content) with a single <c>text/plain</c> entry.
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
