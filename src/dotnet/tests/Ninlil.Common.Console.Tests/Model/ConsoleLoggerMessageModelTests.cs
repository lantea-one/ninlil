using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace for the console-logger-message-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleLoggerMessageModel" /> - its three constructor
///     overloads, and its string-rendering paths across the table/text/structured (JSON/XML/YAML)
///     log formats, along with <see cref="ConsoleLoggerMessageModel.WriteAsync" />'s idempotency and
///     quiet-mode behavior.
/// </summary>
public class ConsoleLoggerMessageModelTests
{
    /// <summary>
    ///     Verifies the level/message/data/prefix constructor sanitizes the level (trimming brackets/
    ///     spaces/dashes, without changing case - <see cref="ConsoleLogger.GetSanitizedPart" /> never
    ///     lowercases) and trims the message, and - a real bug found and fixed in this project - keeps
    ///     a real, populated prefix rather than silently discarding it.
    /// </summary>
    [Fact]
    public void Constructor_WithLevelMessageDataPrefix_SanitizesAndTrims()
    {
        ConsoleLoggerMessageModel message = new(
            "[ERROR]",
            "  Something failed.  ",
            null,
            new ConsoleLoggerPrefixModel().WithPart("svc")
        );

        Assert.Equal("ERROR", message.Level);
        Assert.Equal("Something failed.", message.Message);
        Assert.Contains("svc", message.Prefix);
    }

    /// <summary>Verifies the level/message/prefix-enumerable constructor builds a prefix model from the given parts.</summary>
    [Fact]
    public void Constructor_WithLevelMessagePrefixEnumerable_BuildsPrefixModel()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", ["svc", "worker"]);

        Assert.Contains("svc", message.Prefix);
        Assert.Contains("worker", message.Prefix);
        Assert.Null(message.Data);
    }

    /// <summary>Verifies that a null prefix falls back to a fresh, empty prefix model rather than staying null.</summary>
    [Fact]
    public void Constructor_WithNullPrefix_FallsBackToEmptyPrefixModel()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", null, null);

        Assert.NotNull(message.Prefix);
        Assert.Empty(message.Prefix);
    }

    /// <summary>Verifies the level/data/prefix-enumerable constructor carries the data object through with no message.</summary>
    [Fact]
    public void Constructor_WithLevelDataPrefixEnumerable_CarriesDataWithNoMessage()
    {
        ConsoleLoggerMessageModel message = new("info", new { Key = "value" }, ["svc"]);

        Assert.Null(message.Message);
        Assert.NotNull(message.Data);
    }

    /// <summary>Verifies default property values on the parameterless constructor.</summary>
    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        ConsoleLoggerMessageModel message = new();

        Assert.Equal("info", message.Level);
        Assert.Equal(4, message.Padding);
        Assert.Equal(ConsoleLoggerLogFormatEnum.Yaml, message.Format);
        Assert.False(message.Written);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToTable" /> produces output containing the message and level.</summary>
    [Fact]
    public void ToTable_IncludesLevelAndMessage()
    {
        ConsoleLoggerMessageModel message = new("info", "hello world", (IEnumerable<string>)null);

        string result = message.ToTable();

        Assert.Contains("info", result);
        Assert.Contains("hello world", result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToString(IConsoleLoggerConfigurationModel)" /> in table format renders a table.</summary>
    [Fact]
    public void ToString_WithTableConfiguration_RendersTable()
    {
        ConsoleLoggerMessageModel message = new("info", "hello world", (IEnumerable<string>)null);
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Table,
        };

        string result = message.ToString(configuration);

        Assert.Contains("hello world", result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToString(IConsoleLoggerConfigurationModel)" /> in text format renders the message with the level in the prefix.</summary>
    [Fact]
    public void ToString_WithTextConfiguration_IncludesLevelAndMessage()
    {
        ConsoleLoggerMessageModel message = new("warn", "careful now", (IEnumerable<string>)null);
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Text,
        };

        string result = message.ToString(configuration);

        Assert.Contains("warn", result);
        Assert.Contains("careful now", result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToString(IConsoleLoggerConfigurationModel)" /> in text format includes the attached data, indented.</summary>
    [Fact]
    public void ToString_WithTextConfigurationAndData_IncludesSerializedData()
    {
        ConsoleLoggerMessageModel message = new(
            "info",
            "starting up",
            42,
            (IConsoleLoggerPrefixModel)null
        );
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Text,
        };

        string result = message.ToString(configuration);

        Assert.Contains("42", result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToString(IConsoleLoggerConfigurationModel)" /> in JSON format renders a JSON payload.</summary>
    [Fact]
    public void ToString_WithJsonConfiguration_RendersJson()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", (IEnumerable<string>)null);
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Json,
        };

        string result = message.ToString(configuration);

        Assert.Contains("{", result);
        Assert.Contains("hello", result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.ToString()" /> (parameterless) uses a default configuration.</summary>
    [Fact]
    public void ToString_Parameterless_UsesDefaultConfiguration()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", (IEnumerable<string>)null);

        string result = message.ToString();

        Assert.False(string.IsNullOrWhiteSpace(result));
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLoggerMessageModel.WriteAsync" /> flips
    ///     <see cref="ConsoleLoggerMessageModel.Written" /> to true and does not write to the console
    ///     twice (idempotency), using quiet mode so the assertion doesn't depend on capturing console output.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SetsWrittenFlagAndIsIdempotent()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", (IEnumerable<string>)null);
        ConsoleLoggerConfigurationModel configuration = new() { Quiet = true };

        await message.WriteAsync(configuration);
        Assert.True(message.Written);

        // A second call should be a no-op (the Written guard short-circuits before doing any work again).
        IConsoleLoggerMessageModel result = await message.WriteAsync(configuration);
        Assert.Same(message, result);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerMessageModel.Write" /> (synchronous) also sets the Written flag.</summary>
    [Fact]
    public void Write_Synchronous_SetsWrittenFlag()
    {
        ConsoleLoggerMessageModel message = new("info", "hello", (IEnumerable<string>)null);
        ConsoleLoggerConfigurationModel configuration = new() { Quiet = true };

        message.Write(configuration);

        Assert.True(message.Written);
    }
}
