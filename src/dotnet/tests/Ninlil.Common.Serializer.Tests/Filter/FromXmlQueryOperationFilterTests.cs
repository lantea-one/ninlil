using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.OpenApi;
using Ninlil.Common.Serializer.Filter;
using Ninlil.Common.Serializer.ModelBinder;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Filter;

/// <summary>
///     These tests check that <see cref="FromXmlQueryOperationFilter" /> documents XML-bound
///     parameters as XML content. The <see cref="OperationFilterContext" /> is built as in
///     <see cref="FromJsonQueryOperationFilterTests" />.
/// </summary>
public class FromXmlQueryOperationFilterTests
{
    [Fact]
    public void Apply_ParameterBoundByFromXmlQuery_AddsXmlContentType()
    {
        var actionDescriptor = new ActionDescriptor
        {
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = "payload",
                    BindingInfo = new BindingInfo { BinderType = typeof(FromXmlQueryModelBinder) },
                },
            ],
        };
        var apiDescription = new ApiDescription { ActionDescriptor = actionDescriptor };
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "payload",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["*/*"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            ],
        };
        var context = new OperationFilterContext(apiDescription, null, null, null, null);

        new FromXmlQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.True(parameter.Content.ContainsKey("application/xml"));
    }

    [Fact]
    public void Apply_ParameterNotBoundByFromXmlQuery_LeavesOperationUnchanged()
    {
        var actionDescriptor = new ActionDescriptor
        {
            Parameters = [new ParameterDescriptor { Name = "payload", BindingInfo = null }],
        };
        var apiDescription = new ApiDescription { ActionDescriptor = actionDescriptor };
        var operation = new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "payload",
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["*/*"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            ],
        };
        var context = new OperationFilterContext(apiDescription, null, null, null, null);

        new FromXmlQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.False(parameter.Content.ContainsKey("application/xml"));
    }
}
