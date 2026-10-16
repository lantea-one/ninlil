using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using ConsoleTables;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Serializer;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class is one entry written by <see cref="ConsoleLogger" />: a level, message, optional
///     data object and prefix, stamped with the local time. <see cref="ToString(IConsoleLoggerConfigurationModel)" />
///     renders it in the configured format (padded text, a table, or a JSON/XML/YAML document), and
///     <see cref="WriteAsync" /> writes it once to the console and, when configured, appends it to
///     the log file.
/// </summary>
public class ConsoleLoggerMessageModel : IConsoleLoggerMessageModel
{
    /// <summary>
    ///     This field lists the types whose data is written with <see cref="object.ToString" />
    ///     instead of being serialized.
    /// </summary>
    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(byte),
        typeof(char),
        typeof(decimal),
        typeof(double),
        typeof(Guid),
        typeof(int),
        typeof(long),
        typeof(IPAddress),
        typeof(PhysicalAddress),
        typeof(TimeSpan),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
        typeof(Uri),
    ];

    /// <summary>
    ///     This property contains the number of blank lines to add before and after the message in
    ///     text format.
    /// </summary>
    [JsonPropertyName("buffer")]
    [XmlElement("buffer")]
    [YamlMember(Alias = "buffer")]
    public int Buffer { get; set; }

    /// <summary>
    ///     This property contains the optional <see cref="ConsoleColor" /> for the message. If not
    ///     set, the configuration picks a color from the log level.
    /// </summary>
    [JsonPropertyName("color")]
    [XmlElement("color")]
    [YamlMember(Alias = "color")]
    public ConsoleColor? Color { get; set; }

    /// <summary>
    ///     This property contains any data object attached to the message. It is serialized in
    ///     <see cref="Format" />.
    /// </summary>
    [JsonPropertyName("data")]
    [XmlElement("data")]
    [YamlMember(Alias = "data")]
    public object Data { get; set; }

    /// <summary>
    ///     This property contains the format used to serialize <see cref="Data" />. The configuration's
    ///     format decides how the whole entry is laid out.
    /// </summary>
    [JsonPropertyName("format")]
    [XmlElement("format")]
    [YamlMember(Alias = "format")]
    public ConsoleLoggerLogFormatEnum Format { get; set; } = ConsoleLoggerLogFormatEnum.Yaml;

    /// <summary>
    ///     This property contains the log level of the message, such as "info", "warn", "error", etc.
    /// </summary>
    [JsonPropertyName("level")]
    [XmlElement("level")]
    [YamlMember(Alias = "level")]
    public string Level { get; set; } = "info";

    /// <summary>
    ///     This property contains the message string of the log entry.
    ///     It can be null or empty if only data is being logged.
    /// </summary>
    [JsonPropertyName("message")]
    [XmlElement("message")]
    [YamlMember(Alias = "message")]
    public string Message { get; set; }

    /// <summary>
    ///     This property contains the number of spaces used to indent the message (and twice that
    ///     for data) in text format.
    /// </summary>
    [JsonPropertyName("padding")]
    [XmlElement("padding")]
    [YamlMember(Alias = "padding")]
    public int Padding { get; set; } = 4;

    /// <summary>
    ///     This property contains the prefix for the log message, which can include additional context or identifiers.
    /// </summary>
    [JsonPropertyName("prefix")]
    [XmlElement("prefix")]
    [YamlMember(Alias = "prefix")]
    public IConsoleLoggerPrefixModel Prefix { get; set; } = new ConsoleLoggerPrefixModel();

    /// <summary>
    ///     This property contains the local time the message was created.
    /// </summary>
    [JsonPropertyName("timestamp")]
    [XmlElement("timestamp")]
    [YamlMember(Alias = "timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    ///     This property indicates whether the message has been written. <see cref="WriteAsync" />
    ///     writes a message only once.
    /// </summary>
    [JsonIgnore]
    [XmlIgnore]
    [YamlIgnore]
    public bool Written { get; set; }

    /// <summary>
    ///     This is the default constructor for the <see cref="ConsoleLoggerMessageModel" /> class.
    /// </summary>
    public ConsoleLoggerMessageModel() { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="ConsoleLoggerMessageModel" /> class
    ///     with the specified log <paramref name="level" />,
    ///     <paramref name="message" />, <paramref name="data" />,
    ///     and <paramref name="prefix" />. The level is sanitized and the message trimmed.
    /// </summary>
    /// <param name="level">
    ///     The log level of the message, such as "info", "warn", "error", etc.
    /// </param>
    /// <param name="message">
    ///     The message string of the log entry. It can be <see langword="null" />
    ///     or <see cref="string.Empty"/> if only data is being logged.
    /// </param>
    /// <param name="data">
    ///     The data object associated with the log message.
    ///     It can be of any type and will be serialized based on the specified format.
    /// </param>
    /// <param name="prefix">
    ///     The prefix for the log message, which can include additional context or identifiers.
    /// </param>
    public ConsoleLoggerMessageModel(
        string level,
        string message,
        object data,
        IConsoleLoggerPrefixModel prefix
    ) =>
        (Data, Level, Message, Prefix) = (
            data,
            ConsoleLogger.GetSanitizedPart(level, true),
            message?.Trim() is not (null or { Length: 0 }) ? message.Trim() : null,
            // A null or empty prefix becomes a new empty model; a populated prefix is kept.
            prefix is null or { Count: 0 }
                ? new ConsoleLoggerPrefixModel()
                : prefix
        );

    /// <summary>
    ///     This constructor initializes a new instance of
    ///     the <see cref="ConsoleLoggerMessageModel" /> class
    ///     with the specified log <paramref name="level" />,
    ///     <paramref name="message" />, and <paramref name="prefix" />.
    /// </summary>
    /// <param name="level">
    ///     The log level of the message, such as "info", "warn", "error", etc.
    /// </param>
    /// <param name="message">
    ///     The message string of the log entry. It can be <see langword="null" />
    ///     or <see cref="string.Empty"/> if only data is being logged.
    /// </param>
    /// <param name="prefix">
    ///     The prefix for the log message, which can include additional context
    ///     or identifiers. Represented as an <see cref="IEnumerable{String}" />.
    /// </param>
    public ConsoleLoggerMessageModel(string level, string message, IEnumerable<string> prefix)
        : this(level, message, null, new ConsoleLoggerPrefixModel(prefix?.ToArray() ?? [])) { }

    /// <summary>
    ///     This constructor initializes a new instance of the
    ///     <see cref="ConsoleLoggerMessageModel" /> class
    ///     with the specified log <paramref name="level" />,
    ///     <paramref name="data" />, and <paramref name="prefix" />.
    /// </summary>
    /// <param name="level">
    ///     The log level of the message, such as "info", "warn", "error", etc.
    /// </param>
    /// <param name="data">The data object representing the log message.</param>
    /// <param name="prefix">
    ///     The prefix for the log message, which can include additional context
    ///     or identifiers. Represented as an <see cref="IEnumerable{String}" />.
    /// </param>
    public ConsoleLoggerMessageModel(string level, object data, IEnumerable<string> prefix)
        : this(level, null, data, new ConsoleLoggerPrefixModel(prefix?.ToArray() ?? [])) { }

    /// <summary>
    ///     This method turns <paramref name="data" /> into text. Strings, booleans, byte arrays
    ///     (as Base64), dates (ISO 8601), exceptions and the types in <see cref="ScalarTypes" /> are
    ///     written directly. Other objects are serialized in <paramref name="format" />, or written
    ///     with <see cref="object.ToString" /> when the format is not JSON, XML or YAML.
    /// </summary>
    /// <param name="data">The object to turn into text.</param>
    /// <param name="format">The serialization format for complex objects.</param>
    /// <param name="indented">Whether JSON and XML output is indented.</param>
    /// <returns>
    ///     The text form of <paramref name="data" />, or <see langword="null" /> when it is
    ///     <see langword="null" /> or a blank string.
    /// </returns>
    private string Serialize(
        object data,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Table,
        bool indented = false
    )
    {
        // If the data object is null, return null.
        if (data == null)
            return null;

        // If the data object is a string, return the trimmed string
        // if it's not null or whitespace; otherwise, return null.
        if (data is string strData)
            return strData.Trim() is not { Length: 0 } ? strData.Trim() : null;

        // If the data object is a boolean, return its string representation in lowercase.
        if (data is bool b)
            return b.ToString().ToLower().Trim();

        // If the data object is a byte array, convert it to a Base64 string and return it.
        if (data is byte[] bytes)
            return Convert.ToBase64String(bytes);

        // If the data object is a signed byte array, convert it to a Base64 string and return it.
        if (data is sbyte[] sbytes)
            return Convert.ToBase64String((byte[])(Array)sbytes);

        // If the data object is a DateTime, return its string representation in ISO 8601 format.
        if (data is DateTime dt)
            return dt.ToString("o").Trim();

        // If the data object is a DateTimeOffset, return its string representation in ISO 8601 format.
        if (data is DateTimeOffset dto)
            return dto.ToString("o").Trim();

        // If the data object is an Exception, return its string representation.
        if (data is Exception ex)
            return ex.ToString().Trim();

        // If the data object is a scalar type (e.g., int, long, double, etc.), return its string representation.
        if (ScalarTypes.Contains(data.GetType()))
            return data.ToString()?.Trim();

        // Serialize any other object as JSON.
        if (format is ConsoleLoggerLogFormatEnum.Json)
            return JsonSerializer.Serialize(data, JsonSerializer.GetOptions(null, indented));

        // Serialize any other object as XML.
        if (format is ConsoleLoggerLogFormatEnum.Xml)
            return Serializer.XmlSerializer.Serialize(
                data,
                Serializer.XmlSerializer.GetWriterOptions(null, Encoding.UTF8, indented)
            );

        // Serialize any other object as YAML.
        if (format is ConsoleLoggerLogFormatEnum.Yaml or ConsoleLoggerLogFormatEnum.Yml)
            return YamlSerializer.Serialize(data);

        // For any other format, fall back to ToString().
        return data.ToString()?.Trim();
    }

    /// <summary>
    ///     This method converts the message into a dictionary with timestamp, level, prefix, message
    ///     and data entries, for table and serialized output.
    /// </summary>
    /// <param name="table">
    ///     Whether the dictionary is for a table. A table gets the data as text; otherwise the raw
    ///     data object is kept for the serializer.
    /// </param>
    /// <param name="logPrefixOverride">
    ///     An optional <see cref="IConsoleLoggerPrefixModel" /> to use instead of the message's prefix.
    /// </param>
    /// <param name="serializedDataOverride">
    ///     Optional data text to use instead of serializing <see cref="Data" />.
    /// </param>
    /// <returns>The message as a dictionary.</returns>
    private Dictionary<string, object> ToDictionary(
        bool table = false,
        IConsoleLoggerPrefixModel logPrefixOverride = null,
        string serializedDataOverride = null
    ) =>
        new()
        {
            { "timestamp", Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff") },
            { "level", Level },
            { "prefix", (logPrefixOverride ?? Prefix).ToString() },
            { "message", Message },
            {
                "data",
                table ? serializedDataOverride?.Trim() ?? Serialize(Data, Format, true) : Data
            },
        };

    /// <summary>
    ///     This method renders the message in the configuration's format. The prefix shown is the
    ///     timestamp, the level, the configuration's prefix and then the message's prefix. Text
    ///     format writes the prefix line, then the message and data indented on the following
    ///     lines; table format writes a one-row table; JSON, XML and YAML serialize the whole entry.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted.
    /// </param>
    /// <returns>The rendered message.</returns>
    public string ToString(IConsoleLoggerConfigurationModel configuration)
    {
        // Sanitize the log level in the instance to ensure it is valid and safe for output.
        Level = ConsoleLogger.GetSanitizedPart(Level, true);

        // Trim the message string in the instance to remove any leading or trailing whitespace.
        Message = Message?.Trim() is not (null or { Length: 0 }) ? Message.Trim() : null;

        // Create a new ConsoleLoggerPrefixModel to build the unified log prefix for the message.
        var logPrefix = new ConsoleLoggerPrefixModel();

        // Add the timestamp to the unified log prefix in the format "yyyy-MM-dd HH:mm:ss".
        logPrefix.WithPart(Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

        // Add the log level to the unified log prefix.
        logPrefix.WithPart(Level);

        // Merge the prefix from the logger configuration into the unified log prefix.
        logPrefix.Merge(configuration.Prefix);

        // Merge the prefix from the log message instance into the unified log prefix.
        logPrefix.Merge(Prefix);

        // Localize the serialized data object based on the specified format and indentation preference.
        string serializedData = Serialize(Data, Format, true);

        // If the logger configuration specifies a table format, convert the log message to a table representation.
        if (configuration.Format is ConsoleLoggerLogFormatEnum.Table)
            return ToTable(logPrefix, serializedData);

        // If the logger configuration specifies a text format,
        // format the log message as plain text with padding and newlines.
        if (configuration.Format is ConsoleLoggerLogFormatEnum.Text)
        {
            // Localize our regular expression for matching newlines in the log message and serialized data.
            var newlineRegex = new Regex(
                @"(\r?\n)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase
            );

            // Localize the log message string and trim any leading or trailing whitespace.
            string logMessage = (Message ?? string.Empty).Trim();

            // Localize the padding string based on the specified padding value in the logger configuration.
            string logPadding = new string(' ', Padding);

            // Localize the log prefix string based on the unified log prefix and
            // the specified maximum prefix length in the logger configuration as
            // well as whether to center-align the prefix string or not.
            string logPrefixString = configuration.CenterAlign
                ? logPrefix.ToString(configuration.MaxPrefixLength)
                : logPrefix.ToString(0);

            // Trim any whitespace from the log prefix string and append a colon to it.
            logPrefixString = logPrefixString.Trim() + ':';

            // If the log message contains newlines, split it into lines,
            // trim each line, and prepend the padding to each line.
            if (newlineRegex.IsMatch(logMessage))
                logMessage = string.Format(
                    "{0}{1}{2}",
                    logPrefixString,
                    Environment.NewLine,
                    string.Join(
                            Environment.NewLine,
                            newlineRegex.Split(logMessage).Select(l => $"{logPadding}{l.TrimEnd()}")
                        )
                        .Trim()
                );
            // Otherwise, prepend the padding to the log message and trim it.
            else
                logMessage =
                    $"{logPrefixString}{Environment.NewLine}{logPadding}{logMessage.TrimEnd()}".Trim();

            // If we have serialized data, format it with double padding and append it to the log message.
            if (serializedData?.Trim() is not (null or { Length: 0 }))
            {
                // Localize the double padding string based on the specified padding value in the logger configuration.
                string doublePadding = new string(' ', Padding * 2);

                // If the serialized data contains newlines, split it into lines,
                // trim each line, and prepend the double padding to each line.
                if (newlineRegex.IsMatch(serializedData))
                    serializedData = string.Join(
                            Environment.NewLine,
                            newlineRegex
                                .Split(serializedData)
                                .Select(l => $"{doublePadding}{l.TrimEnd()}")
                        )
                        .Trim();
                // Otherwise, prepend the double padding to the serialized data and trim it.
                else
                    serializedData = $"{doublePadding}{serializedData}".TrimEnd();

                // Append the serialized data to the log message with a newline in between.
                logMessage = string.Join(Environment.NewLine, logMessage, serializedData);
            }

            // If we have a buffer value greater than 0, prepend and
            // append the specified number of newlines to the log message.
            if (Buffer > 0)
                logMessage = string.Format(
                    "{0}{1}{0}",
                    string.Concat(Enumerable.Repeat(Environment.NewLine, Buffer)),
                    logMessage
                );

            // Ensure the log message ends with a newline for proper formatting in the console or file.
            if (!logMessage.EndsWith(Environment.NewLine))
                logMessage += Environment.NewLine;

            // We're done, return the formatted log message string.
            return logMessage;
        }

        // Otherwise, we'll serialize the log message to the specified format (JSON, XML, YAML, etc.) and return it.
        return Serialize(ToDictionary(false, logPrefix), configuration.Format);
    }

    /// <summary>
    ///     This method converts the current log message instance into a string representation
    ///     using the default <see cref="ConsoleLoggerConfigurationModel" />.
    /// </summary>
    /// <returns>
    ///     A string representation of the log message, formatted according to
    ///     the default <see cref="ConsoleLoggerConfigurationModel" /> settings.
    /// </returns>
    public override string ToString() => ToString(new ConsoleLoggerConfigurationModel());

    /// <summary>
    ///     This method renders the message as a one-row table (ConsoleTables library) with
    ///     timestamp, level, prefix, message and data columns.
    /// </summary>
    /// <param name="loggerPrefixOverride">
    ///     An optional <see cref="IConsoleLoggerPrefixModel" /> to use instead of the message's prefix.
    /// </param>
    /// <param name="serializedDataOverride">
    ///     Optional data text to use instead of serializing <see cref="Data" />.
    /// </param>
    /// <returns>The message as a table.</returns>
    public string ToTable(
        IConsoleLoggerPrefixModel loggerPrefixOverride = null,
        string serializedDataOverride = null
    )
    {
        // Convert the instance into a dictionary representation with table formatting.
        var dictionary = ToDictionary(true, loggerPrefixOverride, serializedDataOverride);

        // Instantiate our ConsoleTable with the dictionary keys as column headers.
        var table = new ConsoleTable([.. dictionary.Keys]);

        // Add the dictionary values as a single row in the table.
        table.AddRow([.. dictionary.Values]);

        // We're done, convert the table to its string representation and return it.
        return table.ToString();
    }

    /// <summary>
    ///     This method writes the message synchronously; see <see cref="WriteAsync" />.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted and output.
    /// </param>
    /// <returns>The current message.</returns>
    public IConsoleLoggerMessageModel Write(IConsoleLoggerConfigurationModel configuration) =>
        WriteAsync(configuration, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    ///     This method writes the message once. It appends the rendered text to the configured log
    ///     file, if any, and writes it to the console unless the configuration is quiet. Debug and
    ///     trace messages (the level is compared ignoring case) only reach the console when the
    ///     configuration enables debug output.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted and output.
    /// </param>
    /// <param name="stoppingToken">
    ///     An optional <see cref="CancellationToken" /> that can be used to cancel the file write.
    /// </param>
    /// <returns>The current message.</returns>
    public async Task<IConsoleLoggerMessageModel> WriteAsync(
        IConsoleLoggerConfigurationModel configuration,
        CancellationToken stoppingToken = default
    )
    {
        // If the message has already been written, return the current instance without writing again.
        if (Written)
            return this;

        // Convert the log message to a string representation based on the provided configuration.
        string logMessageString = ToString(configuration);

        // If a file path is specified in the configuration, append the log message to the file asynchronously.
        if (configuration.File?.Trim() is not (null or { Length: 0 }))
            await File.AppendAllTextAsync(
                configuration.File,
                logMessageString,
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? Encoding.UTF8
                    : new UTF8Encoding(false),
                stoppingToken
            );

        // Determine if the log level is "debug" or "trace" for conditional console output. The
        // comparison ignores case because the level colour table does too, so DEBUG is hidden like debug.
        string sanitizedLevel = ConsoleLogger.GetSanitizedPart(Level, true);
        bool isDebugOrTrace =
            string.Equals(sanitizedLevel, "debug", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sanitizedLevel, "trace", StringComparison.OrdinalIgnoreCase);

        // If the configuration is not set to quiet mode and either the log level is not "debug" or "trace", or
        // the configuration allows debug output, write the log message to the console with the appropriate color.
        if (!configuration.Quiet && (!isDebugOrTrace || configuration.Debug))
        {
            // Localize the original console foreground color to restore it after writing the log message.
            ConsoleColor originalColor = System.Console.ForegroundColor;

            // Set the console foreground color to the specified color in the log
            // message or the color determined by the log level in the configuration.
            System.Console.ForegroundColor = Color ?? configuration.GetConsoleColor(Level);

            // Write the log message to the console.
            System.Console.WriteLine(logMessageString);

            // Reset the console foreground color to its original value after writing the log message.
            System.Console.ForegroundColor = originalColor;
        }

        // Mark the message as written so it is not written again.
        Written = true;

        // We're done, return the current instance of the log message.
        return this;
    }
}
