using Ninlil.Common.Console.Enum;

// Define our namespace.
namespace Ninlil.Common.Console.Tests.Enum;

/// <summary>
///     These tests check that <see cref="ConsoleCommandArgumentType" /> defines the expected
///     members with distinct values, since the parser branches on them.
/// </summary>
public class ConsoleCommandArgumentTypeTests
{
    /// <summary>
    ///     Verifies that every expected member is defined on the enum.
    /// </summary>
    [Fact]
    public void Enum_DefinesExpectedMembers()
    {
        var names = System.Enum.GetNames<ConsoleCommandArgumentType>();

        Assert.Contains(nameof(ConsoleCommandArgumentType.Unknown), names);
        Assert.Contains(nameof(ConsoleCommandArgumentType.Named), names);
        Assert.Contains(nameof(ConsoleCommandArgumentType.NestedVerb), names);
        Assert.Contains(nameof(ConsoleCommandArgumentType.Positional), names);
        Assert.Contains(nameof(ConsoleCommandArgumentType.Verb), names);
    }

    /// <summary>
    ///     Verifies that <see cref="ConsoleCommandArgumentType.Unknown" /> is the default (zero) value,
    ///     matching its <c>[DefaultValue]</c> attribute declaration.
    /// </summary>
    [Fact]
    public void Unknown_IsDefaultValue()
    {
        Assert.Equal(default, ConsoleCommandArgumentType.Unknown);
    }

    /// <summary>
    ///     Verifies that every enum member has a distinct underlying value.
    /// </summary>
    [Fact]
    public void Members_AreAllDistinct()
    {
        var values = System.Enum.GetValues<ConsoleCommandArgumentType>();

        Assert.Equal(values.Length, values.Distinct().Count());
    }
}
