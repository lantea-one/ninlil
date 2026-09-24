using System.Text;
using System.Xml;
using System.Xml.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer;

/// <summary>
///     This class provides XML serialization.
/// </summary>
public static class XmlSerializer
{
    /// <summary>
    ///     This property contains the registered namespaces for the serializer.
    /// </summary>
    private static XmlSerializerNamespaces _namespaces = new([new(string.Empty, String.Empty)]);

    /// <summary>
    ///     This property contains the library-default XML reader settings.
    /// </summary>
    public static XmlReaderSettings DeserializerOptions { get; set; } =
        new()
        {
            // Close the stream when we're done.
            CloseInput = true,

            // Define our conformance level.
            ConformanceLevel = ConformanceLevel.Auto,

            // Define our DTD processing.
            DtdProcessing = DtdProcessing.Ignore,

            // Ignore comments.
            IgnoreComments = true,

            // Ignore extraneous whitespace.
            IgnoreWhitespace = true,
        };

    /// <summary>
    ///     This property contains the library-default XML writer settings.
    /// </summary>
    public static XmlWriterSettings SerializerOptions { get; set; } =
        new()
        {
            // Close the stream when we're done.
            CloseOutput = true,

            // We want to use UTF-8 encoding.
            Encoding = Encoding.UTF8,

            // We want to format the XML output.
            Indent = false,

            // Define our indentation character.
            IndentChars = "    ",

            // Define our newline character.
            NewLineChars = Environment.NewLine,

            // We want a new line for attributes.
            NewLineOnAttributes = true,

            // We want to include the XML declaration.
            OmitXmlDeclaration = false,

            // Write closing tags when the stream closes.
            WriteEndDocumentOnClose = true,
        };

    /// <summary>
    ///     This method adds a custom namespace to the serializer.
    /// </summary>
    /// <param name="prefix">The prefix of the custom namespace.</param>
    /// <param name="uri">Optional namespace URI.</param>
    public static void AddNamespace(string prefix, string uri = null) =>
        _namespaces.Add(prefix, uri ?? $"http://{prefix}");

    /// <summary>
    ///     This method clears any custom namespaces from the serializer.
    /// </summary>
    public static void ClearNamespaces() => _namespaces = new([new(string.Empty, String.Empty)]);

    /// <summary>
    ///     This method deserializes an <paramref name="xml" /> string into a <paramref name="type"/> object.
    /// </summary>
    /// <param name="type">The type to deserialize <paramref name="xml" /> into.</param>
    /// <param name="xml">The XML string to deserialize.</param>
    /// <param name="options">Deserializer settings override.</param>
    /// <returns>The deserialized <paramref name="xml" /> <paramref name="type" /> object.</returns>
    public static object Deserialize(Type type, string xml, XmlReaderSettings options = null)
    {
        // Check for XML.
        if (xml?.Trim() is null or "")
            return null;

        // Define our name table.
        NameTable nameTable = new();

        // Define our namespace manager.
        XmlNamespaceManager xmlNamespaceManager = new XmlNamespaceManager(nameTable);

        // Add the custom namespaces to the manager.
        _namespaces
            .ToArray()
            .ToList()
            .ForEach(ns => xmlNamespaceManager.AddNamespace(ns.Name, ns.Namespace));

        // Define our parser context.
        XmlParserContext xmlContext = new XmlParserContext(
            null,
            xmlNamespaceManager,
            null,
            XmlSpace.None
        );

        // Define our XML writer settings.
        XmlReaderSettings xmlReaderSettings = GetReaderOptions(options);

        // Localize our scope and instantiate our string reader.
        using StringReader stringReader = new(xml);

        // Define our serializer.
        System.Xml.Serialization.XmlSerializer xmlSerializer = new(type);

        // Define our reader.
        using XmlReader reader = XmlReader.Create(stringReader, xmlReaderSettings, xmlContext);

        // We're done, deserialize the XML and return the object.
        return xmlSerializer.Deserialize(reader);
    }

    /// <summary>
    ///     This method deserializes an <paramref name="xml" /> string into a <typeparamref name="TResult"/> object.
    /// </summary>
    /// <param name="xml">The XML string to deserialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <typeparam name="TResult">The type to deserialize <paramref name="xml" /> into.</typeparam>
    /// <returns>The deserialized <paramref name="xml" /> <typeparamref name="TResult" /> object.</returns>
    public static TResult Deserialize<TResult>(string xml, XmlReaderSettings options = null) =>
        (TResult)Deserialize(typeof(TResult), xml, options);

    /// <summary>
    ///     This method returns the default XML reader options.
    /// </summary>
    /// <param name="fromOptions">Optional, existing XmlReaderSettings.</param>
    /// <returns>The library-default XmlReaderSettings.</returns>
    public static XmlReaderSettings GetReaderOptions(XmlReaderSettings fromOptions = null) =>
        fromOptions ?? DeserializerOptions;

    /// <summary>
    ///     This method returns the default XML writer options.
    /// </summary>
    /// <param name="fromOptions">Optional, existing settings to base.</param>
    /// <param name="encoding">Optional, character-set encoding for the XML string.</param>
    /// <param name="pretty">Optional, denotes whether to indent the XML or not.</param>
    /// <returns>The library-default XmlWriterSettings.</returns>
    public static XmlWriterSettings GetWriterOptions(
        XmlWriterSettings fromOptions = null,
        Encoding encoding = null,
        bool? pretty = null
    )
    {
        // Clone the base options (whichever were provided, or the shared static default) before
        // mutating anything below. SerializerOptions is a process-wide singleton shared by every
        // caller that doesn't supply its own settings -- writing into it directly here would let
        // one call's per-call Encoding/Indent override leak into every other caller's "default"
        // behavior, including other threads calling this concurrently (this project's real
        // deployment target is a multi-threaded ASP.NET Core server, where that race is not
        // theoretical). Cloning keeps this method's own mutations local to its own return value.
        XmlWriterSettings options = (fromOptions ?? SerializerOptions).Clone();

        // Reset the encoding into the options.
        options.Encoding = encoding ?? fromOptions?.Encoding ?? SerializerOptions.Encoding;

        // Reset the indented flag into the options.
        if (pretty.HasValue)
            options.Indent = pretty.Value;

        // We're done, return the options.
        return options;
    }

    /// <summary>
    ///     This method serializes the <paramref name="type" /> <paramref name="payload" /> object into an XML string.
    /// </summary>
    /// <param name="type">The type of the <paramref name="payload" /> to serialize.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <returns>The xml-serialized <paramref name="type" /> <paramref name="payload" /> object.</returns>
    public static string Serialize(Type type, object payload, XmlWriterSettings options = null)
    {
        // Ensure we have a payload.
        if (payload is null)
            return null;

        // Define our XML serializer.
        System.Xml.Serialization.XmlSerializer xmlSerializer = new(type);

        // Define our XML builder.
        StringBuilder xmlBuilder = new();

        // Define our XML writer settings.
        XmlWriterSettings xmlWriterSettings = GetWriterOptions(options);

        // Localize our scope and create our writer.
        using XmlWriter xmlWriter = XmlWriter.Create(xmlBuilder, xmlWriterSettings);

        // Serialize the XML.
        xmlSerializer.Serialize(xmlWriter, payload, _namespaces);

        // We're done with the writer.
        xmlWriter.Close();

        // We're done, return the XML payload.
        return xmlBuilder.ToString();
    }

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> object into an XML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <returns>The xml-serialized <paramref name="payload" /> object.</returns>
    public static string Serialize(object payload, XmlWriterSettings options = null) =>
        Serialize(payload?.GetType() ?? typeof(object), payload, options);

    /// <summary>
    ///     This method serializes the <typeparamref name="TSource" />
    ///     <paramref name="payload" /> object into an XML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <typeparam name="TSource">The type of the <paramref name="payload" /> to serialize.</typeparam>
    /// <returns>The xml-serialized <typeparamref name="TSource" /> <paramref name="payload" /> object.</returns>
    public static string Serialize<TSource>(TSource payload, XmlWriterSettings options = null) =>
        Serialize(typeof(TSource), payload, options);

    /// <summary>
    ///     This method serializes the <paramref name="payload" /> object into an XML string with whitespace.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <returns>The xml-serialized <paramref name="payload" /> object.</returns>
    public static string SerializePretty(object payload, XmlWriterSettings options = null) =>
        SerializePretty(payload?.GetType() ?? typeof(object), payload, options);

    /// <summary>
    ///     This method serializes the <paramref name="type" /> <paramref name="payload" /> object into an XML string.
    /// </summary>
    /// <param name="type">The type of the <paramref name="payload" /> to serialize.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <returns>The xml-serialized <paramref name="type" /> <paramref name="payload" /> object.</returns>
    public static string SerializePretty(
        Type type,
        object payload,
        XmlWriterSettings options = null
    ) => Serialize(type, payload, GetWriterOptions(options, pretty: true));

    /// <summary>
    ///     This method serializes the <typeparamref name="TSource" />
    ///     <paramref name="payload" /> object into an XML string with whitespace.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="options">Optional, serializer settings override.</param>
    /// <typeparam name="TSource">The type of the <paramref name="payload" /> to serialize.</typeparam>
    /// <returns>The xml-serialized <typeparamref name="TSource" /> <paramref name="payload" /> object.</returns>
    public static string SerializePretty<TSource>(
        TSource payload,
        XmlWriterSettings options = null
    ) => SerializePretty(typeof(TSource), payload, options);

    /// <summary>
    ///     This method tries to deserialize a <paramref name="xml" /> string into a <paramref name="type" /> object.
    /// </summary>
    /// <param name="type">The expected type of the deserialized object.</param>
    /// <param name="xml">The XML to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="options">Optional, XML Serializer Options override.</param>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize(
        Type type,
        string xml,
        out object result,
        XmlReaderSettings options = null
    )
    {
        // Try to deserialize the XML string.
        try
        {
            // Deserialize the XML string.
            result = Deserialize(type, xml, options);

            // We're done, return whether the result is null or not.
            return result != null;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the result to null.
            result = null;

            // We're done, we couldn't deserialize the XML string.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to deserialize a <paramref name="xml" />
    ///     string into a <typeparamref name="TResult" /> object.
    /// </summary>
    /// <param name="xml">The XML to deserialize.</param>
    /// <param name="result">The deserialized object.</param>
    /// <param name="options">Optional, XML Serializer Options override.</param>
    /// <typeparam name="TResult">The expected type of the deserialized object.</typeparam>
    /// <returns>A boolean denoting whether the deserialization was successful or not.</returns>
    public static bool TryDeserialize<TResult>(
        string xml,
        out TResult result,
        XmlReaderSettings options = null
    )
    {
        // Try to deserialize the JSON string.
        if (TryDeserialize(typeof(TResult), xml, out object resultObject, options))
        {
            // Reset the result to the deserialized object.
            result = (TResult)resultObject;

            // We're done, return true.
            return true;
        }

        // Reset the result to default.
        result = default;

        // We're done, return false.
        return false;
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into an XML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="xml">The serialized XML output string.</param>
    /// <param name="options">Optional, XML writer options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        object payload,
        out string xml,
        XmlWriterSettings options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            xml = pretty ? SerializePretty(payload, options) : Serialize(payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            xml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into an XML string.
    /// </summary>
    /// <param name="type">The type of the <paramref name="payload" /> to serialize.</param>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="xml">The serialized XML output string.</param>
    /// <param name="options">Optional, XML writer options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize(
        Type type,
        object payload,
        out string xml,
        XmlWriterSettings options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            xml = pretty
                ? SerializePretty(type, payload, options)
                : Serialize(type, payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            xml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }

    /// <summary>
    ///     This method tries to serialize the <paramref name="payload" /> string into an XML string.
    /// </summary>
    /// <param name="payload">The value to serialize.</param>
    /// <param name="xml">The serialized XML output string.</param>
    /// <param name="options">Optional, XML writer options override.</param>
    /// <param name="pretty">Denotes whether to indent the serialized output or not.</param>
    /// <typeparam name="TPayload">The type of <paramref name="payload" />.</typeparam>
    /// <returns>A boolean denoting whether the serialization was successful or not.</returns>
    public static bool TrySerialize<TPayload>(
        TPayload payload,
        out string xml,
        XmlWriterSettings options = null,
        bool pretty = false
    )
    {
        // Try to serialize the payload.
        try
        {
            // Serialize the payload into the output string.
            xml = pretty ? SerializePretty(payload, options) : Serialize(payload, options);

            // We're done, serialization was successful.
            return true;
        }
        // Catch any exceptions that occur.
        catch (Exception)
        {
            // Reset the output string to null.
            xml = null;

            // We're done, we couldn't serialize the payload.
            return false;
        }
    }
}
