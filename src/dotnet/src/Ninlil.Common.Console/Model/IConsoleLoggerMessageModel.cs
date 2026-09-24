using Ninlil.Common.Console.Enum;

// Define our namespace for the console logger message model.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This interface represents a log message model for the console logger. It contains properties
///     for the log level, message, data, prefix, timestamp, color, buffer, padding, and format.
/// </summary>
public interface IConsoleLoggerMessageModel
{
    /// <summary>
    ///     This property contains an <see cref="int" /> representing the number of buffer lines
    ///     to add before and after the log message when outputting to the console or file.
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
    ///     This property contains format specific to the message which will
    ///     override the global configuration format when serializing the message.
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
    ///     This property contains a <see cref="int" /> representing the
    ///     number of spaces to use for padding when formatting the log message.
    /// </summary>
    public int Padding { get; set; }

    /// <summary>
    ///     This property contains the prefix for the log message, which can include additional context or identifiers.
    /// </summary>
    public IConsoleLoggerPrefixModel Prefix { get; set; }

    /// <summary>
    ///     This property contains the timestamp at which the log message was created.
    ///     It is set to the current date and time by default.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    ///     This property indicates whether the log message has already been written to the console and/or file.
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
