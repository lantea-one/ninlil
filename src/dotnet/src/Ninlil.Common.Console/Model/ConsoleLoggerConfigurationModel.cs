using System.Text.Json.Serialization;
using System.Xml.Serialization;
using Ninlil.Common.Console.Enum;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class holds the settings a <see cref="ConsoleLogger" /> writes messages with: layout
///     format (table by default), prefix, level colors, center alignment, debug and quiet
///     switches and an optional log file. The <c>With*</c> methods set them fluently.
/// </summary>
[XmlInclude(typeof(ConsoleLoggerPrefixModel))]
[XmlRoot("consoleLoggerConfiguration")]
public class ConsoleLoggerConfigurationModel : IConsoleLoggerConfigurationModel
{
    /// <summary>
    ///     This property indicates whether text-format prefixes are padded to
    ///     <see cref="MaxPrefixLength" /> with each part centered, so columns line up.
    /// </summary>
    [JsonPropertyName("centerAlign")]
    [XmlElement("centerAlign")]
    [YamlMember(Alias = "center-align")]
    public bool CenterAlign { get; set; }

    /// <summary>
    ///     This property indicates whether debug and trace messages are written to the console. They
    ///     are always written to the log file.
    /// </summary>
    [JsonPropertyName("debug")]
    [XmlElement("debug")]
    [YamlMember(Alias = "debug")]
    public bool Debug { get; set; }

    /// <summary>
    ///     This property specifies the file every message is appended to, or null for console only.
    /// </summary>
    [JsonPropertyName("file")]
    [XmlElement("file")]
    [YamlMember(Alias = "file")]
    public string File { get; set; }

    /// <summary>
    ///     This property specifies how each entry is laid out: padded text, a table, or a JSON, XML
    ///     or YAML document.
    /// </summary>
    [JsonPropertyName("format")]
    [XmlElement("format")]
    [YamlMember(Alias = "format")]
    public ConsoleLoggerLogFormatEnum Format { get; set; } = ConsoleLoggerLogFormatEnum.Table;

    /// <summary>
    ///     This property maps log levels (ignoring case) to console colors. The <c>_</c> entry is
    ///     the fallback for levels without their own entry.
    /// </summary>
    [JsonPropertyName("levels")]
    [XmlIgnore]
    [YamlMember(Alias = "levels")]
    public Dictionary<string, ConsoleColor> Levels { get; set; } =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "debug", ConsoleColor.DarkCyan },
            { "done", ConsoleColor.DarkGreen },
            { "error", ConsoleColor.DarkRed },
            { "fail", ConsoleColor.DarkRed },
            { "heartbeat", ConsoleColor.DarkMagenta },
            { "info", ConsoleColor.DarkBlue },
            { "notice", ConsoleColor.DarkYellow },
            { "success", ConsoleColor.DarkGreen },
            { "trace", ConsoleColor.Gray },
            { "warn", ConsoleColor.DarkYellow },
            { "_", ConsoleColor.DarkGray },
        };

    /// <summary>
    ///     This property specifies the width each prefix part is padded to when
    ///     <see cref="CenterAlign" /> is on. <see cref="ConsoleLogger" /> widens it to fit the longest
    ///     part logged so far.
    /// </summary>
    [JsonPropertyName("maxPrefixLength")]
    [XmlElement("maxPrefixLength")]
    [YamlMember(Alias = "max-prefix-length")]
    public int MaxPrefixLength { get; set; }

    /// <summary>
    ///     This property specifies the prefix parts added to every message, after the timestamp and
    ///     level and before the message's parts.
    /// </summary>
    [JsonPropertyName("prefix")]
    [XmlElement("prefix")]
    [YamlMember(Alias = "prefix")]
    public IConsoleLoggerPrefixModel Prefix { get; set; } = new ConsoleLoggerPrefixModel();

    /// <summary>
    ///     This property indicates whether console output is suppressed. The log file is still
    ///     written.
    /// </summary>
    [JsonPropertyName("quiet")]
    [XmlAttribute("quiet")]
    [YamlMember(Alias = "quiet")]
    public bool Quiet { get; set; }

    /// <summary>
    ///     This constructor creates a configuration with the defaults: table format, no prefix and
    ///     debug output off.
    /// </summary>
    public ConsoleLoggerConfigurationModel() => Debug = false;

    /// <summary>
    ///     This constructor initializes a new instance of the <see cref="ConsoleLoggerConfigurationModel" />
    ///     class with the specified logging <paramref name="format" /> and optional <paramref name="prefix" />.
    /// </summary>
    /// <param name="format">
    ///     The logging format to be used for console output, represented
    ///     by the <see cref="ConsoleLoggerLogFormatEnum" /> enumeration.
    /// </param>
    /// <param name="prefix">
    ///     The optional prefix parts for every message.
    /// </param>
    public ConsoleLoggerConfigurationModel(
        ConsoleLoggerLogFormatEnum format,
        IEnumerable<string> prefix
    ) =>
        (Debug, Format, Prefix) = (
            false,
            format,
            prefix?.ToArray() ?? new ConsoleLoggerPrefixModel()
        );

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
    public ConsoleColor GetConsoleColor(string level) =>
        ConsoleLogger.GetSanitizedPart(level, true)?.Trim() is not (null or { Length: 0 })
            ? Levels.TryGetValue(ConsoleLogger.GetSanitizedPart(level, true), out var color)
                ? color
                : Levels.TryGetValue("_", out var defaultColor)
                    ? defaultColor
                    : ConsoleColor.DarkGray
            : Levels.TryGetValue("_", out var defaultColor2)
                ? defaultColor2
                : ConsoleColor.DarkGray;

    /// <summary>
    ///     This method sets the <paramref name="centerAlign" /> state option for the console logger configuration.
    /// </summary>
    /// <param name="centerAlign">
    ///     A <see cref="bool" /> value indicating whether to enable or disable center alignment for log output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithCenterAlignment(bool centerAlign = true)
    {
        // Reset the center alignment setting to the specified value.
        CenterAlign = centerAlign;

        // We're done, return the current instance for method chaining.
        return this;
    }

    /// <summary>
    ///     This method sets the <paramref name="debug" /> state option for the console logger configuration.
    /// </summary>
    /// <param name="debug">
    ///     A <see cref="bool" /> value indicating whether to enable or disable debug-level log output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithDebug(bool debug = true)
    {
        // Reset the debug setting to the specified value.
        Debug = debug;

        // We're done, return the current instance for method chaining.
        return this;
    }

    /// <summary>
    ///     This method sets the <paramref name="file" /> path for the console logger configuration.
    /// </summary>
    /// <param name="file">
    ///     A <see cref="string" /> representing the file path where log output should be written.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithFile(string file)
    {
        // If the provided file path is null, empty, or consists only of
        // whitespace, return the current instance without making any changes.
        if (file?.Trim() is null or { Length: 0 })
            return this;

        // Store the file as a full path, so a later change of working directory does not move the log.
        File = Path.GetFullPath(file.Trim());

        // We're done, return the current instance for method chaining.
        return this;
    }

    /// <summary>
    ///     This method sets the <paramref name="format" /> option for the console logger configuration.
    /// </summary>
    /// <param name="format">
    ///     A <see cref="ConsoleLoggerLogFormatEnum" /> value indicating the desired log format for console output.
    /// </param>
    /// <returns>
    ///     The current instance of <see cref="IConsoleLoggerConfigurationModel" /> to allow for method chaining.
    /// </returns>
    public IConsoleLoggerConfigurationModel WithFormat(ConsoleLoggerLogFormatEnum format)
    {
        // Reset the log format to the specified value.
        Format = format;

        // We're done, return the current instance for method chaining.
        return this;
    }

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
    public IConsoleLoggerConfigurationModel WithLevel(string level, ConsoleColor color)
    {
        // Sanitize the provided log level string to ensure it is valid and safe for use.
        var sanitized = ConsoleLogger.GetSanitizedPart(level, true);

        // If the sanitized log level is null or empty, return the current instance without making any changes.
        if (sanitized is null or { Length: 0 })
            return this;

        // Reset the color for the specified log level to the provided value.
        Levels[sanitized] = color;

        // We're done, return the current instance for method chaining.
        return this;
    }

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
    public IConsoleLoggerConfigurationModel WithPrefix(IConsoleLoggerPrefixModel prefix)
    {
        // Reset the prefix to the provided value, or create a new instance if the provided value is null.
        Prefix = prefix ?? new ConsoleLoggerPrefixModel();

        // We're done, return the current instance for method chaining.
        return this;
    }

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
    public IConsoleLoggerConfigurationModel WithQuiet(bool quiet = true)
    {
        // Reset the quiet setting to the specified value.
        Quiet = quiet;

        // We're done, return the current instance for method chaining.
        return this;
    }
}
