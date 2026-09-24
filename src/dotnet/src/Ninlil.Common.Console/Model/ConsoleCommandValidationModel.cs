using System.Text.Json.Serialization;
using System.Xml.Serialization;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Console.Model;

/// <summary>
///     This class represents the validation container for a console command.
/// </summary>
[XmlRoot("ConsoleCommandValidation")]
public class ConsoleCommandValidationModel
{
    /// <summary>
    ///     This property contains the invalid arguments with their respective error messages.
    /// </summary>
    [JsonPropertyName("invalid")]
    [XmlElement("invalid")]
    [YamlMember(Alias = "invalid")]
    public List<KeyValuePair<string, string>> InvalidArguments { get; set; } = [];

    /// <summary>
    ///     This property contains the missing arguments.
    /// </summary>
    [JsonPropertyName("missing")]
    [XmlElement("missing")]
    [YamlMember(Alias = "missing")]
    public List<string> MissingArguments { get; set; } = [];

    /// <summary>
    ///     This property contains the unknown arguments.
    /// </summary>
    [JsonPropertyName("unknown")]
    [XmlElement("unknown")]
    [YamlMember(Alias = "unknown")]
    public List<string> UnknownArguments { get; set; } = [];

    /// <summary>
    ///     This method converts the validation results to a human-readable string.
    /// </summary>
    /// <returns>The human-readable string.</returns>
    public override string ToString()
    {
        // Define our list of parts.
        List<string> parts = [];

        // Check for missing arguments.
        if (MissingArguments.Any())
            parts.Add(ToMissingString());

        // Check for invalid arguments.
        if (InvalidArguments.Any())
            parts.Add(ToInvalidString());

        // Check for unknown arguments.
        if (UnknownArguments.Any())
            parts.Add(ToUnknownString());

        // We're done, return the parts joined by new lines.
        return string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    /// <summary>
    ///     This method checks if the command is valid.
    /// </summary>
    /// <returns><see langword="true" /> if the command is valid; otherwise, <see langword="false" />.</returns>
    public bool IsValid() =>
        !InvalidArguments.Any() && !MissingArguments.Any() && !UnknownArguments.Any();

    /// <summary>
    ///     This method converts the validation results to a human-readable
    ///     string for the <see cref="InvalidArguments" /> list.
    /// </summary>
    /// <returns>The human-readable string.</returns>
    public string ToInvalidString() =>
        string.Format(
            "Invalid Arguments:{0}{1}{2}{0}",
            Environment.NewLine,
            ConsoleHelpTextCollectionModel.Tab,
            string.Join(
                $"{Environment.NewLine}{ConsoleHelpTextCollectionModel.Tab}",
                InvalidArguments
                    .Where(a => a.Key?.Trim() is not (null or ""))
                    .Where(a => a.Value?.Trim() is not (null or ""))
                    .Select(a => new KeyValuePair<string, string>(a.Key.Trim(), a.Value.Trim()))
                    .Select(a =>
                        string.Format(
                            "{0}{1}{2}{2}{3}{1}",
                            a.Key,
                            Environment.NewLine,
                            ConsoleHelpTextCollectionModel.Tab,
                            a.Value
                        )
                    )
            )
        );

    /// <summary>
    ///     This method converts the validation results to a human-readable
    ///     string for the <see cref="MissingArguments" /> list.
    /// </summary>
    /// <returns>The human-readable string.</returns>
    public string ToMissingString() =>
        string.Format(
            "Missing Arguments:{0}{1}{2}{0}",
            Environment.NewLine,
            ConsoleHelpTextCollectionModel.Tab,
            string.Join(
                $"{Environment.NewLine}{ConsoleHelpTextCollectionModel.Tab}",
                MissingArguments.Where(a => a?.Trim() is not (null or "")).Select(a => a.Trim())
            )
        );

    /// <summary>
    ///     This method converts the validation results to a human-readable
    ///     string for the <see cref="UnknownArguments" /> list.
    /// </summary>
    /// <returns>The human-readable string.</returns>
    public string ToUnknownString() =>
        string.Format(
            "Unknown Arguments:{0}{1}{2}{0}",
            Environment.NewLine,
            ConsoleHelpTextCollectionModel.Tab,
            string.Join(
                $"{Environment.NewLine}{ConsoleHelpTextCollectionModel.Tab}",
                UnknownArguments.Where(a => a?.Trim() is not (null or "")).Select(a => a.Trim())
            )
        );

    /// <summary>
    ///     This method fluently adds an <paramref name="argument" /> and
    ///     <paramref name="message" /> to the <see cref="InvalidArguments" />.
    /// </summary>
    /// <param name="argument">The name of the invalid argument.</param>
    /// <param name="message">The message describing why the argument is invalid.</param>
    /// <returns>The updated <see cref="ConsoleCommandValidationModel" /> instance.</returns>
    /// <exception cref="ArgumentNullException">
    ///     When the <paramref name="argument" /> or <paramref name="message" /> is <see langword="null" /> or empty.
    /// </exception>
    public ConsoleCommandValidationModel WithInvalidArgument(string argument, string message)
    {
        // Ensure the argument is valid.
        if (argument?.Trim() is null or "")
            throw new ArgumentNullException(nameof(argument));

        // Ensure the message is valid.
        if (message?.Trim() is null or "")
            throw new ArgumentNullException(nameof(message));

        // Add the invalid argument to the list.
        InvalidArguments.Add(new KeyValuePair<string, string>(argument.Trim(), message.Trim()));

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method fluently adds an <paramref name="argument" /> to the <see cref="MissingArguments" />.
    /// </summary>
    /// <param name="argument">The name of the missing argument.</param>
    /// <returns>The updated <see cref="ConsoleCommandValidationModel" /> instance.</returns>
    /// <exception cref="ArgumentNullException">
    ///     When the <paramref name="argument" /> is <see langword="null" /> or empty.
    /// </exception>
    public ConsoleCommandValidationModel WithMissingArgument(string argument)
    {
        // Ensure the argument is valid.
        if (argument?.Trim() is null or "")
            throw new ArgumentNullException(nameof(argument));

        // Add the missing argument to the list.
        MissingArguments.Add(argument.Trim());

        // We're done, return the instance.
        return this;
    }

    /// <summary>
    ///     This method fluently adds an <paramref name="argument" /> to the <see cref="UnknownArguments" />.
    /// </summary>
    /// <param name="argument">The name of the unknown argument.</param>
    /// <returns>The updated <see cref="ConsoleCommandValidationModel" /> instance.</returns>
    /// <exception cref="ArgumentNullException">
    ///     When the <paramref name="argument" /> is <see langword="null" /> or empty.
    /// </exception>
    public ConsoleCommandValidationModel WithUnknownArgument(string argument)
    {
        // Ensure the argument is valid.
        if (argument?.Trim() is null or "")
            throw new ArgumentNullException(nameof(argument));

        // Add the unknown argument to the list.
        UnknownArguments.Add(argument.Trim());

        // We're done, return the instance.
        return this;
    }
}
