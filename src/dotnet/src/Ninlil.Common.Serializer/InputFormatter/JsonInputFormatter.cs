using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.InputFormatter;

/// <summary>
///     This input formatter provides our JSON capabilities.
/// </summary>
public class JsonInputFormatter : TextInputFormatter
{
    /// <summary>
    ///     This property contains the serializer options.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     This method instantiates our formatter with supported encodings and media types.
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
    ///     This method instantiates our formatter with serialization <paramref name="options" />.
    /// </summary>
    /// <param name="options">Serializer options to control the serialization of objects.</param>
    public JsonInputFormatter(JsonSerializerOptions options)
        : this() => _options = options;

    /// <summary>
    ///     This method reads and deserializes the request body.
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
