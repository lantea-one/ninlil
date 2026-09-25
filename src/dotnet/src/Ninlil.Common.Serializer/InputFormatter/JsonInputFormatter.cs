using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.InputFormatter;

/// <summary>
///     This MVC input formatter reads JSON request bodies (UTF-8, for <c>application/json</c>,
///     <c>text/json</c>, the <c>jsonc</c> variants and their <c>+json</c> suffixes) using
///     <see cref="JsonSerializer" /> with web defaults unless options are given.
///     <see cref="Extension.ServiceCollectionSerializerExtension" /> registers it.
/// </summary>
public class JsonInputFormatter : TextInputFormatter
{
    /// <summary>
    ///     This field contains optional serializer options.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     This constructor sets the supported encoding and media types.
    /// </summary>
    public JsonInputFormatter()
    {
        // Clear any supported encodings.
        SupportedEncodings.Clear();

        // Add our supported encodings.
        SupportedEncodings.Add(Encoding.UTF8);

        // Clear any supported media types.
        SupportedMediaTypes.Clear();

        // Add our supported media types.
        SupportedMediaTypes.Add(MediaTypeNames.Application.Json);
        SupportedMediaTypes.Add("application/*+json");
        SupportedMediaTypes.Add("application/jsonc");
        SupportedMediaTypes.Add("application/*+jsonc");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("text/*+json");
        SupportedMediaTypes.Add("text/jsonc");
        SupportedMediaTypes.Add("text/*+jsonc");
    }

    /// <summary>
    ///     This constructor uses the given serializer <paramref name="options" />.
    /// </summary>
    /// <param name="options">Serializer options to control the serialization of objects.</param>
    public JsonInputFormatter(JsonSerializerOptions options)
        : this() => _options = options;

    /// <summary>
    ///     This method reads and deserializes the request body. An empty body gives no value.
    /// </summary>
    /// <param name="context">The serialization context.</param>
    /// <param name="encoding">The input encoding.</param>
    /// <returns>An awaitable task containing the formatting result.</returns>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(
        InputFormatterContext context,
        Encoding encoding
    )
    {
        // Check for an empty request body then return no value.
        if (context.HttpContext.Request.ContentLength == 0)
            return await InputFormatterResult.NoValueAsync();

        // Instantiate our stream reader.
        using StreamReader jsonReader = new StreamReader(context.HttpContext.Request.Body);

        // Localize the JSON body.
        string json = await jsonReader.ReadToEndAsync();

        // Make sure we have JSON content, return no value if not.
        if (json.Trim() is null or "")
            return await InputFormatterResult.NoValueAsync();

        // We're done, deserialize the JSON and return the result.
        return await InputFormatterResult.SuccessAsync(
            JsonSerializer.Deserialize(
                context.ModelType,
                json,
                JsonSerializer.GetOptions(_options ?? new(JsonSerializerDefaults.Web))
            )
        );
    }
}
