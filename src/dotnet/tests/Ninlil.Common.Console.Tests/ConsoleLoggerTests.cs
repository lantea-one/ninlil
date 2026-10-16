using Ninlil.Common.Console;
using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests;

/// <summary>
///     These tests cover <see cref="ConsoleLogger" /> and the class-typed
///     <see cref="ConsoleLogger{TClass}" />: the factories, the level-named log methods, prefix-width
///     tracking, and adding the class name to the prefix. Every scenario logs with a quiet
///     configuration so assertions do not depend on console output.
/// </summary>
public class ConsoleLoggerTests
{
    /// <summary>Verifies that <see cref="ConsoleLogger.Create()" /> returns an instance with the documented default table format.</summary>
    [Fact]
    public void Create_Parameterless_DefaultsToTableFormat()
    {
        IConsoleLogger logger = ConsoleLogger.Create();

        Assert.Equal(ConsoleLoggerLogFormatEnum.Table, logger.Configuration.Format);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.Create(IConsoleLoggerConfigurationModel)" /> uses the supplied configuration.</summary>
    [Fact]
    public void Create_WithConfiguration_UsesSuppliedConfiguration()
    {
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Json,
        };

        IConsoleLogger logger = ConsoleLogger.Create(configuration);

        Assert.Same(configuration, logger.Configuration);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.Create(ConsoleLoggerLogFormatEnum, IEnumerable{string})" /> applies the given format and prefix.</summary>
    [Fact]
    public void Create_WithFormatAndPrefix_AppliesBoth()
    {
        IConsoleLogger logger = ConsoleLogger.Create(ConsoleLoggerLogFormatEnum.Xml, ["service"]);

        Assert.Equal(ConsoleLoggerLogFormatEnum.Xml, logger.Configuration.Format);
        Assert.Contains("service", logger.Configuration.Prefix);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.GetSanitizedPart" /> trims brackets and spaces.</summary>
    [Fact]
    public void GetSanitizedPart_TrimsBracketsAndSpaces()
    {
        Assert.Equal("info", ConsoleLogger.GetSanitizedPart("  [info]  "));
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.GetSanitizedPart" /> replaces spaces with hyphens when requested.</summary>
    [Fact]
    public void GetSanitizedPart_WithReplaceSpaces_ReplacesSpacesWithHyphens()
    {
        Assert.Equal("my-part", ConsoleLogger.GetSanitizedPart("my part", true));
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.GetSanitizedPart" /> returns null for a null or empty part.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[-]")]
    public void GetSanitizedPart_ReturnsNullForEffectivelyEmptyPart(string part)
    {
        Assert.Null(ConsoleLogger.GetSanitizedPart(part));
    }

    /// <summary>Verifies that a LogXxx call adds a message to <see cref="ConsoleLogger.Messages" /> with the expected level.</summary>
    [Fact]
    public void LogInfo_AddsMessageWithInfoLevel()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;

        logger.LogInformation("hello");

        Assert.Single(logger.Messages);
        Assert.Equal("info", logger.Messages[0].Level);
        Assert.Equal("hello", logger.Messages[0].Message);
    }

    /// <summary>Verifies that each distinctly named LogXxx helper stamps the expected level string.</summary>
    [Theory]
    [InlineData(nameof(IConsoleLogger.LogDebug), "debug")]
    [InlineData(nameof(IConsoleLogger.LogDone), "done")]
    [InlineData(nameof(IConsoleLogger.LogError), "error")]
    [InlineData(nameof(IConsoleLogger.LogFatal), "fail")]
    [InlineData(nameof(IConsoleLogger.LogHeartbeat), "heartbeat")]
    [InlineData(nameof(IConsoleLogger.LogNotice), "notice")]
    [InlineData(nameof(IConsoleLogger.LogSuccess), "done")]
    [InlineData(nameof(IConsoleLogger.LogTrace), "trace")]
    [InlineData(nameof(IConsoleLogger.LogWarning), "warn")]
    public void LogXxx_StampsExpectedLevel(string methodName, string expectedLevel)
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;

        var method = typeof(IConsoleLogger).GetMethod(methodName);
        method!.Invoke(
            logger,
            ["a message", null, null, 0, 4, ConsoleLoggerLogFormatEnum.Yaml, null]
        );

        Assert.Single(logger.Messages);
        Assert.Equal(expectedLevel, logger.Messages[0].Level);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.NewMessage" /> builds a message model with the given parameters.</summary>
    [Fact]
    public void NewMessage_BuildsConfiguredMessageModel()
    {
        IConsoleLogger logger = ConsoleLogger.Create();

        IConsoleLoggerMessageModel message = logger.NewMessage(
            "info",
            "hello",
            ["svc"],
            data: 42,
            buffer: 1,
            padding: 8,
            format: ConsoleLoggerLogFormatEnum.Json,
            color: ConsoleColor.Cyan
        );

        Assert.Equal("info", message.Level);
        Assert.Equal("hello", message.Message);
        Assert.Contains("svc", message.Prefix);
        Assert.Equal(42, message.Data);
        Assert.Equal(1, message.Buffer);
        Assert.Equal(8, message.Padding);
        Assert.Equal(ConsoleLoggerLogFormatEnum.Json, message.Format);
        Assert.Equal(ConsoleColor.Cyan, message.Color);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.LogMessage(IConsoleLoggerMessageModel)" /> tracks the maximum prefix length seen so far.</summary>
    [Fact]
    public void LogMessage_TracksMaximumPrefixLength()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;

        logger.LogInformation("short", ["ab"]);
        logger.LogInformation("longer", ["abcdefgh"]);

        Assert.True(logger.Configuration.MaxPrefixLength >= 8);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.Flush" /> writes every not-yet-written message.</summary>
    [Fact]
    public void Flush_WritesEveryUnwrittenMessage()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;
        logger.Messages.Add(
            new ConsoleLoggerMessageModel("info", "queued", (IEnumerable<string>)null)
        );

        logger.Flush();

        Assert.All(logger.Messages, m => Assert.True(m.Written));
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.ToString" /> concatenates the rendering of every logged message.</summary>
    [Fact]
    public void ToString_ConcatenatesEveryMessage()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;

        logger.LogInformation("hello world");

        Assert.Contains("hello world", logger.ToString());
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.Write" /> (synchronous) returns the same instance for chaining.</summary>
    [Fact]
    public void Write_ReturnsSameInstanceForChaining()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;
        logger.LogInformation("hello");

        IConsoleLogger result = logger.Write();

        Assert.Same(logger, result);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.WriteAsync" /> returns the same instance for chaining.</summary>
    [Fact]
    public async Task WriteAsync_ReturnsSameInstanceForChaining()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        logger.Configuration.Quiet = true;
        logger.LogInformation("hello");

        IConsoleLogger result = await logger.WriteAsync();

        Assert.Same(logger, result);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.WithConfiguration" /> replaces the configuration.</summary>
    [Fact]
    public void WithConfiguration_ReplacesConfiguration()
    {
        IConsoleLogger logger = ConsoleLogger.Create();
        ConsoleLoggerConfigurationModel newConfiguration = new()
        {
            Format = ConsoleLoggerLogFormatEnum.Yml,
        };

        logger.WithConfiguration(newConfiguration);

        Assert.Same(newConfiguration, logger.Configuration);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger{TClass}.Create()" /> auto-inserts the class name as the first prefix part.</summary>
    [Fact]
    public void GenericCreate_Parameterless_InsertsClassNameAsPrefix()
    {
        IConsoleLogger<ConsoleLoggerTests> logger = ConsoleLogger<ConsoleLoggerTests>.Create();

        Assert.Contains(nameof(ConsoleLoggerTests), logger.Configuration.Prefix);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLogger{TClass}.Create(ConsoleLoggerLogFormatEnum, IEnumerable{string})" />
    ///     is declared to return <see cref="IConsoleLogger{TClass}" />, not the plain
    ///     <see cref="IConsoleLogger" />. <c>UseConsoleLogging&lt;TClassPrefix&gt;(format, prefix)</c>
    ///     registers <c>_ =&gt; ConsoleLogger&lt;TClassPrefix&gt;.Create(format, prefix)</c>, so the service
    ///     type it registers comes from this return type. <c>ServiceCollectionConsoleExtensionTests</c>
    ///     checks the registration itself.
    /// </summary>
    [Fact]
    public void GenericCreate_WithFormatAndPrefix_ReturnsGenericallyTypedInstance()
    {
        IConsoleLogger<ConsoleLoggerTests> logger = ConsoleLogger<ConsoleLoggerTests>.Create(
            ConsoleLoggerLogFormatEnum.Json,
            ["extra"]
        );

        Assert.Equal(ConsoleLoggerLogFormatEnum.Json, logger.Configuration.Format);
        Assert.Contains(nameof(ConsoleLoggerTests), logger.Configuration.Prefix);
        Assert.Contains("extra", logger.Configuration.Prefix);
    }

    /// <summary>Verifies that the configuration constructor inserts the class name only when it isn't already present.</summary>
    [Fact]
    public void GenericConstructor_WithConfiguration_DoesNotDuplicateClassNamePrefix()
    {
        ConsoleLoggerConfigurationModel configuration = new()
        {
            Prefix = new ConsoleLoggerPrefixModel().WithPart(nameof(ConsoleLoggerTests)),
        };

        IConsoleLogger<ConsoleLoggerTests> logger = ConsoleLogger<ConsoleLoggerTests>.Create(
            configuration
        );

        Assert.Single(logger.Configuration.Prefix, p => p == nameof(ConsoleLoggerTests));
    }

    /// <summary>Verifies that the logger keeps at most 1,000 written messages by default and the newest survive.</summary>
    [Fact]
    public void LogMessage_BeyondTheDefaultCap_KeepsTheNewestThousand()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true } };

        for (int i = 0; i < 1500; i++)
            logger.LogInformation($"message {i}");

        Assert.Equal(1000, logger.Messages.Count);
        Assert.Equal("message 1499", logger.Messages[^1].Message);
        Assert.Equal("message 500", logger.Messages[0].Message);
    }

    /// <summary>Verifies that a negative <see cref="ConsoleLogger.MaxRetainedMessages" /> keeps every message.</summary>
    [Fact]
    public void LogMessage_WithNegativeCap_KeepsEverything()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true }, MaxRetainedMessages = -1 };

        for (int i = 0; i < 1500; i++)
            logger.LogInformation($"message {i}");

        Assert.Equal(1500, logger.Messages.Count);
    }

    /// <summary>Verifies that a cap of 0 keeps no written message.</summary>
    [Fact]
    public void LogMessage_WithCapOfZero_KeepsNoWrittenMessage()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true }, MaxRetainedMessages = 0 };

        logger.LogInformation("one").LogInformation("two");

        Assert.Empty(logger.Messages);
    }

    /// <summary>Verifies that trimming never removes a message that is not written yet, and that Flush writes it and then trims.</summary>
    [Fact]
    public void Flush_KeepsUnwrittenMessagesThroughATrimAndTrimsAfterWriting()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true }, MaxRetainedMessages = 1 };
        ConsoleLoggerMessageModel queued = new("info", "queued", (IEnumerable<string>)null);
        logger.Messages.Add(queued);

        logger.LogInformation("a").LogInformation("b");

        Assert.Contains(queued, logger.Messages);
        Assert.False(queued.Written);
        Assert.Equal(2, logger.Messages.Count);

        logger.Flush();

        // The queued message is now written and the oldest, so it is the one dropped.
        Assert.True(queued.Written);
        Assert.Single(logger.Messages);
        Assert.Equal("b", logger.Messages[0].Message);
    }

    /// <summary>Verifies that <see cref="ConsoleLogger.WriteAsync" /> trims written messages beyond the cap.</summary>
    [Fact]
    public async Task WriteAsync_TrimsWrittenMessagesBeyondTheCap()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true }, MaxRetainedMessages = 1 };
        logger.Messages.Add(new ConsoleLoggerMessageModel("info", "x", (IEnumerable<string>)null));
        logger.Messages.Add(new ConsoleLoggerMessageModel("info", "y", (IEnumerable<string>)null));

        await logger.WriteAsync();

        Assert.Single(logger.Messages);
        Assert.Equal("y", logger.Messages[0].Message);
    }

    /// <summary>Verifies that the prefix width tracks the longest part seen so far, even after earlier messages are trimmed away.</summary>
    [Fact]
    public void LogMessage_PrefixWidth_IsTheLongestPartPlusTwoAndSurvivesTrimming()
    {
        ConsoleLogger logger = new() { Configuration = { Quiet = true }, MaxRetainedMessages = 0 };

        logger.LogInformation("m", ["a"]);
        Assert.Equal(3, logger.Configuration.MaxPrefixLength);

        logger.LogInformation("m", ["longer-part"]);
        Assert.Equal(13, logger.Configuration.MaxPrefixLength);

        logger.LogInformation("m", ["a"]);
        Assert.Equal(13, logger.Configuration.MaxPrefixLength);
    }
}
