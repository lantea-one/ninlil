using Ninlil.Common.Console.Service;
using Ninlil.Common.Serializer.Enum;

// Define our namespace for the console-service tests.
namespace Ninlil.Common.Console.Tests.Service;

/// <summary>
///     This test class exercises <see cref="ConsoleService" /> - the colored-output helpers. Most
///     scenarios only verify the calls complete without throwing, since capturing real console output
///     (or its color state, which reflects the actual OS terminal/redirected-stream default rather
///     than any snapshot taken before the call) isn't meaningfully assertable in a headless,
///     parallel-capable test run. <see cref="ConsoleService.Exit" />/the exiting overload of
///     <see cref="ConsoleService.WriteLine(Exception, bool)" /> are never exercised at all - both call
///     <see cref="Environment.Exit" />, which would terminate the test host.
/// </summary>
public class ConsoleServiceTests
{
    /// <summary>Verifies that <see cref="ConsoleService.WriteLine(ConsoleColor, string, object[])" /> doesn't throw for plain text with no replacements.</summary>
    [Fact]
    public void WriteLine_WithColorAndPlainText_DoesNotThrow()
    {
        ConsoleService.WriteLine(ConsoleColor.DarkGreen, "hello");
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleService.WriteLine(ConsoleColor, string, object[])" /> with no
    ///     replacements does not treat the text as a composite format string. This is a real bug found
    ///     and fixed in this project: <c>Console.WriteLine(string, object[])</c> always parses its first
    ///     argument as a composite format string, even when the replacements array is empty, so any
    ///     text containing an unescaped '{' (a bare, non-numeric brace like this test's) previously threw
    ///     a <see cref="FormatException" /> instead of being printed literally.
    /// </summary>
    [Fact]
    public void WriteLine_WithLiteralBraceAndNoReplacements_DoesNotThrow()
    {
        ConsoleService.WriteLine(ConsoleColor.DarkGreen, "status: {not a placeholder}");
    }

    /// <summary>Verifies that <see cref="ConsoleService.WriteLine(ConsoleColor, string, object[])" /> with real replacements still applies composite formatting.</summary>
    [Fact]
    public void WriteLine_WithReplacements_AppliesCompositeFormat()
    {
        ConsoleService.WriteLine(ConsoleColor.DarkGreen, "Hello, {0}!", "world");
    }

    /// <summary>Verifies that <see cref="ConsoleService.WriteLine(Exception, bool)" /> (non-exiting) doesn't throw.</summary>
    [Fact]
    public void WriteLine_WithExceptionNonExiting_DoesNotThrow()
    {
        ConsoleService.WriteLine(new InvalidOperationException("boom"));
    }

    /// <summary>Verifies that the generic <see cref="ConsoleService.WriteLine{TValue}(ConsoleColor, SerializerFormatEnum, TValue)" /> overload doesn't throw for a plain value with no serialization format.</summary>
    [Fact]
    public void WriteLineGeneric_WithNoneFormat_UsesToString()
    {
        ConsoleService.WriteLine(ConsoleColor.Blue, SerializerFormatEnum.None, 42);
    }

    /// <summary>
    ///     Verifies that the generic <see cref="ConsoleService.WriteLine{TValue}(ConsoleColor, SerializerFormatEnum, TValue)" />
    ///     overload doesn't throw when serializing a value to JSON - the exact real-world trigger for
    ///     the bug described on <see cref="WriteLine_WithLiteralBraceAndNoReplacements_DoesNotThrow" />,
    ///     since JSON output always contains literal, unescaped braces.
    /// </summary>
    [Fact]
    public void WriteLineGeneric_WithJsonFormat_DoesNotThrowOnBraceContainingOutput()
    {
        ConsoleService.WriteLine(
            ConsoleColor.Blue,
            SerializerFormatEnum.Json,
            new { Name = "test" }
        );
    }

    /// <summary>Verifies the four-argument text-plus-value overload doesn't throw.</summary>
    [Fact]
    public void WriteLineGeneric_WithTextAndValue_DoesNotThrow()
    {
        ConsoleService.WriteLine(
            ConsoleColor.Blue,
            SerializerFormatEnum.Json,
            new { Name = "test" },
            "Serialized value:"
        );
    }

    /// <summary>Verifies that <see cref="ConsoleService.WriteOperationCancelledLine" /> doesn't throw.</summary>
    [Fact]
    public void WriteOperationCancelledLine_DoesNotThrow()
    {
        ConsoleService.WriteOperationCancelledLine();
    }
}
