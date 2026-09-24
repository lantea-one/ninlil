using System.Net.Mime;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Serialization;
using MimeKit;
using Ninlil.Common.Serializer.Attribute;
using Ninlil.Common.Serializer.Converter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Model;

/// <summary>
///     This class maintains the structure for a Data URI.
/// </summary>
[JsonConverter<StructuredStringJsonConverter<DataUriModel>>]
[XmlRoot("dataUri")]
public class DataUriModel : StructuredStringModel
{
    /// <summary>
    ///     This operator provides implicit <see langword="string" /> conversion from a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="dataUri">The <see cref="DataUriModel" /> to convert.</param>
    /// <returns>A <see langword="string" /> representation of the <paramref name="dataUri" />.</returns>
    public static implicit operator string(DataUriModel dataUri) => dataUri?.ToString();

    /// <summary>
    ///     This operator provides implicit <see langword="byte[]" /> conversion from a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="dataUri">The <see cref="DataUriModel" /> to convert.</param>
    /// <returns>A <see langword="byte[]" /> representation of the <paramref name="dataUri" />.</returns>
    public static implicit operator byte[](DataUriModel dataUri) => dataUri?.Binary;

    /// <summary>
    ///     This operator provides implicit <see cref="Stream" /> conversion from a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="dataUri">The <see cref="DataUriModel" /> to convert.</param>
    /// <returns>A <see cref="Stream" /> representation of the <paramref name="dataUri" />.</returns>
    public static implicit operator Stream(DataUriModel dataUri) => dataUri?.ToStream();

    /// <summary>
    ///     This operator provides implicit <see langword="byte[]" /> conversion to a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="binary">The <see langword="byte[]" /> to convert.</param>
    /// <returns>The <see cref="DataUriModel" /> instance representing the <paramref name="binary" />.</returns>
    public static implicit operator DataUriModel(byte[] binary) => new(binary, null);

    /// <summary>
    ///     This operator provides implicit <see cref="Stream" /> conversion to a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="stream">The <see cref="Stream" /> to convert.</param>
    /// <returns>The <see cref="DataUriModel" /> instance representing the <paramref name="stream" />.</returns>
    public static implicit operator DataUriModel(Stream stream)
    {
        // Check for an empty stream.
        if (stream == null)
            return null;

        // Localize the stream reader into a disposable context.
        using StreamReader reader = new(stream);

        // We're done, instantiates a new data URI with the stream's content as the binary then return it.
        return stream is FileStream fileStream
            ? new(
                Encoding.UTF8.GetBytes(reader.ReadToEnd()),
                MimeTypes.GetMimeType(fileStream.Name),
                fileStream.Name,
                reader.CurrentEncoding.WebName
            )
            : new(
                Encoding.UTF8.GetBytes(reader.ReadToEnd()),
                null,
                null,
                reader.CurrentEncoding.WebName
            );
    }

    /// <summary>
    ///     This operator provides implicit <see cref="string" /> conversion to a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="dataUri">The data URI <see cref="string" /> to convert.</param>
    /// <returns>
    ///     The <see cref="DataUriModel" /> instance representing the <paramref name="dataUri" /> <see cref="string" />.
    /// </returns>
    public static implicit operator DataUriModel(string dataUri) =>
        TryParse(dataUri, out DataUriModel uri) ? uri : null;

    /// <summary>
    ///     This constant defines the pattern to match data URIs.
    /// </summary>
    public static Regex Pattern =>
        new(
            "^(data:(?<mime>(.+?)))((;charset=(?<charset>(.+?)))|(;filename=(?<filename>(.+?)))){0,2};base64,(?<binary>(.+?))$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline
        );

    /// <summary>
    ///     This method attempts to parse a <paramref name="value" /> into a <see cref="DataUriModel" /> instance.
    /// </summary>
    /// <param name="value">The data URI <see cref="string" /> to parse.</param>
    /// <param name="uri">
    ///     The output <see cref="DataUriModel" /> instance if the parse was successful, otherwise <see langword="null" />.
    /// </param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether the parse was successful or not. If <see langword="true" />,
    ///     the <paramref name="uri" /> output parameter will contain the parsed <see cref="DataUriModel" /> instance; if
    ///     <see langword="false" />, the <paramref name="uri" /> output parameter will be <see langword="null" />.
    /// </returns>
    public static bool TryParse(string value, out DataUriModel uri) =>
        TryParse<DataUriModel>(value, out uri);

    /// <summary>
    ///     This property contains the binary of the file.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public byte[] Binary { get; set; }

    /// <summary>
    ///     This property contains the filename if one was provided.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public string CharacterSet { get; set; }

    /// <summary>
    ///     This property contains the filename if one was provided.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public string Filename { get; set; }

    /// <summary>
    ///     This property contains the mime-type for the binary.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    public string MimeType { get; set; }

    /// <summary>
    ///     This method reads a file from the <paramref name="filePath" /> into a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="filePath">The file path to the file on the file system.</param>
    /// <returns>
    ///     The <see cref="DataUriModel" /> instance representing the file at <paramref name="filePath" />.
    /// </returns>
    /// <exception cref="System.Exception">When the file does not exist.</exception>
    public static DataUriModel FromFile(string filePath) =>
        FromFileAsync(filePath).GetAwaiter().GetResult();

    /// <summary>
    ///     This method asynchronously reads the <paramref name="filePath" /> into a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="filePath">The file path to the file on the file system.</param>
    /// <param name="stoppingToken">
    ///     The optional <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    ///     An awaitable <see cref="DataUriModel" /> instance representing the file at <paramref name="filePath" />.
    /// </returns>
    /// <exception cref="System.Exception">When the file does not exist.</exception>
    public static async Task<DataUriModel> FromFileAsync(
        string filePath,
        CancellationToken stoppingToken = default
    )
    {
        // Check the file for an absolute path and generate it.
        if (!filePath[0].Equals(Path.DirectorySeparatorChar))
            filePath = Path.Combine(
                Environment.GetEnvironmentVariable("ASPNETCORE_PHYSICAL_PATH") ?? string.Empty,
                filePath
            );

        // Load the file's information.
        FileInfo fileInfo = new FileInfo(filePath);

        // Make sure the file exists.
        if (!fileInfo.Exists)
            throw new($"{filePath} does not exist");

        // We're done, return the new data-uri.
        return new(
            await File.ReadAllBytesAsync(filePath, stoppingToken),
            MimeTypes.GetMimeType(fileInfo.Extension),
            fileInfo.Name
        );
    }

    /// <summary>
    ///     This method converts a <paramref name="fileStream" /> into a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="fileStream">The file stream to convert.</param>
    /// <param name="fileName">The file name to use for the <paramref name="fileStream" />.</param>
    /// <param name="contentType">The content type of the <paramref name="fileStream" />.</param>
    /// <returns>The <see cref="DataUriModel" /> instance representing the file.</returns>
    public static DataUriModel FromStream(Stream fileStream, string fileName, string contentType) =>
        FromStreamAsync(fileStream, fileName, contentType).GetAwaiter().GetResult();

    /// <summary>
    ///     This method asynchronously converts a <paramref name="fileStream" /> into a <see cref="DataUriModel" />.
    /// </summary>
    /// <param name="fileStream">The file stream to convert.</param>
    /// <param name="fileName">The file name to use for the <paramref name="fileStream" />.</param>
    /// <param name="contentType">The content type of the <paramref name="fileStream" />.</param>
    /// <param name="stoppingToken">
    ///     The optional <see cref="CancellationToken" /> to observe while waiting for the task to complete.
    /// </param>
    /// <returns>An awaitable <see cref="DataUriModel" /> instance representing the file.</returns>
    public static async Task<DataUriModel> FromStreamAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken stoppingToken = default
    )
    {
        // Localize the stream reader
        using StreamReader reader = new(fileStream);

        // We're done, return the new data URI
        return new(
            Encoding.UTF8.GetBytes(await reader.ReadToEndAsync(stoppingToken)),
            contentType,
            fileName,
            reader.CurrentEncoding.WebName
        );
    }

    /// <summary>
    ///     This method checks if a <paramref name="value" /> is a valid data-uri.
    /// </summary>
    /// <param name="value">The data-uri <see cref="string" /> to check.</param>
    /// <returns>A <see cref="bool" /> indicating if the <paramref name="value" /> is a valid data-uri.</returns>
    public static bool IsDataUri(string value) =>
        value?.Trim() is not (null or "") && TryParse<DataUriModel>(value, out _);

    /// <summary>
    ///     This method instantiates an empty data URI.
    /// </summary>
    public DataUriModel()
        : base(true, Pattern) { }

    /// <summary>
    ///     This method instantiates a populated Data URI.
    /// </summary>
    /// <param name="binary">The binary data to encode.</param>
    /// <param name="mimeType">The content-type of the <paramref name="binary" />.</param>
    /// <param name="filename">The filename to use.</param>
    /// <param name="characterSet">The character set encoding of the <paramref name="binary" />.</param>
    public DataUriModel(
        byte[] binary,
        string mimeType,
        string filename = null,
        string characterSet = null
    )
        : base(true, Pattern)
    {
        // Set the binary into the instance.
        Binary = binary;

        // Set the character set into the instance.
        CharacterSet = characterSet;

        // Set the filename into the instance.
        Filename = filename;

        // Set the mime-type into the instance.
        MimeType = mimeType;
    }

    /// <summary>
    ///     This method instantiates a data URI from a <see cref="string" />.
    /// </summary>
    /// <param name="dataUri">The data URI <see cref="string" /> to parse.</param>
    public DataUriModel(string dataUri)
        : base(true, dataUri, Pattern) { }

    /// <summary>
    ///     This abstract method is responsible for parsing the provided <paramref name="match" />
    ///     Regular Expression <see cref="Match" /> object into the properties of this model.
    ///     The <paramref name="match" /> object is expected to contain the captured groups that correspond
    ///     to the properties of this model, and the implementation of this method should extract those
    ///     values and assign them to the appropriate properties.
    /// </summary>
    /// <param name="original">
    ///     The original input string that was parsed to produce the <paramref name="match" />
    ///     object. This can be used for reference or for any additional parsing logic that may
    ///     require the original input string.
    /// </param>
    /// <param name="match">
    ///     The Regular Expression <see cref="Match" /> object that contains
    ///     the captured groups to be parsed into the properties of this model.
    /// </param>
    protected override DataUriModel ParseMatch(string original, Match match)
    {
        // Ensure we have a match -- a null/empty original, or a match that did not actually
        // succeed, both mean this string is not a well-formed data URI.
        if (original?.Trim() is null or { Length: 0 } || match?.Success is null or false)
            return null;

        // Localize the binary match.
        string binaryMatch = match.Groups["binary"].Value.Trim();

        // Localize the filename match.
        string filenameMatch = match.Groups["filename"].Value.Trim();

        // Localize the character set match.
        string characterSetMatch = match.Groups["charset"].Value.Trim();

        // Localize the mime-type match.
        string mimeTypeMatch = match.Groups["mime"].Value.Trim();

        // Ensure we have a binary match.
        if (binaryMatch is null or "")
            return null;

        // Set the binary into the instance.
        Binary = Convert.FromBase64String(binaryMatch);

        // Set the filename into the instance.
        Filename = filenameMatch is null or "" ? null : filenameMatch;

        // Set the character set into the instance.
        CharacterSet = characterSetMatch is null or "" ? null : characterSetMatch;

        // Set the mime-type into the instance.
        MimeType = mimeTypeMatch is null or "" ? null : mimeTypeMatch;

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method converts the data URI instance into a data-uri <see cref="string" />.
    /// </summary>
    /// <returns>The data-uri <see cref="string" /> representation of the instance.</returns>
    protected override string ToString(IDictionary<string, object> _)
    {
        // Define our data URI string.
        string dataUri =
            $"data:{(MimeType?.Trim() is null or ""
                ? Filename?.Trim() is null or ""
                    ? MediaTypeNames.Application.Octet
                    : MimeTypes.GetMimeType(Filename.Trim()).Trim() is null or ""
                        ? MediaTypeNames.Application.Octet
                        : MimeTypes.GetMimeType(Filename.Trim()).Trim()
                : MimeType.Trim())}";

        // Check for a character set.
        if (CharacterSet?.Trim() is not (null or ""))
            dataUri = $"{dataUri};charset={CharacterSet.Trim()}";

        // Check for a file name.
        if (Filename?.Trim() is not (null or ""))
            dataUri = $"{dataUri};filename={HttpUtility.UrlEncode(Filename)}";

        // Finalize and return the data URI.
        return $"{dataUri};base64,{ToBase64()}";
    }

    /// <summary>
    ///     This method checks whether <see langword="this" /> is valid by ensuring that <see cref="Binary" />
    ///     is not <see langword="null" /> and that its length is greater than <see langword="0" />.
    /// </summary>
    /// <returns>A <see langword="bool" /> indicating whether <see langword="this" /> is valid or not.</returns>
    public override bool IsValid() => Binary?.Length > 0;

    /// <summary>
    ///     This method returns the binary in a Base64 encoded <see cref="string" />.
    /// </summary>
    /// <returns>The Base64 encoded <see cref="string" /> representation of <see cref="Binary" />.</returns>
    public string ToBase64() => Convert.ToBase64String(Binary);

    /// <summary>
    ///     This method converts the data URI instance into a <see cref="Stream" />.
    /// </summary>
    /// <returns>A <see cref="MemoryStream" /> representation of the <see cref="Binary" /> data.</returns>
    public Stream ToStream() => Binary == null ? null : new MemoryStream(Binary);
}
