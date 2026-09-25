using System.Diagnostics.CodeAnalysis;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Service;

// Define our namespace.
namespace Ninlil.Common.Console.Service;

/// <summary>
///     This static class writes colored text, exceptions and serialized values to the console
///     and resets the color afterwards. The parser uses it for help text and errors.
/// </summary>
public static class ConsoleService
{
    /// <summary>
    ///     This method resets the console color to the default color and
    ///     exits the application with the provided <paramref name="code" />.
    /// </summary>
    /// <param name="code">The exit code to exit the application with. Default is 0.</param>
    public static void Exit(int code = 0)
    {
        // Reset the console color to the default color.
        System.Console.ResetColor();

        // Exit the application with the provided exit code.
        Environment.Exit(code);
    }

    /// <summary>
    ///     This method writes a line of <paramref name="text" /> to the console in the
    ///     specified <paramref name="color" /> with optional <paramref name="replacements" />.
    /// </summary>
    /// <param name="color">The color to write the <paramref name="text" /> in.</param>
    /// <param name="text">The text to write to the console.</param>
    /// <param name="replacements">Optional replacements for the <paramref name="text" />.</param>
    public static void WriteLine(
        ConsoleColor color,
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string text,
        params object[] replacements
    )
    {
        // Set the console color to the provided color.
        System.Console.ForegroundColor = color;

        // Only use composite formatting when there are replacements. Console.WriteLine(string,
        // object[]) treats its first argument as a format string even when the array is empty, so
        // plain text containing '{' or '}' (such as the serialized JSON, XML or YAML the
        // WriteLine<TValue> overloads pass) would throw a FormatException.
        if (replacements is { Length: > 0 })
            System.Console.WriteLine(text, replacements);
        else
            System.Console.WriteLine(text);

        // Reset the console color to the default color.
        System.Console.ResetColor();
    }

    /// <summary>
    ///     This method writes the <paramref name="exception" /> to standard error in dark red.
    /// </summary>
    /// <param name="exception">The exception to write to the console.</param>
    /// <param name="exit">If true, the application will exit with code 1 after writing the exception.</param>
    public static void WriteLine(Exception exception, bool exit = false)
    {
        // Set the console color to dark red.
        System.Console.ForegroundColor = ConsoleColor.DarkRed;

        // Write the line to the console.
        System.Console.Error.WriteLine(exception);

        // Reset the console color to the default color.
        System.Console.ResetColor();

        // Check the exit flag, then exit if we need to.
        if (exit)
            Exit(1);
    }

    /// <summary>
    ///     This method serializes a <paramref name="value" /> into the specified <paramref name="format" />
    ///     and writes it to the console in the specified <paramref name="color" />. With
    ///     <see cref="SerializerFormatEnum.None" /> the value's <see cref="object.ToString" /> is written.
    /// </summary>
    /// <param name="color">The color to write the serialized <paramref name="value" /> in.</param>
    /// <param name="format">The format to serialize the <paramref name="value" /> into.</param>
    /// <param name="value">The value to serialize and write to the console.</param>
    /// <typeparam name="TValue">
    ///     The type of the <paramref name="value" /> to serialize and write to the console.
    /// </typeparam>
    public static void WriteLine<TValue>(
        ConsoleColor color,
        SerializerFormatEnum format,
        TValue value
    ) =>
        WriteLine(
            color,
            format is SerializerFormatEnum.None
                ? value?.ToString()
                : SerializerService.Serialize(value, format)
        );

    /// <summary>
    ///     This method writes a line of <paramref name="text" /> to the console in the
    ///     specified <paramref name="color" /> with optional <paramref name="replacements" />
    ///     and then serializes a <paramref name="value" /> into the specified <paramref name="format" />
    ///     and writes it to the console in the specified <paramref name="color" />.
    /// </summary>
    /// <param name="color">
    ///     The color to write the <paramref name="text" /> and serialized <paramref name="value" /> in.
    /// </param>
    /// <param name="format">The format to serialize the <paramref name="value" /> into.</param>
    /// <param name="value">The value to serialize and write to the console.</param>
    /// <param name="text">The text to write to the console.</param>
    /// <param name="replacements">Optional replacements for the <paramref name="text" />.</param>
    /// <typeparam name="TValue">
    ///     The type of the <paramref name="value" /> to serialize and write to the console.
    /// </typeparam>
    public static void WriteLine<TValue>(
        ConsoleColor color,
        SerializerFormatEnum format,
        TValue value,
        [StringSyntax(StringSyntaxAttribute.CompositeFormat)] string text,
        params object[] replacements
    )
    {
        // First we write the message to the console.
        WriteLine(color, text, replacements);

        // Then we write the serialized value to the console.
        WriteLine(color, format, value);
    }

    /// <summary>
    ///     This method writes a standard "operation cancelled" message in dark red.
    /// </summary>
    public static void WriteOperationCancelledLine() =>
        WriteLine(ConsoleColor.DarkRed, "Asynchronous operation has cancelled.");
}
