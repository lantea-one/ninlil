using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Ninlil.Common.Serializer.OutputFormatter;

namespace Ninlil.Common.Serializer.Tests.OutputFormatter;

/// <summary>
///     This class contains unit tests for <see cref="JsonOutputFormatter" />. Its
///     <c>WriteResponseBodyAsync</c> override is exercised directly against a real
///     <see cref="DefaultHttpContext" />/<see cref="OutputFormatterWriteContext" /> pair, writing
///     into an in-memory response body stream -- the rest of the ASP.NET Core MVC formatter
///     pipeline (content negotiation) is not exercised here; see
///     <see cref="InputFormatter.JsonInputFormatterTests" /> for the matching read-side note.
/// </summary>
public class JsonOutputFormatterTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    private static async Task<(DefaultHttpContext HttpContext, string Body)> WriteAsync(
        TextOutputFormatter formatter,
        object payload,
        Type objectType
    )
    {
        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var context = new OutputFormatterWriteContext(
            httpContext,
            (stream, encoding) => new StreamWriter(stream, encoding),
            objectType,
            payload
        );

        await formatter.WriteResponseBodyAsync(context, Encoding.UTF8);

        responseBody.Seek(0, SeekOrigin.Begin);
        return (httpContext, new StreamReader(responseBody, Encoding.UTF8).ReadToEnd());
    }

    [Fact]
    public async Task WriteResponseBodyAsync_WritesSerializedJson_CamelCased()
    {
        // Unlike JsonSerializer.GetOptions() (no naming policy, PascalCase-verbatim, per this
        // project's documented "internal storage is always PascalCase" convention), the default
        // JsonOutputFormatter falls back to `new JsonSerializerOptions(JsonSerializerDefaults.Web)`
        // when no explicit options are supplied, which sets a camelCase naming policy -- this is
        // the REST response boundary where that conversion is documented to happen.
        (_, string body) = await WriteAsync(
            new JsonOutputFormatter(),
            new Simple { Value = "x" },
            typeof(Simple)
        );

        Assert.Contains("\"value\":\"x\"", body);
    }

    [Fact]
    public async Task WriteResponseBodyAsync_PrettyFlag_ProducesIndentedOutput()
    {
        (_, string compact) = await WriteAsync(
            new JsonOutputFormatter(pretty: false),
            new Simple { Value = "x" },
            typeof(Simple)
        );
        (_, string pretty) = await WriteAsync(
            new JsonOutputFormatter(pretty: true),
            new Simple { Value = "x" },
            typeof(Simple)
        );

        Assert.DoesNotContain("\n", compact);
        Assert.Contains("\n", pretty);
    }

    [Fact]
    public void Constructor_FromOptions_DerivesPrettyFromWriteIndented()
    {
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };

        var formatter = new JsonOutputFormatter(options);

        Assert.Contains(
            "application/json",
            formatter.SupportedMediaTypes.Select(m => m.ToString())
        );
    }
}
