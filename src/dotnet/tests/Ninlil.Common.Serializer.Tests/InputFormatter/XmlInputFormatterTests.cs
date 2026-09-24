using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Common.Serializer.InputFormatter;

namespace Ninlil.Common.Serializer.Tests.InputFormatter;

/// <summary>
///     This class contains unit tests for <see cref="XmlInputFormatter" />. See
///     <see cref="JsonInputFormatterTests" /> for the shared reasoning behind how the request
///     context is constructed for these tests.
/// </summary>
public class XmlInputFormatterTests
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
    public async Task ReadRequestBodyAsync_ValidXml_ReturnsDeserializedValue()
    {
        InputFormatterContext context = BuildContext(
            "<Simple><Value>x</Value></Simple>",
            typeof(Simple)
        );

        InputFormatterResult result = await new XmlInputFormatter().ReadRequestBodyAsync(
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

        InputFormatterResult result = await new XmlInputFormatter().ReadRequestBodyAsync(
            context,
            Encoding.UTF8
        );

        Assert.False(result.IsModelSet);
    }
}
