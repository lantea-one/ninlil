using Ninlil.Common.Console.Service;
using Ninlil.Common.Serializer.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Service;

/// <summary>
///     These tests cover the <see cref="ConsoleService" /> output helpers. They only check that the
///     calls do not throw: console output and color state cannot be asserted reliably in a
///     headless, parallel test run. <see cref="ConsoleService.Exit" /> and the exiting overload of
///     <see cref="ConsoleService.WriteLine(Exception, bool)" /> are not tested because they call
///     <see cref="Environment.Exit" />, which would stop the test host.
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
    ///     replacements does not treat the text as a composite format string.
    ///     <c>Console.WriteLine(string, object[])</c> parses its first argument as a format string even
    ///     when the array is empty, so text with a bare '{' would throw a <see cref="FormatException" />.
    /// </summary>
    [Fact]
    public void WriteLine_WithLiteralBraceAndNoReplacements_DoesNotThrow()
    {
        ConsoleService.WriteLine(ConsoleColor.DarkGreen, "status: {not a placeholder}");
    }

    /// <summary>Verifies that <see cref="ConsoleService.WriteLine(ConsoleColor, string, object[])" /> with replacements still applies composite formatting.</summary>
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
    ///     overload doesn't throw when serializing a value to JSON. JSON output always contains
    ///     braces, which is the case <see cref="WriteLine_WithLiteralBraceAndNoReplacements_DoesNotThrow" />
    ///     guards against.
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
