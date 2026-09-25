using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     These tests cover <see cref="ConsoleLoggerConfigurationModel" />: constructor defaults, the
///     level-to-color lookup and the fluent <c>With*</c> setters.
/// </summary>
public class ConsoleLoggerConfigurationModelTests
{
    /// <summary>Verifies the default constructor's initial state.</summary>
    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        ConsoleLoggerConfigurationModel model = new();

        Assert.False(model.Debug);
        Assert.Equal(ConsoleLoggerLogFormatEnum.Table, model.Format);
        Assert.False(model.Quiet);
        Assert.False(model.CenterAlign);
        Assert.NotNull(model.Levels);
        Assert.NotNull(model.Prefix);
    }

    /// <summary>Verifies that the format/prefix constructor applies the given format and prefix parts.</summary>
    [Fact]
    public void FormatAndPrefixConstructor_AppliesFormatAndPrefix()
    {
        ConsoleLoggerConfigurationModel model = new(ConsoleLoggerLogFormatEnum.Json, ["service"]);

        Assert.Equal(ConsoleLoggerLogFormatEnum.Json, model.Format);
        Assert.Contains("service", model.Prefix);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleLoggerConfigurationModel.GetConsoleColor" /> returns the
    ///     color registered for a known log level rather than the "_" fallback. A non-empty level must
    ///     be looked up in the dictionary; only a blank level goes straight to the fallback.
    /// </summary>
    [Fact]
    public void GetConsoleColor_ForKnownLevel_ReturnsRegisteredColor()
    {
        ConsoleLoggerConfigurationModel model = new();

        ConsoleColor color = model.GetConsoleColor("error");

        Assert.Equal(ConsoleColor.DarkRed, color);
    }

    /// <summary>Verifies that two known levels resolve to their different registered colors, not the fallback.</summary>
    [Fact]
    public void GetConsoleColor_ForDifferentKnownLevel_ReturnsItsOwnColor()
    {
        ConsoleLoggerConfigurationModel model = new();

        ConsoleColor infoColor = model.GetConsoleColor("info");
        ConsoleColor errorColor = model.GetConsoleColor("error");

        Assert.Equal(ConsoleColor.DarkBlue, infoColor);
        Assert.Equal(ConsoleColor.DarkRed, errorColor);
        Assert.NotEqual(infoColor, errorColor);
    }

    /// <summary>Verifies that an unknown level falls back to the "_" default color.</summary>
    [Fact]
    public void GetConsoleColor_ForUnknownLevel_ReturnsFallbackColor()
    {
        ConsoleLoggerConfigurationModel model = new();

        ConsoleColor color = model.GetConsoleColor("totally-unknown-level");

        Assert.Equal(ConsoleColor.DarkGray, color);
    }

    /// <summary>Verifies that a null level falls back to the "_" default color rather than throwing.</summary>
    [Fact]
    public void GetConsoleColor_ForNullLevel_ReturnsFallbackColor()
    {
        ConsoleLoggerConfigurationModel model = new();

        ConsoleColor color = model.GetConsoleColor(null);

        Assert.Equal(ConsoleColor.DarkGray, color);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithCenterAlignment" /> sets the flag (default true).</summary>
    [Fact]
    public void WithCenterAlignment_DefaultsToTrue()
    {
        IConsoleLoggerConfigurationModel model =
            new ConsoleLoggerConfigurationModel().WithCenterAlignment();

        Assert.True(model.CenterAlign);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithDebug" /> sets the flag (default true).</summary>
    [Fact]
    public void WithDebug_DefaultsToTrue()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithDebug();

        Assert.True(model.Debug);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithFile" /> resolves and sets a full path.</summary>
    [Fact]
    public void WithFile_ResolvesFullPath()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithFile(
            "log.txt"
        );

        Assert.True(Path.IsPathRooted(model.File));
        Assert.EndsWith("log.txt", model.File);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithFile" /> with a null/empty path is a no-op.</summary>
    [Fact]
    public void WithFile_WithNullOrEmpty_IsNoOp()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithFile(
            null
        );

        Assert.Null(model.File);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithFormat" /> sets the format.</summary>
    [Fact]
    public void WithFormat_SetsFormat()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithFormat(
            ConsoleLoggerLogFormatEnum.Xml
        );

        Assert.Equal(ConsoleLoggerLogFormatEnum.Xml, model.Format);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithLevel" /> registers a new/overridden level color.</summary>
    [Fact]
    public void WithLevel_RegistersColorForLevel()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithLevel(
            "custom",
            ConsoleColor.Magenta
        );

        Assert.Equal(ConsoleColor.Magenta, model.GetConsoleColor("custom"));
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithPrefix" /> replaces the prefix model.</summary>
    [Fact]
    public void WithPrefix_ReplacesPrefixModel()
    {
        IConsoleLoggerPrefixModel prefix = new ConsoleLoggerPrefixModel().WithPart("service-a");

        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithPrefix(
            prefix
        );

        Assert.Contains("service-a", model.Prefix);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithPrefix" /> with a null prefix falls back to an empty prefix model.</summary>
    [Fact]
    public void WithPrefix_WithNull_FallsBackToEmptyPrefixModel()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithPrefix(
            null
        );

        Assert.NotNull(model.Prefix);
        Assert.Empty(model.Prefix);
    }

    /// <summary>Verifies that <see cref="ConsoleLoggerConfigurationModel.WithQuiet" /> sets the flag (default true).</summary>
    [Fact]
    public void WithQuiet_DefaultsToTrue()
    {
        IConsoleLoggerConfigurationModel model = new ConsoleLoggerConfigurationModel().WithQuiet();

        Assert.True(model.Quiet);
    }
}
