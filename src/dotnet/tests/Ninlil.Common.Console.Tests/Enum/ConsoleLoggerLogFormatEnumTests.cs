using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace for the console-logger-log-format enum tests.
namespace Ninlil.Common.Console.Tests.Enum;

/// <summary>
///     This test class exercises <see cref="ConsoleLoggerLogFormatEnum" /> - the six supported log
///     output formats (including the Yaml/Yml alias pair), since
///     <see cref="ConsoleLoggerMessageModel" />'s formatting logic branches directly on these values.
/// </summary>
public class ConsoleLoggerLogFormatEnumTests
{
    /// <summary>
    ///     Verifies the expected numeric ordering of the enum's members, since some callers rely on the
    ///     underlying integral value (e.g. serialization round-tripping).
    /// </summary>
    [Fact]
    public void Members_HaveExpectedNumericValues()
    {
        Assert.Equal(0, (int)ConsoleLoggerLogFormatEnum.Text);
        Assert.Equal(1, (int)ConsoleLoggerLogFormatEnum.Json);
        Assert.Equal(2, (int)ConsoleLoggerLogFormatEnum.Table);
        Assert.Equal(3, (int)ConsoleLoggerLogFormatEnum.Xml);
        Assert.Equal(4, (int)ConsoleLoggerLogFormatEnum.Yaml);
        Assert.Equal(5, (int)ConsoleLoggerLogFormatEnum.Yml);
    }

    /// <summary>
    ///     Verifies that Yaml and Yml are deliberately distinct members (an alias pair with different
    ///     underlying values), not the same value under two names.
    /// </summary>
    [Fact]
    public void Yaml_And_Yml_AreDistinctMembers()
    {
        Assert.NotEqual(ConsoleLoggerLogFormatEnum.Yaml, ConsoleLoggerLogFormatEnum.Yml);
    }
}
