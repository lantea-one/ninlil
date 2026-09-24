using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace
namespace Ninlil.Common.Serializer.OutputFormatter;

/// <summary>
///     This output formatter provides our JSON response capabilities.
/// </summary>
public class JsonOutputFormatter : TextOutputFormatter
{
    /// <summary>
    ///     This property contains the serializer options.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     This property denotes whether the output format should be pretty or not.
    /// </summary>
    private readonly bool _pretty;

    /// <summary>
    ///     This method instantiates our formatter with supported encodings and media types.
    /// </summary>
    public JsonOutputFormatter()
    {
        // Clear any supported encodings.
        SupportedEncodings.Clear();

        // Add our supported encodings.
        SupportedEncodings.Add(Encoding.UTF8);

        // Clear any supported media types.
        SupportedMediaTypes.Clear();

        // Add our supported media types.
        SupportedMediaTypes.Add(MediaTypeNames.Application.Json);
        SupportedMediaTypes.Add("application/jsonc");
        SupportedMediaTypes.Add("application/*+json");
        SupportedMediaTypes.Add("application/*+jsonc");
        SupportedMediaTypes.Add("text/json");
        SupportedMediaTypes.Add("text/jsonc");
        SupportedMediaTypes.Add("text/*+json");
        SupportedMediaTypes.Add("text/*+jsonc");
    }

    /// <summary>
    ///     This method instantiates our formatter with a <paramref name="pretty" />
    ///     flag and supported encodings and media types.
    /// </summary>
    /// <param name="pretty">Denotes whether the output should be pretty and indented or not.</param>
    public JsonOutputFormatter(bool pretty)
        : this() => _pretty = pretty;

    /// <summary>
    ///     This method instantiates our formatter with <paramref name="options" />.
    /// </summary>
    /// <param name="options">The options for serialization.</param>
    public JsonOutputFormatter(JsonSerializerOptions options)
        : this(options.WriteIndented) => _options = options;

    /// <summary>
    ///     This method writes the JSON to the response body.
    /// </summary>
    /// <param name="context">The serialization context.</param>
    /// <param name="selectedEncoding">The output encoding.</param>
    /// <returns>An awaitable task containing a void result.</returns>
    public override Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context,
        Encoding selectedEncoding
    ) =>
        context.HttpContext.Response.WriteAsync(
            JsonSerializer.Serialize(
                context.ObjectType,
                context.Object,
                JsonSerializer.GetOptions(_options ?? new(JsonSerializerDefaults.Web), _pretty)
            )
        );
}
