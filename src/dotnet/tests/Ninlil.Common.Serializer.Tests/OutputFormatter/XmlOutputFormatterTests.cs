using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Ninlil.Common.Serializer.OutputFormatter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.OutputFormatter;

/// <summary>
///     These tests call <see cref="XmlOutputFormatter" />'s <c>WriteResponseBodyAsync</c> directly,
///     with the response context built as in <see cref="JsonOutputFormatterTests" />.
/// </summary>
public class XmlOutputFormatterTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    private static async Task<string> WriteAsync(
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
        return new StreamReader(responseBody, Encoding.UTF8).ReadToEnd();
    }

    [Fact]
    public async Task WriteResponseBodyAsync_WritesSerializedXml()
    {
        string body = await WriteAsync(
            new XmlOutputFormatter(),
            new Simple { Value = "x" },
            typeof(Simple)
        );

        Assert.Contains("<Value>x</Value>", body);
    }

    [Fact]
    public async Task WriteResponseBodyAsync_PrettyFlag_ProducesIndentedOutput()
    {
        string compact = await WriteAsync(
            new XmlOutputFormatter(pretty: false),
            new Simple { Value = "x" },
            typeof(Simple)
        );
        string pretty = await WriteAsync(
            new XmlOutputFormatter(pretty: true),
            new Simple { Value = "x" },
            typeof(Simple)
        );

        // XmlSerializer.SerializerOptions.IndentChars is 4 spaces.
        Assert.DoesNotContain("\n    <Value>", compact);
        Assert.Contains("\n    <Value>", pretty);
    }
}
