using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Enum;

/// <summary>
///     These tests cover <see cref="ConsoleLoggerLogFormatEnum" />: the six log formats, including
///     the Yaml/Yml pair. <see cref="ConsoleLoggerMessageModel" /> branches on these values, so the
///     tests pin them.
/// </summary>
public class ConsoleLoggerLogFormatEnumTests
{
    /// <summary>
    ///     Verifies the numeric value of each member, so reordering the enum is caught.
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
    ///     Verifies that Yaml and Yml are separate members with different values, not one value under
    ///     two names.
    /// </summary>
    [Fact]
    public void Yaml_And_Yml_AreDistinctMembers()
    {
        Assert.NotEqual(ConsoleLoggerLogFormatEnum.Yaml, ConsoleLoggerLogFormatEnum.Yml);
    }
}
