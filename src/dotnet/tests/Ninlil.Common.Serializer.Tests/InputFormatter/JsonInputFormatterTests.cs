using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Common.Serializer.InputFormatter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.InputFormatter;

/// <summary>
///     These tests call <see cref="JsonInputFormatter" />'s <c>ReadRequestBodyAsync</c> directly
///     with a <see cref="DefaultHttpContext" /> and <see cref="InputFormatterContext" />, which is all
///     the method reads. Content negotiation and <c>CanRead</c> checks belong to the MVC pipeline and
///     would need a <c>WebApplicationFactory</c>-style host, so they are not covered here.
/// </summary>
public class JsonInputFormatterTests
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
    public async Task ReadRequestBodyAsync_ValidJson_ReturnsDeserializedValue()
    {
        InputFormatterContext context = BuildContext("""{"Value":"x"}""", typeof(Simple));

        InputFormatterResult result = await new JsonInputFormatter().ReadRequestBodyAsync(
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

        InputFormatterResult result = await new JsonInputFormatter().ReadRequestBodyAsync(
            context,
            Encoding.UTF8
        );

        Assert.True(result.IsModelSet is false);
    }

    [Fact]
    public async Task ReadRequestBodyAsync_UsesProvidedOptions()
    {
        // The options passed to the constructor must be used when reading.
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };
        InputFormatterContext context = BuildContext(
            """{"value":"lowercase-key"}""",
            typeof(Simple)
        );

        InputFormatterResult result = await new JsonInputFormatter(options).ReadRequestBodyAsync(
            context,
            Encoding.UTF8
        );

        var simple = Assert.IsType<Simple>(result.Model);
        Assert.Equal("lowercase-key", simple.Value);
    }

    [Fact]
    public void Constructor_Default_RegistersExpectedMediaTypes()
    {
        var formatter = new JsonInputFormatter();

        Assert.Contains(
            "application/json",
            formatter.SupportedMediaTypes.Select(m => m.ToString())
        );
        Assert.Contains(
            "application/jsonc",
            formatter.SupportedMediaTypes.Select(m => m.ToString())
        );
    }
}
