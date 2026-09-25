using Ninlil.Common.Console.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This interface is the contract for a <see cref="ConsoleLogger" /> configuration: layout
///     format, prefix, level colors, and whether output goes to the console, a file or both.
///     <see cref="ConsoleLoggerConfigurationModel" /> implements it with fluent <c>With*</c> setters.
/// </summary>
public interface IConsoleLoggerConfigurationModel
{
    /// <summary>
    ///     This property indicates whether text-format prefixes are padded to
    ///     <see cref="MaxPrefixLength" /> with each part centered, so columns line up.
    /// </summary>
    public bool CenterAlign { get; set; }

    /// <summary>
    ///     This property indicates whether debug and trace messages are written to the console. They
    ///     are always written to the log file.
    /// </summary>
    public bool Debug { get; set; }

    /// <summary>
    ///     This property specifies the file every message is appended to, or null for console only.
    /// </summary>
    public string File { get; set; }

    /// <summary>
    ///     This property specifies how each entry is laid out: padded text, a table, or a JSON, XML
    ///     or YAML document.
    /// </summary>
    public ConsoleLoggerLogFormatEnum Format { get; set; }

    /// <summary>
    ///     This property maps log levels (ignoring case) to console colors. The <c>_</c> entry is
    ///     the fallback for levels without their own entry.
    /// </summary>
    public Dictionary<string, ConsoleColor> Levels { get; set; }

    /// <summary>
    ///     This property specifies the width each prefix part is padded to when
    ///     <see cref="CenterAlign" /> is on. <see cref="ConsoleLogger" /> widens it to fit the longest
    ///     part logged so far.
    /// </summary>
    public int MaxPrefixLength { get; set; }

    /// <summary>
    ///     This property specifies the prefix parts added to every message, after the timestamp and
    ///     level and before the message's parts.
    /// </summary>
    public IConsoleLoggerPrefixModel Prefix { get; set; }

    /// <summary>
    ///     This property indicates whether console output is suppressed. The log file is still
    ///     written.
    /// </summary>
    public bool Quiet { get; set; }

    /// <summary>
    ///     This method retrieves the console color associated with a given log <paramref name="level" />.
    ///     If the log <paramref name="level" /> is not found in the Levels dictionary, it returns a default color.
    /// </summary>
    /// <param name="level">The log level for which to retrieve the associated console color.</param>
    /// <returns>
    ///     The console color associated with the specified log <paramref name="level" />,
    ///     or a default color if the level is not found which will be <see cref="ConsoleColor.DarkGray" />
    ///     if a default color is not specified in the Levels dictionary.
    /// </returns>
    public ConsoleColor GetConsoleColor(string level);

    /// <summary>
    ///     This method sets the <paramref name="centerAlign" /> state option for the console logger configuration.
    /// </summary>
    /// <param name="centerAlign">
    ///     A <see cref="bool" /> value indicating whether to enable or disable center alignment for log output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithCenterAlignment(bool centerAlign = true);

    /// <summary>
    ///     This method sets the <paramref name="debug" /> state option for the console logger configuration.
    /// </summary>
    /// <param name="debug">
    ///     A <see cref="bool" /> value indicating whether to enable or disable debug-level log output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithDebug(bool debug = true);

    /// <summary>
    ///     This method sets the <paramref name="file" /> path for the console logger configuration.
    /// </summary>
    /// <param name="file">
    ///     A <see cref="string" /> representing the file path where log output should be written.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithFile(string file);

    /// <summary>
    ///     This method sets the <paramref name="format" /> option for the console logger configuration.
    /// </summary>
    /// <param name="format">
    ///     A <see cref="ConsoleLoggerLogFormatEnum" /> value indicating the desired log format for console output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithFormat(ConsoleLoggerLogFormatEnum format);

    /// <summary>
    ///     This method sets the console color for a specific log
    ///     <paramref name="level" /> in the console logger configuration.
    /// </summary>
    /// <param name="level">
    ///     A <see cref="string" /> representing the log level for which to set the console color.
    /// </param>
    /// <param name="color">
    ///     A <see cref="ConsoleColor" /> value indicating the desired console color for the specified log level.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithLevel(string level, ConsoleColor color);

    /// <summary>
    ///     This method sets the <paramref name="prefix" /> option for the console logger configuration.
    /// </summary>
    /// <param name="prefix">
    ///     A <see cref="IConsoleLoggerPrefixModel" /> instance representing
    ///     the desired prefix configuration for console output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithPrefix(IConsoleLoggerPrefixModel prefix);

    /// <summary>
    ///     This method sets the <paramref name="quiet" /> option
    ///     for the quiet mode of the console logger configuration.
    /// </summary>
    /// <param name="quiet">
    ///     A <see cref="bool" /> value indicating whether to enable or disable quiet mode for log output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithQuiet(bool quiet = true);
}
