using System.Xml.Serialization;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console;

/// <summary>
///     This interface is the contract for <see cref="ConsoleLogger" />: level-named log methods
///     that write each message at once and keep it in <see cref="Messages" />, plus the
///     configuration that controls format, prefix, colors and output targets.
/// </summary>
[XmlInclude(typeof(ConsoleLoggerConfigurationModel))]
[XmlInclude(typeof(ConsoleLoggerMessageModel))]
public interface IConsoleLogger
{
    /// <summary>
    ///     This property holds the configuration settings for the
    ///     console logger, including format, prefix, and other options.
    /// </summary>
    public IConsoleLoggerConfigurationModel Configuration { get; set; }

    /// <summary>
    ///     This property holds every message logged by this instance.
    /// </summary>
    public List<IConsoleLoggerMessageModel> Messages { get; set; }

    /// <summary>
    ///     This method writes any unwritten messages and exits the process with
    ///     <paramref name="exitCode" />. A negative code does nothing.
    /// </summary>
    /// <param name="exitCode">
    ///     The exit code to use. The default of <see langword="-1" /> leaves the process running.
    /// </param>
    public void Exit(int exitCode = -1);

    /// <summary>
    ///     This method writes any logged messages that have not been written yet.
    /// </summary>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger Flush();

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
    );

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
    );

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
    );

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
    );

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
    );

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
    );

    /// <summary>
    ///     This method logs a message to the console using the provided <paramref name="message" /> model.
    /// </summary>
    /// <param name="message">
    ///     The <see cref="IConsoleLoggerMessageModel" /> instance that represents the log message to be logged.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger LogMessage(IConsoleLoggerMessageModel message);

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
    );

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
    );

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
    );

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
    );

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
    );

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
    );

    /// <summary>
    ///     This method returns a string representation of the console logger,
    ///     including all logged messages formatted according to the current configuration.
    /// </summary>
    /// <returns>
    ///     A string that represents the console logger and its logged
    ///     messages, formatted according to the current configuration.
    /// </returns>
    public string ToString();

    /// <summary>
    ///     This method fluently resets the configuration of the console
    ///     logger to the provided <paramref name="configuration" /> model.
    /// </summary>
    /// <param name="configuration">
    ///     The <see cref="IConsoleLoggerConfigurationModel" /> instance
    ///     that contains the new configuration settings for the console logger.
    /// </param>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger WithConfiguration(IConsoleLoggerConfigurationModel configuration);

    /// <summary>
    ///     This method synchronously writes any logged messages that have not been written yet.
    /// </summary>
    /// <returns>The current instance of the <see cref="IConsoleLogger" /> for method chaining.</returns>
    public IConsoleLogger Write();

    /// <summary>
    ///     This method writes any logged messages that have not been written yet.
    /// </summary>
    /// <param name="stoppingToken">
    ///     An optional <see cref="CancellationToken" /> that can be used to cancel the asynchronous operation.
    /// </param>
    /// <returns>
    ///     An awaitable <see cref="Task{IConsoleLogger}" /> that resolves to the
    ///     current instance of the <see cref="IConsoleLogger" /> for method chaining.
    /// </returns>
    public Task<IConsoleLogger> WriteAsync(CancellationToken stoppingToken = default);
}

/// <summary>
///     This interface is a console logger whose prefix starts with the name of
///     <typeparamref name="TClass" />. Taking it as a dependency gives a class a logger labelled
///     with the class name.
/// </summary>
/// <typeparam name="TClass">The class whose name prefixes every message.</typeparam>
public interface IConsoleLogger<TClass> : IConsoleLogger
    where TClass : class;
