using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.InputFormatter;
using Ninlil.Common.Serializer.OutputFormatter;
using YamlDotNet.Serialization;

// Define our namespace
namespace Ninlil.Common.Serializer.Extension;

/// <summary>
///     This class maintains our IServiceCollection extensions for the Serializer.
/// </summary>
public static class ServiceCollectionSerializerExtension
{
    /// <summary>
    ///     This method configures the API to use our MVC which includes the
    ///     built-in input/output formatters using the internal serializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="prettyOutput">Optional, if true the output will be indented.</param>
    /// <returns>The modified <paramref name="instance" /> of IServiceCollection.</returns>
    public static IServiceCollection UseSerializerMvc(
        this IServiceCollection instance,
        bool prettyOutput = false
    )
    {
        // Configure MVC for the application.
        instance.AddMvc(options =>
        {
            // Clear the input formatters.
            options.InputFormatters.Clear();

            // Clear the output formatters.
            options.OutputFormatters.Clear();

            // We need to respect the browser's accept header.
            options.RespectBrowserAcceptHeader = true;

            // Return 406 content errors.
            options.ReturnHttpNotAcceptable = true;

            // Add our JSON input formatter.
            options.InputFormatters.Add(new JsonInputFormatter());

            // Add our XML input formatter.
            options.InputFormatters.Add(new XmlInputFormatter());

            // Add our YAML input formatter.
            options.InputFormatters.Add(new YamlInputFormatter());

            // Add our JSON output formatter.
            options.OutputFormatters.Add(new JsonOutputFormatter(prettyOutput));

            // Add our XML output formatter.
            options.OutputFormatters.Add(new XmlOutputFormatter(prettyOutput));

            // Add our YAML output formatter.
            options.OutputFormatters.Add(new YamlOutputFormatter());
        });

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method configures the API to use our MVC which includes the
    ///     built-in input/output formatters using the internal serializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="prettyOutput">If true the output will be indented.</param>
    /// <param name="formats">The formats to support.</param>
    /// <returns>The modified <paramref name="instance" /> of IServiceCollection.</returns>
    public static IServiceCollection UseSerializerMvc(
        this IServiceCollection instance,
        bool prettyOutput,
        IEnumerable<SerializerFormatEnum> formats
    )
    {
        // Ensure we have formats to work with.
        formats = formats?.ToArray() ?? [];

        // Configure MVC for the application.
        instance.AddMvc(options =>
        {
            // Clear the input formatters.
            options.InputFormatters.Clear();

            // Clear the output formatters.
            options.OutputFormatters.Clear();

            // We need to respect the browser's accept header.
            options.RespectBrowserAcceptHeader = true;

            // Return 406 content errors.
            options.ReturnHttpNotAcceptable = true;

            // Check for JSON support then add our JSON formatters.
            if (
                formats.Contains(SerializerFormatEnum.Json)
                || formats.Contains(SerializerFormatEnum.Jsonc)
            )
            {
                // Add our input formatter.
                options.InputFormatters.Add(new JsonInputFormatter());

                // Add our output formatter.
                options.OutputFormatters.Add(new JsonOutputFormatter(prettyOutput));
            }

            // Check for XML support then add our XML formatters.
            if (formats.Contains(SerializerFormatEnum.Xml))
            {
                // Add our input formatter.
                options.InputFormatters.Add(new XmlInputFormatter());

                // Add our output formatter.
                options.OutputFormatters.Add(new XmlOutputFormatter(prettyOutput));
            }

            // Check for YAML support then add our YAML formatters.
            if (
                !formats.Contains(SerializerFormatEnum.Yaml)
                && !formats.Contains(SerializerFormatEnum.Yml)
            )
                return;

            // Add our YAML input formatter.
            options.InputFormatters.Add(new YamlInputFormatter());

            // Add our YAML output formatter.
            options.OutputFormatters.Add(new YamlOutputFormatter());
        });

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method configures the API to use our MVC which includes the
    ///     built-in input/output formatters using the internal serializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="prettyOutput">If true the output will be indented.</param>
    /// <param name="formats">The formats to support.</param>
    /// <returns>The modified <paramref name="instance" /> of IServiceCollection.</returns>
    public static IServiceCollection UseSerializerMvc(
        this IServiceCollection instance,
        bool prettyOutput,
        params SerializerFormatEnum[] formats
    ) => instance.UseSerializerMvc(prettyOutput, formats.AsEnumerable());

    /// <summary>
    ///     This method configures the API to use our MVC which includes the
    ///     built-in input/output formatters using the internal serializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="jsonOptions">Optional, settings for the JSON serializer.</param>
    /// <param name="xmlReaderOptions">Optional, settings for the XML deserializer.</param>
    /// <param name="xmlWriterOptions">Optional, settings for the XML serializer.</param>
    /// <param name="yamlDeserializer">Optional, settings for the YAML deserializer.</param>
    /// <param name="yamlSerializer">Optional, settings for the YAML serializer.</param>
    /// <returns>The current IServiceCollection <paramref name="instance" />.</returns>
    public static IServiceCollection UseSerializerMvc(
        this IServiceCollection instance,
        JsonSerializerOptions jsonOptions = null,
        XmlReaderSettings xmlReaderOptions = null,
        XmlWriterSettings xmlWriterOptions = null,
        IDeserializer yamlDeserializer = null,
        ISerializer yamlSerializer = null
    )
    {
        // Configure MVC for the application.
        instance.AddMvc(options =>
        {
            // Clear the input formatters.
            options.InputFormatters.Clear();

            // Clear the output formatters.
            options.OutputFormatters.Clear();

            // We need to respect the browser's accept header.
            options.RespectBrowserAcceptHeader = true;

            // Return 406 content errors.
            options.ReturnHttpNotAcceptable = true;

            // Add our JSON input formatter.
            options.InputFormatters.Add(
                new JsonInputFormatter(JsonSerializer.GetOptions(jsonOptions))
            );

            // Add our XML input formatter.
            options.InputFormatters.Add(
                new XmlInputFormatter(XmlSerializer.GetReaderOptions(xmlReaderOptions))
            );

            // Add our YAML input formatter.
            options.InputFormatters.Add(new YamlInputFormatter(yamlDeserializer));

            // Add our JSON output formatter.
            options.OutputFormatters.Add(
                new JsonOutputFormatter(JsonSerializer.GetOptions(jsonOptions))
            );

            // Add our XML output formatter.
            options.OutputFormatters.Add(
                new XmlOutputFormatter(XmlSerializer.GetWriterOptions(xmlWriterOptions))
            );

            // Add our YAML output formatter.
            options.OutputFormatters.Add(new YamlOutputFormatter(yamlSerializer));
        });

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method fluidly registers global serializer options for the JSON Serializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The current IServiceCollection <paramref name="instance" />.</returns>
    public static IServiceCollection UseSerializerJsonOptions(
        this IServiceCollection instance,
        JsonSerializerOptions options
    )
    {
        // Set the options into the library.
        JsonSerializer.SerializerOptions = options;

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method fluidly registers global serializer options for the XML Deserializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="options">The XML deserializer options.</param>
    /// <returns>The current IServiceCollection <paramref name="instance" />.</returns>
    public static IServiceCollection UseSerializerXmlDeserializerOptions(
        this IServiceCollection instance,
        XmlReaderSettings options
    )
    {
        // Set the options into the library.
        XmlSerializer.DeserializerOptions = options;

        // We're done, return the IServiceCollection instance.
        return instance;
    }

    /// <summary>
    ///     This method fluidly registers global serializer options for the XML Deserializer.
    /// </summary>
    /// <param name="instance">The current IServiceCollection instance.</param>
    /// <param name="options">The XML deserializer options.</param>
    /// <returns>The current IServiceCollection <paramref name="instance" />.</returns>
    public static IServiceCollection UseSerializerXmlSerializerOptions(
        this IServiceCollection instance,
        XmlWriterSettings options
    )
    {
        // Set the options into the library.
        XmlSerializer.SerializerOptions = options;

        // We're done, return the IServiceCollection instance.
        return instance;
    }
}
