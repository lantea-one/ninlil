using System.Net.Mime;
using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc.Formatters;

// Define our namespace.
namespace Ninlil.Common.Serializer.InputFormatter;

/// <summary>
///     This input formatter provides our XML capabilities.
/// </summary>
public class XmlInputFormatter : TextInputFormatter
{
    /// <summary>
    ///     This property contains our deserializer settings.
    /// </summary>
    private readonly XmlReaderSettings _options;

    /// <summary>
    ///     This method instantiates our formatter with supported encodings and media types.
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
    ///     This method instantiates our formatter with deserializer <paramref name="options" />.
    /// </summary>
    /// <param name="options">Deserializer options.</param>
    public XmlInputFormatter(XmlReaderSettings options)
        : this() => _options = options;

    /// <summary>
    ///     This method reads and deserializes the request body.
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
