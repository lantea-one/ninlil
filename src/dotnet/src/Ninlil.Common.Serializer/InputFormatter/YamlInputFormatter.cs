using System.Text;
using Microsoft.AspNetCore.Mvc.Formatters;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.InputFormatter;

/// <summary>
///     This input formatter provides our YAML capabilities.
/// </summary>
public class YamlInputFormatter : TextInputFormatter
{
    /// <summary>
    ///     This property contains our YAML deserializer override.
    /// </summary>
    private readonly IDeserializer _deserializer;

    /// <summary>
    ///     This method instantiates our formatter with supported encodings and media types.
    /// </summary>
    public YamlInputFormatter()
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
    ///     This method instantiates our formatter with a specific <paramref name="deserializer" />.
    /// </summary>
    /// <param name="deserializer">YAML deserializer override.</param>
    public YamlInputFormatter(IDeserializer deserializer)
        : this() => _deserializer = deserializer;

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
        using StreamReader reader = new StreamReader(context.HttpContext.Request.Body);

        // Localize the YAML body.
        string yaml = await reader.ReadToEndAsync();

        // Make sure we have YAML content, return no value if not.
        if (yaml is null or "")
            return await InputFormatterResult.NoValueAsync();

        // We're done, deserialize the JSON and return the result.
        return await InputFormatterResult.SuccessAsync(
            YamlSerializer.Deserialize(context.ModelType, yaml, _deserializer)
        );
    }
}
