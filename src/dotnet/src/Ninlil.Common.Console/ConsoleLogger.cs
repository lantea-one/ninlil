using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This class is the logger for console applications built on this library. Each
///     <c>Log*</c> call creates a <see cref="ConsoleLoggerMessageModel" />, writes it at once
///     according to <see cref="Configuration" /> (format, prefix, colors, quiet/debug switches and
///     optional log file) and keeps it in <see cref="Messages" />. The prefix column width grows to
///     fit the longest prefix seen so far so text output stays aligned.
///     <see cref="Extension.ServiceCollectionConsoleExtension" /> registers it for dependency
///     injection, and <see cref="ConsoleApplication" /> receives it through its constructor.
/// </summary>
[XmlInclude(typeof(ConsoleLoggerConfigurationModel))]
[XmlInclude(typeof(ConsoleLoggerMessageModel))]
public class ConsoleLogger : IConsoleLogger
{
    /// <summary>
    ///     This static factory method creates a new instance of the <see cref="ConsoleLogger" /> class.
    /// </summary>
    /// <returns>A new instance of the <see cref="ConsoleLogger" /> class.</returns>
    public static IConsoleLogger Create() => new ConsoleLogger();

    /// <summary>
    ///     This static factory method creates a new instance of the <see cref="ConsoleLogger" />
    ///     class with the specified <paramref name="configuration" /> settings.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the configuration settings for the console logger.
    /// </param>
    /// <returns>
    ///     A new instance of the <see cref="ConsoleLogger" /> class with the specified configuration.
    /// </returns>
    public static IConsoleLogger Create(IConsoleLoggerConfigurationModel configuration) =>
        new ConsoleLogger(configuration);

    /// <summary>
    ///     This static factory method creates a new instance of the <see cref="ConsoleLogger" />
    ///     class with the specified <paramref name="format" /> and optional <paramref name="prefix" /> settings.
    /// </summary>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that specifies the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for log messages.
    /// </param>
    /// <returns>
    ///     A new instance of the <see cref="ConsoleLogger" /> class with the specified format and optional prefix.
    /// </returns>
    public static IConsoleLogger Create(
        ConsoleLoggerLogFormatEnum format,
        IEnumerable<string> prefix = null
    ) => new ConsoleLogger(format, prefix);

    /// <summary>
    ///     This static method sanitizes a log message part by
    ///     trimming leading and trailing spaces, brackets, and hyphens.
    /// </summary>
    /// <param name="part">The log message part <see cref="string" /> to be sanitized.</param>
    /// <param name="replaceSpaces">
    ///     An optional <see cref="bool" /> parameter that indicates
    ///     whether to replace spaces in the part with hyphens or not.
    /// </param>
    /// <returns>
    ///     A sanitized version of the log message <paramref name="part" />, or <see langword="null" /> if the
    ///     <paramref name="part" /> is <see langword="null" /> or <see cref="string.Empty" /> after trimming.
    /// </returns>
    public static string GetSanitizedPart(string part, bool replaceSpaces = false)
    {
        // Trim the part of any leading or trailing spaces, brackets, or hyphens.
        part = part?.Trim(' ', '[', ']', '-');

        // If the part is null or empty after trimming, return null.
        if (part is null or { Length: 0 })
            return null;

        // If replaceSpaces is true, replace spaces in the part with hyphens.
        if (replaceSpaces)
            part = part.Replace(' ', '-');

        // We're done sanitizing the part, so return it.
        return part;
    }

    /// <summary>
    ///     This property holds the configuration settings for the
    ///     console logger, including format, prefix, and other options.
    /// </summary>
    [JsonPropertyName("configuration")]
    [XmlElement("configuration")]
    [YamlMember(Alias = "configuration")]
    public IConsoleLoggerConfigurationModel Configuration { get; set; } =
        new ConsoleLoggerConfigurationModel();

    /// <summary>
    ///     This property holds every message logged by this instance. It is never trimmed.
    /// </summary>
    [JsonPropertyName("messages")]
    [XmlElement("message")]
    [YamlMember(Alias = "messages")]
    public List<IConsoleLoggerMessageModel> Messages { get; set; } = [];

    /// <summary>
    ///     This is the default constructor for the <see cref="ConsoleLogger" /> class.
    ///     It initializes a new instance of the console logger with default configuration settings.
    /// </summary>
    public ConsoleLogger() { }

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleLogger" />
    ///     class with the specified <paramref name="configuration" /> settings.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the configuration settings for the console logger.
    /// </param>
    public ConsoleLogger(IConsoleLoggerConfigurationModel configuration) =>
        Configuration = configuration ?? new ConsoleLoggerConfigurationModel();

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleLogger" />
    ///     class with the specified <paramref name="format" /> and optional <paramref name="prefix" /> settings.
    /// </summary>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that specifies the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for log messages.
    /// </param>
    public ConsoleLogger(ConsoleLoggerLogFormatEnum format, IEnumerable<string> prefix = null)
    {
        // Reset the configuration to a new instance of ConsoleLoggerConfigurationModel with the specified format.
        Configuration = new ConsoleLoggerConfigurationModel { Format = format };

        // If a prefix is provided, set the prefix in the configuration using the provided parts.
        if (prefix != null)
            Configuration.Prefix.WithParts(prefix.ToArray());
    }

    /// <summary>
    ///     This method writes any unwritten messages and exits the process with
    ///     <paramref name="exitCode" />. A negative code does nothing.
    /// </summary>
    /// <param name="exitCode">
    ///     The exit code to use. The default of <see langword="-1" /> leaves the process running.
    /// </param>
    public void Exit(int exitCode = -1)
    {
        // A negative exit code means do not exit.
        if (exitCode <= -1)
            return;

        // Flush the messages to ensure all logged messages are written before exiting.
        Flush();

        // We're done, so exit the application with the specified exit code.
        Environment.Exit(exitCode);
    }

    /// <summary>
    ///     This method writes any logged messages that have not been written yet.
    /// </summary>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger Flush()
    {
        // Iterate through the log messages that have yet to be written
        // and write them to the console using the current configuration.
        foreach (var message in Messages.Where(m => !m.Written))
            message.Write(Configuration);

        // We're done flushing the messages, so return the current instance of the console logger for method chaining.
        return this;
    }

    /// <summary>
    ///     This method logs a <code>debug</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogDebug(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("debug", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>done</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogDone(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("done", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs an <code>error</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogError(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("error", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>fail</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogFatal(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("fail", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>heartbeat</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogHeartbeat(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("heartbeat", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs an <code>info</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogInformation(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("info", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method records the <paramref name="message" />, widens
    ///     <see cref="IConsoleLoggerConfigurationModel.MaxPrefixLength" /> to fit its prefix and writes it.
    /// </summary>
    /// <param name="message">
    ///     The <see cref="IConsoleLoggerMessageModel" /> instance that represents the log message to be logged.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogMessage(IConsoleLoggerMessageModel message)
    {
        // Add the provided log message to the list of messages in the logger.
        Messages.Add(message);

        // Find the longest prefix part across all messages, ignoring timestamp parts, plus two for
        // the surrounding brackets.
        int maxPrefixLength =
            Messages
                .Where(m => m.Prefix?.GetParts().Count > 0)
                .SelectMany(m => m.Prefix.GetParts())
                .Where(p => !DateTime.TryParse(p, out _))
                .Select(p => p.Length)
                .DefaultIfEmpty(0)
                .Max() + 2;

        // If the maximum prefix length exceeds the current maximum prefix length in the configuration, update it.
        if (maxPrefixLength > Configuration.MaxPrefixLength)
            Configuration.MaxPrefixLength = maxPrefixLength;

        // Write the log message to the console using the current configuration.
        message.Write(Configuration);

        // We're done, return the current instance of the console logger for method chaining.
        return this;
    }

    /// <summary>
    ///     This method logs a message to the console with the specified parameters, including log
    ///     <paramref name="level" />, <paramref name="message" /> content, optional <paramref name="prefix" />,
    ///     <paramref name="data" />, <paramref name="buffer" /> space, <paramref name="padding" />,
    ///     <paramref name="format" />, and <paramref name="color" />.
    /// </summary>
    /// <param name="level">
    ///     The log level as a string (e.g., "info", "error", "debug")
    ///     that indicates the severity or type of the log message.
    /// </param>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogMessage(
        string level,
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage(NewMessage(level, message, prefix, data, buffer, padding, format, color));

    /// <summary>
    ///     This method logs a <code>notice</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogNotice(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("notice", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>success</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogSuccess(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("done", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>trace</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogTrace(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("trace", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method logs a <code>warn</code> message to the console with the specified parameters.
    /// </summary>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogWarning(
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) => LogMessage("warn", message, prefix, data, buffer, padding, format, color);

    /// <summary>
    ///     This method creates a new instance of the <see cref="IConsoleLoggerMessageModel" />
    ///     interface with the specified parameters, including log <paramref name="level" />,
    ///     <paramref name="message" /> content, optional <paramref name="prefix" />, <paramref name="data" />,
    ///     <paramref name="buffer" /> space, <paramref name="padding" />, <paramref name="format" />,
    ///     and <paramref name="color" />.
    /// </summary>
    /// <param name="level">
    ///     The log level as a string (e.g., "info", "error", "debug")
    ///     that indicates the severity or type of the log message.
    /// </param>
    /// <param name="message">
    ///     The log message content as a string that describes the event or information to be logged.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for the log message.
    /// </param>
    /// <param name="data">
    ///     An optional object that contains additional data or context related to the log message.
    /// </param>
    /// <param name="buffer">
    ///     The number of blank lines to add before and after the message in text format.
    /// </param>
    /// <param name="padding">
    ///     The number of spaces to indent the message by in text format.
    /// </param>
    /// <param name="format">
    ///     The format used to serialize <paramref name="data" />.
    /// </param>
    /// <param name="color">
    ///     The console color for the message; when null the level decides.
    /// </param>
    /// <returns>
    ///     A new instance of the <see cref="IConsoleLoggerMessageModel" /> interface
    ///     that represents the log message with the specified parameters.
    /// </returns>
    public IConsoleLoggerMessageModel NewMessage(
        string level,
        string message,
        IEnumerable<string> prefix = null,
        object data = null,
        int buffer = 0,
        int padding = 4,
        ConsoleLoggerLogFormatEnum format = ConsoleLoggerLogFormatEnum.Yaml,
        ConsoleColor? color = null
    ) =>
        new ConsoleLoggerMessageModel(
            level,
            message,
            data,
            (ConsoleLoggerPrefixModel)prefix?.ToArray() ?? []
        )
        {
            Buffer = buffer,
            Color = color,
            Format = format,
            Padding = padding,
        };

    /// <summary>
    ///     This method renders every logged message according to the current configuration, one per
    ///     line.
    /// </summary>
    /// <returns>The rendered messages.</returns>
    public override string ToString() =>
        string.Join(Environment.NewLine, Messages.Select(m => m.ToString(Configuration)));

    /// <summary>
    ///     This method fluently resets the configuration of the console
    ///     logger to the provided <paramref name="configuration" /> model.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the new configuration settings for the console logger.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger WithConfiguration(IConsoleLoggerConfigurationModel configuration)
    {
        // Reset the configuration of the console logger to the provided configuration model.
        Configuration = configuration;

        // We're done, return the current instance of the console logger for method chaining.
        return this;
    }

    /// <summary>
    ///     This method synchronously writes any logged messages that have not been written yet.
    /// </summary>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger Write() => WriteAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>
    ///     This method writes any logged messages that have not been written yet. Each message is
    ///     written only once.
    /// </summary>
    /// <param name="stoppingToken">
    ///     An optional <see cref="CancellationToken" /> that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     An awaitable <see cref="Task{IConsoleLogger}" /> that resolves to the
    ///     current instance of the <see cref="IConsoleLogger" /> for method chaining.
    /// </returns>
    public async Task<IConsoleLogger> WriteAsync(CancellationToken stoppingToken = default)
    {
        // Write each message; messages already written are skipped by the message itself.
        foreach (var message in Messages)
            await message.WriteAsync(Configuration, stoppingToken);

        // We're done, return the current instance of the console logger for method chaining.
        return this;
    }
}

/// <summary>
///     This class is a <see cref="ConsoleLogger" /> whose prefix starts with the name of
///     <typeparamref name="TClass" />, so messages show which class logged them. It is the
///     <see cref="IConsoleLogger{TClass}" /> registered by the generic
///     <c>UseConsoleLogging&lt;TClassPrefix&gt;</c> overloads.
/// </summary>
/// <typeparam name="TClass">The class whose name prefixes every message.</typeparam>
public class ConsoleLogger<TClass> : ConsoleLogger, IConsoleLogger<TClass>
    where TClass : class
{
    /// <summary>
    ///     This static factory method creates a new <see cref="ConsoleLogger{TClass}" />.
    /// </summary>
    /// <returns>A new <see cref="ConsoleLogger{TClass}" />.</returns>
    public static new IConsoleLogger<TClass> Create() => new ConsoleLogger<TClass>();

    /// <summary>
    ///     This static factory method creates a new <see cref="ConsoleLogger{TClass}" /> with the
    ///     specified <paramref name="configuration" /> settings.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the configuration settings for the console logger.
    /// </param>
    /// <returns>A new <see cref="ConsoleLogger{TClass}" /> with the specified configuration.</returns>
    public static new IConsoleLogger<TClass> Create(
        IConsoleLoggerConfigurationModel configuration
    ) => new ConsoleLogger<TClass>(configuration);

    /// <summary>
    ///     This static factory method creates a new <see cref="ConsoleLogger{TClass}" /> with the
    ///     specified <paramref name="format" /> and optional <paramref name="prefix" /> settings.
    /// </summary>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that specifies the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for log messages.
    /// </param>
    /// <returns>A new <see cref="ConsoleLogger{TClass}" /> with the specified format and prefix.</returns>
    public static new IConsoleLogger<TClass> Create(
        ConsoleLoggerLogFormatEnum format,
        IEnumerable<string> prefix = null
    ) => new ConsoleLogger<TClass>(format, prefix);

    /// <summary>
    ///     This constructor creates a logger with YAML format and the class name as the prefix.
    /// </summary>
    public ConsoleLogger() =>
        Configuration = new ConsoleLoggerConfigurationModel
        {
            Format = ConsoleLoggerLogFormatEnum.Yaml,
            Prefix = new ConsoleLoggerPrefixModel([typeof(TClass).Name]),
        };

    /// <summary>
    ///     This constructor creates a logger with the specified <paramref name="configuration" />,
    ///     adding the class name to the front of its prefix when it is not there yet.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the configuration settings for the console logger.
    /// </param>
    public ConsoleLogger(IConsoleLoggerConfigurationModel configuration)
    {
        // Ensure we have a valid configuration model; if not, create a new instance with default settings.
        configuration ??= new ConsoleLoggerConfigurationModel();

        // Ensure we have a valid logger prefix; if not, create a new instance with default settings.
        configuration.Prefix ??= new ConsoleLoggerPrefixModel();

        // Check the incoming configuration's prefix, not the Configuration property. The property
        // is not assigned yet, so it would always look empty and the class name would be added
        // twice.
        if (
            !configuration
                .Prefix.GetParts()
                .Contains(typeof(TClass).Name, StringComparer.OrdinalIgnoreCase)
        )
            configuration.Prefix.Insert(0, typeof(TClass).Name);

        // Set the configuration into the instance.
        Configuration = configuration;
    }

    /// <summary>
    ///     This constructor creates a logger with the specified <paramref name="format" />, using
    ///     the class name followed by <paramref name="prefix" /> as the prefix.
    /// </summary>
    /// <param name="format">
    ///     The <see cref="ConsoleLoggerLogFormatEnum" /> value that specifies the log format for the console logger.
    /// </param>
    /// <param name="prefix">
    ///     An optional collection of strings that represent the prefix for log messages.
    /// </param>
    public ConsoleLogger(ConsoleLoggerLogFormatEnum format, IEnumerable<string> prefix = null)
    {
        // Start from the provided prefix parts, or an empty list.
        List<string> prefixParts = prefix?.ToList() ?? [];

        // If the class name is not already part of the prefix, add it to the prefix parts.
        if (!prefixParts.Contains(typeof(TClass).Name, StringComparer.OrdinalIgnoreCase))
            prefixParts.Insert(0, typeof(TClass).Name);

        // Reset the configuration to a new instance of ConsoleLoggerConfigurationModel with the specified format.
        Configuration = new ConsoleLoggerConfigurationModel
        {
            Format = format,
            Prefix = new ConsoleLoggerPrefixModel(prefixParts),
        };
    }
}
