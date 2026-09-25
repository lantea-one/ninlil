using System.Net.Mime;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.InputFormatter;

/// <summary>
///     This MVC input formatter reads XML request bodies (UTF-8, for <c>application/xml</c>,
///     <c>text/xml</c> and their <c>+xml</c> suffixes) using <see cref="XmlSerializer" />.
///     <see cref="Extension.ServiceCollectionSerializerExtension" /> registers it.
/// </summary>
public class XmlInputFormatter : TextInputFormatter
{
    /// <summary>
    ///     This field contains optional XML reader settings.
    /// </summary>
    private readonly XmlReaderSettings _options;

    /// <summary>
    ///     This constructor sets the supported encoding and media types.
    /// </summary>
    public XmlInputFormatter()
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
    ///     This constructor uses the given reader <paramref name="options" />.
    /// </summary>
    /// <param name="options">Deserializer options.</param>
    public XmlInputFormatter(XmlReaderSettings options)
        : this() => _options = options;

    /// <summary>
    ///     This method reads and deserializes the request body. An empty body gives no value.
    /// </summary>
    /// <param name="context">The current serialization context.</param>
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
        using StreamReader xmlReader = new StreamReader(context.HttpContext.Request.Body);

        // Localize the XML body.
        string xml = await xmlReader.ReadToEndAsync();

        // Make sure we have XML content, return no value if not.
        if (xml.Trim() is null or "")
            return await InputFormatterResult.NoValueAsync();

        // We're done, deserialize the XML and return the result.
        return await InputFormatterResult.SuccessAsync(
            XmlSerializer.Deserialize(
                context.ModelType,
                xml,
                _options ?? XmlSerializer.GetReaderOptions()
            )
        );
    }
}
