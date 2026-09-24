using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace for the console-logger-configuration-model tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleLoggerConfigurationModel" /> - construction
///     defaults, the level-to-color lookup (including a real bug fixed in this project), and the
///     fluent With* builders.
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
    ///     color registered for a known log level. This is a real bug found and fixed in this project:
    ///     the original condition was inverted (<c>is null or {Length: 0}</c> instead of
    ///     <c>is not (null or {Length: 0})</c>), so it always fell through to the "_" fallback color
    ///     for every real, non-empty level name, and only ever attempted the actual dictionary lookup
    ///     for a null/empty level (which, by definition, could never be found).
    /// </summary>
    [Fact]
    public void GetConsoleColor_ForKnownLevel_ReturnsRegisteredColor()
    {
        ConsoleLoggerConfigurationModel model = new();

        ConsoleColor color = model.GetConsoleColor("error");

        Assert.Equal(ConsoleColor.DarkRed, color);
    }

    /// <summary>Verifies that a different known level resolves to its own distinct registered color, not the fallback.</summary>
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
