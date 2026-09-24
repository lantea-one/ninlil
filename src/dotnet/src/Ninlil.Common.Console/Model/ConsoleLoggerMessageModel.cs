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

// Define our namespace for the console logger message model.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class represents a log message model for the console logger. It contains properties
///     for the log level, message, data, prefix, timestamp, color, buffer, padding, and format.
/// </summary>
public class ConsoleLoggerMessageModel : IConsoleLoggerMessageModel
{
    /// <summary>
    ///     This HashSet contains the scalar types that can be serialized directly without additional formatting.
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
    ///     This property contains an <see cref="int" /> representing the number of buffer lines
    ///     to add before and after the log message when outputting to the console or file.
    /// </summary>
    [JsonPropertyName("buffer")]
    [XmlElement("buffer")]
    [YamlMember(Alias = "buffer")]
    public int Buffer { get; set; }

    /// <summary>
    ///     This property contains the optional <see cref="ConsoleColor" /> for the message when
    ///     outputting to the console. If not set, the color will be determined by the log level.
    /// </summary>
    [JsonPropertyName("color")]
    [XmlElement("color")]
    [YamlMember(Alias = "color")]
    public ConsoleColor? Color { get; set; }

    /// <summary>
    ///     This property contains any data object associated with the log message.
    ///     It can be of any type and will be serialized based on the specified format.
    /// </summary>
    [JsonPropertyName("data")]
    [XmlElement("data")]
    [YamlMember(Alias = "data")]
    public object Data { get; set; }

    /// <summary>
    ///     This property contains format specific to the message which will
    ///     override the global configuration format when serializing the message.
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
    ///     This property contains a <see cref="int" /> representing the
    ///     number of spaces to use for padding when formatting the log message.
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
    ///     This property contains the timestamp at which the log message was created.
    ///     It is set to the current date and time by default.
    /// </summary>
    [JsonPropertyName("timestamp")]
    [XmlElement("timestamp")]
    [YamlMember(Alias = "timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    ///     This property indicates whether the log message has already been written to the console and/or file.
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
    ///    This constructor initializes a new instance of the
    ///     <see cref="ConsoleLoggerMessageModel" /> class
    ///     with the specified log <paramref name="level" />,
    ///     <paramref name="message" />, <paramref name="data" />,
    ///     and <paramref name="prefix" />.
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
            // A null or genuinely empty prefix falls back to a fresh, empty model; any real,
            // populated prefix is kept as-is. The condition previously here was inverted (it
            // checked for "not (null or non-empty)", i.e. "null-or-non-empty" negated down to
            // "non-null and empty"), which meant a real, populated prefix always failed the check
            // and got silently replaced with a fresh empty one - so per-message prefixes were
            // discarded outright.
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
    ///     This method serializes the provided <paramref name="data" /> object into a string representation
    ///    based on the specified <paramref name="format" /> and <paramref name="indented" /> preference.
    /// </summary>
    /// <param name="data">
    ///     The object to be serialized. It can be of any type, including null, string, boolean, byte array,
    /// </param>
    /// <param name="format">
    ///     The desired serialization format, specified as a <see cref="ConsoleLoggerLogFormatEnum" />.
    /// </param>
    /// <param name="indented">
    ///     A boolean indicating whether the serialized output should be indented for better readability.
    /// </param>
    /// <returns>
    ///     A string representation of the serialized <paramref name="data" /> object based on the specified
    ///    <paramref name="format" /> and <paramref name="indented" /> preference. If the <paramref name="data" />
    ///     is <see langword="null" />, an empty string, or whitespace, it returns <see langword="null" />.
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

        // If the data object is an enumerable (e.g., list, array, etc.), serialize it to JSON and return the string.
        if (format is ConsoleLoggerLogFormatEnum.Json)
            return JsonSerializer.Serialize(data, JsonSerializer.GetOptions(null, indented));

        // If the data object is an enumerable (e.g., list, array, etc.), serialize it to XML and return the string.
        if (format is ConsoleLoggerLogFormatEnum.Xml)
            return Serializer.XmlSerializer.Serialize(
                data,
                Serializer.XmlSerializer.GetWriterOptions(null, Encoding.UTF8, indented)
            );

        // If the data object is an enumerable (e.g., list, array, etc.), serialize it to YAML and return the string.
        if (format is ConsoleLoggerLogFormatEnum.Yaml or ConsoleLoggerLogFormatEnum.Yml)
            return YamlSerializer.Serialize(data);

        // If we get here, return the string representation of the data.
        return data.ToString()?.Trim();
    }

    /// <summary>
    ///     This method converts the current log message instance into a dictionary
    ///     representation with keys for timestamp, level, prefix, message, and data.
    /// </summary>
    /// <param name="table">
    ///     A <see cref="bool" /> indicating whether to format the data as a table.
    ///     If <see langword="true" /> and the data is not <see langword="null" />,
    ///     it will be serialized as a table; otherwise, it will be included as-is.
    /// </param>
    /// <param name="logPrefixOverride">
    ///     An optional <see cref="IConsoleLoggerPrefixModel" /> to override the instance's prefix.
    /// </param>
    /// <param name="serializedDataOverride">
    ///     An optional <see cref="string" /> to override the serialized data representation.
    /// </param>
    /// <returns>
    ///     A <see cref="Dictionary{String, Object}" /> containing the log message properties:
    ///         - "timestamp": The timestamp of the log message in "yyyy-MM-dd HH:mm:ss.fff" format.
    ///         - "level": The log level of the message.
    ///         - "prefix": The string representation of the log prefix.
    ///         - "message": The log message string.
    ///         - "data": The serialized data representation, formatted as a table if specified.
    /// </returns>
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
    ///     This method converts the current log message instance into a string representation
    ///     based on the values in the provided logger <paramref name="configuration" />.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted and output.
    /// </param>
    /// <returns>
    ///     A string representation of the log message, formatted
    ///     according to the specified logger <paramref name="configuration" />.
    /// </returns>
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
    ///     This method converts the current log message instance into a table representation
    ///     using the ConsoleTables library. It formats the log message properties into a table with
    ///     columns for timestamp, level, prefix, message, and data.
    /// </summary>
    /// <param name="loggerPrefixOverride">
    ///     An optional <see cref="IConsoleLoggerPrefixModel" /> to override the instance's prefix.
    /// </param>
    /// <param name="serializedDataOverride">
    ///     An optional <see cref="string" /> to override the serialized data representation.
    /// </param>
    /// <returns>
    ///     A string representation of the log message formatted as a table,
    ///     with columns for timestamp, level, prefix, message, and data.
    /// </returns>
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
    ///     This method writes the log message to the console and/or a file based on the provided logger configuration.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted and output.
    /// </param>
    /// <returns>
    ///     An instance of <see cref="IConsoleLoggerMessageModel" /> representing
    ///     the current log message after writing it to the console and/or file.
    /// </returns>
    public IConsoleLoggerMessageModel Write(IConsoleLoggerConfigurationModel configuration) =>
        WriteAsync(configuration, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    ///     This asynchronous method writes the log message to the console
    ///     and/or a file based on the provided logger configuration.
    /// </summary>
    /// <param name="configuration">
    ///     The logger configuration model that specifies how the log message should be formatted and output.
    /// </param>
    /// <param name="stoppingToken">
    ///     An optional <see cref="CancellationToken" /> that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     An awaitable <see cref="Task{ConsoleLoggerMessageModel}" /> that resolves to the current
    ///     instance of <see cref="IConsoleLoggerMessageModel" /> after writing the log message.
    /// </returns>
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

        // Determine if the log level is "debug" or "trace" for conditional console output.
        bool isDebugOrTrace = ConsoleLogger.GetSanitizedPart(Level, true) is "debug" or "trace";

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

        // Reset the written flag to true to indicate that the log message has been written to the console and/or file.
        Written = true;

        // We're done, return the current instance of the log message.
        return this;
    }
}
