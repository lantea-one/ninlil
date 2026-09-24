using Ninlil.Common.Console.Enum;
using Ninlil.Common.Console.Model;

// Define our namespace for the console-command-argument tests.
namespace Ninlil.Common.Console.Tests.Model;

/// <summary>
///     This test class exercises <see cref="ConsoleCommandArgument" /> - typed value retrieval and
///     the two shapes of <see cref="ConsoleCommandArgument.ToString" /> output (positional vs. named).
/// </summary>
public class ConsoleCommandArgumentTests
{
    /// <summary>Verifies that <see cref="ConsoleCommandArgument.GetValue(Type, string, Ninlil.Common.Serializer.Enum.SerializerFormatEnum)" /> converts the string value to the requested type.</summary>
    [Fact]
    public void GetValue_ConvertsToRequestedType()
    {
        ConsoleCommandArgument argument = new() { Value = "42" };

        object result = argument.GetValue(typeof(int));

        Assert.Equal(42, result);
    }

    /// <summary>Verifies the generic <see cref="ConsoleCommandArgument.GetValue{TValue}" /> overload.</summary>
    [Fact]
    public void GetValueGeneric_ConvertsToRequestedType()
    {
        ConsoleCommandArgument argument = new() { Value = "true" };

        bool result = argument.GetValue<bool>();

        Assert.True(result);
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgument.ToString" /> for a positional argument returns only the trimmed value.</summary>
    [Fact]
    public void ToString_ForPositionalArgument_ReturnsOnlyValue()
    {
        ConsoleCommandArgument argument = new() { Position = 0, Value = "  in.txt  " };

        Assert.Equal("in.txt", argument.ToString());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgument.ToString" /> for a long-named argument returns "--name value".</summary>
    [Fact]
    public void ToString_ForLongNamedArgument_ReturnsDashDashNameAndValue()
    {
        ConsoleCommandArgument argument = new()
        {
            Position = -1,
            Character = char.MinValue,
            Name = "name",
            Value = "John",
        };

        Assert.Equal("--name John", argument.ToString());
    }

    /// <summary>Verifies that <see cref="ConsoleCommandArgument.ToString" /> for a short-named argument returns "-x value".</summary>
    [Fact]
    public void ToString_ForShortNamedArgument_ReturnsDashCharacterAndValue()
    {
        ConsoleCommandArgument argument = new()
        {
            Position = -1,
            Character = 'a',
            Value = "30",
        };

        Assert.Equal("-a 30", argument.ToString());
    }

    /// <summary>Verifies the default state of a newly constructed argument.</summary>
    [Fact]
    public void Defaults_AreUnknownAndUnset()
    {
        ConsoleCommandArgument argument = new();

        Assert.Equal(char.MinValue, argument.Character);
        Assert.Equal(-1, argument.Position);
        Assert.Equal(ConsoleCommandArgumentType.Unknown, argument.Type);
        Assert.Null(argument.Name);
        Assert.Null(argument.Value);
    }
}
