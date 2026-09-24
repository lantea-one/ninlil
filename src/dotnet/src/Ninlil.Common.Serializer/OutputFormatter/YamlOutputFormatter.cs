using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;
using YamlDotNet.Serialization;

// Define our namespace
namespace Ninlil.Common.Serializer.OutputFormatter;

/// <summary>
///     This output formatter provides our YAML response capabilities.
/// </summary>
public class YamlOutputFormatter : TextOutputFormatter
{
    /// <summary>
    ///     This property contains our YAML serializer override.
    /// </summary>
    private readonly ISerializer _serializer;

    /// <summary>
    ///     This method instantiates our formatter with supported encodings and media types.
    /// </summary>
    public YamlOutputFormatter()
    {
        // Clear any supported encodings.
        SupportedEncodings.Clear();

        // Add our supported encodings.
        SupportedEncodings.Add(Encoding.UTF8);

        // Clear any supported media types.
        SupportedMediaTypes.Clear();

        // Add our supported media types.
        SupportedMediaTypes.Add("application/yaml");
        SupportedMediaTypes.Add("application/*+yaml");
        SupportedMediaTypes.Add("text/yaml");
        SupportedMediaTypes.Add("text/*+yaml");
    }

    /// <summary>
    ///     This method instantiates our formatter with a specific <paramref name="serializer" />.
    /// </summary>
    /// <param name="serializer">YAML serializer override.</param>
    public YamlOutputFormatter(ISerializer serializer)
        : this() => _serializer = serializer;

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
            YamlSerializer.Serialize(context.ObjectType, context.Object, _serializer)
        );
}
