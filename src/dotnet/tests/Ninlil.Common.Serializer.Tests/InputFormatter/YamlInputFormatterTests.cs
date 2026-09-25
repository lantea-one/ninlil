using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Common.Serializer.InputFormatter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.InputFormatter;

/// <summary>
///     These tests call <see cref="YamlInputFormatter" />'s <c>ReadRequestBodyAsync</c> directly,
///     with the request context built as in <see cref="JsonInputFormatterTests" />.
/// </summary>
public class YamlInputFormatterTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    private static InputFormatterContext BuildContext(string body, Type modelType)
    {
        var httpContext = new DefaultHttpContext();
        byte[] bytes = Encoding.UTF8.GetBytes(body ?? string.Empty);
        httpContext.Request.Body = new MemoryStream(bytes);
        httpContext.Request.ContentLength = body is null ? 0 : bytes.Length;

        var metadataProvider = new EmptyModelMetadataProvider();
        ModelMetadata metadata = metadataProvider.GetMetadataForType(modelType);

        return new InputFormatterContext(
            httpContext,
            modelName: "model",
            modelState: new ModelStateDictionary(),
            metadata: metadata,
            readerFactory: (stream, encoding) => new StreamReader(stream, encoding)
        );
    }

    [Fact]
    public async Task ReadRequestBodyAsync_ValidYaml_ReturnsDeserializedValue()
    {
        InputFormatterContext context = BuildContext("value: x\n", typeof(Simple));

        InputFormatterResult result = await new YamlInputFormatter().ReadRequestBodyAsync(
            context,
            Encoding.UTF8
        );

        Assert.False(result.HasError);
        var simple = Assert.IsType<Simple>(result.Model);
        Assert.Equal("x", simple.Value);
    }

    [Fact]
    public async Task ReadRequestBodyAsync_EmptyContentLength_ReturnsNoValue()
    {
        InputFormatterContext context = BuildContext(null, typeof(Simple));

        InputFormatterResult result = await new YamlInputFormatter().ReadRequestBodyAsync(
            context,
            Encoding.UTF8
        );

        Assert.False(result.IsModelSet);
    }
}
