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
///     These tests check that <see cref="FromJsonQueryOperationFilter" /> documents parameters bound
///     by <see cref="FromJsonQueryModelBinder" /> under the <c>application/json</c> content type and
///     leaves other parameters alone.
/// </summary>
public class FromJsonQueryOperationFilterTests
{
    /// <summary>
    ///     This method builds a minimal <see cref="OperationFilterContext" /> with one action parameter
    ///     bound by <paramref name="binderType" /> (or unbound, if <see langword="null" />), and a
    ///     matching <see cref="OpenApiOperation" /> with one <see cref="OpenApiParameter" /> of the same
    ///     name and some existing content. Only the members the filters read are set; the schema
    ///     generator, repository, document and action method are left null.
    /// </summary>
    private static (OpenApiOperation Operation, OperationFilterContext Context) BuildContext(
        string parameterName,
        Type binderType
    )
    {
        var actionDescriptor = new ActionDescriptor
        {
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = parameterName,
                    BindingInfo = binderType is null
                        ? null
                        : new BindingInfo { BinderType = binderType },
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
                    Name = parameterName,
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

        return (operation, context);
    }

    [Fact]
    public void Apply_ParameterBoundByFromJsonQuery_AddsJsonContentType()
    {
        (OpenApiOperation operation, OperationFilterContext context) = BuildContext(
            "payload",
            typeof(FromJsonQueryModelBinder)
        );

        new FromJsonQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.True(parameter.Content.ContainsKey("application/json"));
    }

    [Fact]
    public void Apply_ParameterNotBoundByFromJsonQuery_LeavesOperationUnchanged()
    {
        (OpenApiOperation operation, OperationFilterContext context) = BuildContext(
            "payload",
            null
        );

        new FromJsonQueryOperationFilter().Apply(operation, context);

        IOpenApiParameter parameter = Assert.Single(operation.Parameters);
        Assert.False(parameter.Content.ContainsKey("application/json"));
    }

    [Fact]
    public void Apply_NoParametersAtAll_DoesNotThrow()
    {
        var actionDescriptor = new ActionDescriptor { Parameters = [] };
        var apiDescription = new ApiDescription { ActionDescriptor = actionDescriptor };
        var operation = new OpenApiOperation { Parameters = [] };
        var context = new OperationFilterContext(apiDescription, null, null, null, null);

        var exception = Record.Exception(() =>
            new FromJsonQueryOperationFilter().Apply(operation, context)
        );

        Assert.Null(exception);
    }
}
