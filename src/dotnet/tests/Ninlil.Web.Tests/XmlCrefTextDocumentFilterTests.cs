using Microsoft.OpenApi;
using Ninlil.Web.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the line-ending normalisation of <see cref="XmlCrefTextDocumentFilter" />
///     by running the filter directly over a hand-built OpenAPI document, with no host. The C#
///     compiler writes the XML documentation file with the line endings of the platform it runs
///     on, so a build on Windows puts <c>\r\n</c> into every multi-line summary that Swashbuckle
///     copies into the document; the filter must turn them into <c>\n</c> so the document is the
///     same on every platform.
/// </summary>
public class XmlCrefTextDocumentFilterTests
{
    [Fact]
    public void Apply_WithCrLfAndLoneCrInDocumentText_ReplacesThemWithLf()
    {
        OpenApiSchema property = new() { Description = "Property\r\nline two" };
        OpenApiSchema schema = new()
        {
            Description = "Schema\r\nline two\rline three",
            Properties = new Dictionary<string, IOpenApiSchema> { ["name"] = property },
        };
        OpenApiParameter parameter = new()
        {
            Name = "id",
            In = ParameterLocation.Query,
            Description = "Parameter\r\nline two",
        };
        OpenApiOperation operation = new()
        {
            Summary = "Operation\r\nline two",
            Description = "Details\r\nline two",
            Parameters = [parameter],
        };
        OpenApiDocument document = new()
        {
            Paths = new OpenApiPaths
            {
                ["/api/example"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<HttpMethod, OpenApiOperation>
                    {
                        [HttpMethod.Get] = operation,
                    },
                },
            },
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema> { ["Example"] = schema },
            },
        };

        new XmlCrefTextDocumentFilter().Apply(
            document,
            new DocumentFilterContext([], null, new SchemaRepository())
        );

        Assert.Equal("Operation\nline two", operation.Summary);
        Assert.Equal("Details\nline two", operation.Description);
        Assert.Equal("Parameter\nline two", parameter.Description);
        Assert.Equal("Schema\nline two\nline three", schema.Description);
        Assert.Equal("Property\nline two", property.Description);
    }
}
