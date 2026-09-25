using Ninlil.Common.Console.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This interface is the contract for one log entry written by <see cref="ConsoleLogger" />.
///     <see cref="ConsoleLoggerMessageModel" /> implements it.
/// </summary>
public interface IConsoleLoggerMessageModel
{
    /// <summary>
    ///     This property contains the number of blank lines to add before and after the message in
    ///     text format.
    /// </summary>
    public int Buffer { get; set; }

    /// <summary>
    ///     This property contains the optional <see cref="ConsoleColor" /> for the message when
    ///     outputting to the console. If not set, the color will be determined by the log level.
    /// </summary>
    public ConsoleColor? Color { get; set; }

    /// <summary>
    ///     This property contains any data object associated with the log message.
    ///     It can be of any type and will be serialized based on the specified format.
    /// </summary>
    public object Data { get; set; }

    /// <summary>
    ///     This property contains the format used to serialize <see cref="Data" />. The configuration's
    ///     format decides how the whole entry is laid out.
    /// </summary>
    public ConsoleLoggerLogFormatEnum Format { get; set; }

    /// <summary>
    ///     This property contains the log level of the message, such as "info", "warn", "error", etc.
    /// </summary>
    public string Level { get; set; }

    /// <summary>
    ///     This property contains the message string of the log entry.
    ///     It can be null or empty if only data is being logged.
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    ///     This property contains the number of spaces used to indent the message (and twice that
    ///     for data) in text format.
    /// </summary>
    public int Padding { get; set; }

    /// <summary>
    ///     This property contains the prefix for the log message, which can include additional context or identifiers.
    /// </summary>
    public IConsoleLoggerPrefixModel Prefix { get; set; }

    /// <summary>
    ///     This property contains the local time the message was created.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    ///     This property indicates whether the message has been written. A message is written only once.
    /// </summary>
    public bool Written { get; set; }

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
    public string ToString(IConsoleLoggerConfigurationModel configuration);

    /// <summary>
    ///     This method converts the current log message instance into a string representation
    ///     using the default <see cref="IConsoleLoggerConfigurationModel" />.
    /// </summary>
    /// <returns>
    ///     A string representation of the log message, formatted according to
    ///     the default <see cref="IConsoleLoggerConfigurationModel" /> settings.
    /// </returns>
    public string ToString();

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
    );

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
    public IConsoleLoggerMessageModel Write(IConsoleLoggerConfigurationModel configuration);

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
    public Task<IConsoleLoggerMessageModel> WriteAsync(
        IConsoleLoggerConfigurationModel configuration,
        CancellationToken stoppingToken = default
    );
}
