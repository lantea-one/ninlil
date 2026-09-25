using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.OutputFormatter;

/// <summary>
///     This MVC output formatter writes responses as JSON (UTF-8, for <c>application/json</c>,
///     <c>text/json</c>, the <c>jsonc</c> variants and their <c>+json</c> suffixes) using
///     <see cref="JsonSerializer" /> with web defaults unless options are given.
///     <see cref="Extension.ServiceCollectionSerializerExtension" /> registers it.
/// </summary>
public class JsonOutputFormatter : TextOutputFormatter
{
    /// <summary>
    ///     This field contains optional serializer options.
    /// </summary>
    private readonly JsonSerializerOptions _options;

    /// <summary>
    ///     This field denotes whether the output is indented.
    /// </summary>
    private readonly bool _pretty;

    /// <summary>
    ///     This constructor sets the supported encoding and media types.
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
    ///     This constructor sets whether the output is indented.
    /// </summary>
    /// <param name="pretty">Denotes whether the output should be pretty and indented or not.</param>
    public JsonOutputFormatter(bool pretty)
        : this() => _pretty = pretty;

    /// <summary>
    ///     This constructor uses the given <paramref name="options" />, including their indent
    ///     setting.
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
