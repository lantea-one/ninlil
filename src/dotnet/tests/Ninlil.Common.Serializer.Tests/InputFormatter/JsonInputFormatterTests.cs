using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Common.Serializer.InputFormatter;

namespace Ninlil.Common.Serializer.Tests.InputFormatter;

/// <summary>
///     This class contains unit tests for <see cref="JsonInputFormatter" />. Its
///     <c>ReadRequestBodyAsync</c> override is exercised directly against a real
///     <see cref="DefaultHttpContext" />/<see cref="InputFormatterContext" /> pair, which is
///     everything the method itself actually reads -- the rest of the ASP.NET Core MVC formatter
///     pipeline (content negotiation, <c>CanRead</c> checks) is not exercised here, since this
///     project has no committed integration-test host to exercise it against; that would need a
///     <c>WebApplicationFactory</c>-style test instead.
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
        // The options-carrying constructor must actually be respected when reading.
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
