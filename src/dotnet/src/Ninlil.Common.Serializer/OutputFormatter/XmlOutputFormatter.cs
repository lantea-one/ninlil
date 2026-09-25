using System.Net.Mime;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.OutputFormatter;

/// <summary>
///     This MVC output formatter writes responses as XML (UTF-8, for <c>application/xml</c>,
///     <c>text/xml</c> and their <c>+xml</c> suffixes) using <see cref="XmlSerializer" />, optionally
///     indented. <see cref="Extension.ServiceCollectionSerializerExtension" /> registers it.
/// </summary>
public class XmlOutputFormatter : TextOutputFormatter
{
    /// <summary>
    ///     This field denotes whether the output is indented; <see langword="null" /> keeps the
    ///     writer settings' choice.
    /// </summary>
    private readonly bool? _pretty;

    /// <summary>
    ///     This field contains optional XML writer settings.
    /// </summary>
    private readonly XmlWriterSettings _options;

    /// <summary>
    ///     This constructor sets the supported encoding and media types.
    /// </summary>
    public XmlOutputFormatter()
    {
        // Clear any supported encodings.
        SupportedEncodings.Clear();

        // Add our supported encodings.
        SupportedEncodings.Add(Encoding.UTF8);

        // Clear any supported media types.
        SupportedMediaTypes.Clear();

        // Add our supported media types.
        SupportedMediaTypes.Add(MediaTypeNames.Application.Xml);
        SupportedMediaTypes.Add("application/*+xml");
        SupportedMediaTypes.Add(MediaTypeNames.Text.Xml);
        SupportedMediaTypes.Add("text/*+xml");
    }

    /// <summary>
    ///     This constructor sets whether the output is indented.
    /// </summary>
    /// <param name="pretty">Denotes whether the output should be pretty and indented or not.</param>
    public XmlOutputFormatter(bool? pretty = null)
        : this() => _pretty = pretty;

    /// <summary>
    ///     This constructor uses the given writer <paramref name="options" />, including their indent
    ///     setting.
    /// </summary>
    /// <param name="options">The serializer settings.</param>
    public XmlOutputFormatter(XmlWriterSettings options)
        : this(options.Indent) => _options = options;

    /// <summary>
    ///     This method writes the XML to the response body.
    /// </summary>
    /// <param name="context">The serialization context.</param>
    /// <param name="selectedEncoding">The output encoding.</param>
    /// <returns>An awaitable task with void result.</returns>
    public override Task WriteResponseBodyAsync(
        OutputFormatterWriteContext context,
        Encoding selectedEncoding
    ) =>
        context.HttpContext.Response.WriteAsync(
            XmlSerializer.Serialize(
                context.ObjectType,
                context.Object,
                XmlSerializer.GetWriterOptions(_options, selectedEncoding, _pretty)
            ),
            selectedEncoding
        );
}
