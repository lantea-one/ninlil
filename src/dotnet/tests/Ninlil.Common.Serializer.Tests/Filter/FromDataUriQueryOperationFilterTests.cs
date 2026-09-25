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
///     These tests run <see cref="FromDataUriQueryOperationFilter" /> against a hand-built filter
///     context (see <see cref="FromJsonQueryOperationFilterTests" />). Unlike the JSON and XML
///     filters, it clears any existing content entries before adding <c>text/plain</c>, and the
///     tests check that.
/// </summary>
public class FromDataUriQueryOperationFilterTests
{
    [Fact]
    public void Apply_ParameterBoundByFromDataUriQuery_ReplacesContentWithTextPlain()
    {
        var actionDescriptor = new ActionDescriptor
        {
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = "payload",
                    BindingInfo = new BindingInfo
                    {
                        BinderType = typeof(FromDataUriQueryModelBinder),
                    },
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
                        ["application/json"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            ],
        };
        var context = new OperationFilterContext(apiDescription, null, null, null, null);

        new FromDataUriQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.True(parameter.Content.ContainsKey("text/plain"));

        // The pre-existing entry must be cleared, not just added alongside.
        Assert.False(parameter.Content.ContainsKey("application/json"));
        Assert.Single(parameter.Content);
    }

    [Fact]
    public void Apply_ParameterNotBoundByFromDataUriQuery_LeavesContentUnchanged()
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
                        ["application/json"] = new()
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                    },
                },
            ],
        };
        var context = new OperationFilterContext(apiDescription, null, null, null, null);

        new FromDataUriQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.True(parameter.Content.ContainsKey("application/json"));
        Assert.False(parameter.Content.ContainsKey("text/plain"));
    }
}
