using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Ninlil.Common.Serializer.OutputFormatter;

namespace Ninlil.Common.Serializer.Tests.OutputFormatter;

/// <summary>
///     This class contains unit tests for <see cref="YamlOutputFormatter" />. See
///     <see cref="JsonOutputFormatterTests" /> for the shared reasoning behind how the response
///     context is constructed for these tests.
/// </summary>
public class YamlOutputFormatterTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    [Fact]
    public async Task WriteResponseBodyAsync_WritesSerializedYaml()
    {
        var httpContext = new DefaultHttpContext();
        var responseBody = new MemoryStream();
        httpContext.Response.Body = responseBody;

        var context = new OutputFormatterWriteContext(
            httpContext,
            (stream, encoding) => new StreamWriter(stream, encoding),
            typeof(Simple),
            new Simple { Value = "x" }
        );

        await new YamlOutputFormatter().WriteResponseBodyAsync(context, Encoding.UTF8);

        responseBody.Seek(0, SeekOrigin.Begin);
        string body = new StreamReader(responseBody, Encoding.UTF8).ReadToEnd();

        Assert.Contains("value", body);
        Assert.Contains("x", body);
    }
}
