using Ninlil.Common.Extension.Tuple;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Tuple;

/// <summary>
///     This class tests <see cref="TupleExtension" />.
/// </summary>
/// <remarks>
///     <see cref="Ninlil.Common.Extension.Type.IsTypeExtension.IsTuple" /> recognizes every real
///     1-through-8-element <c>Tuple&lt;...&gt;</c>/<c>ValueTuple&lt;...&gt;</c> arity, so the underlying
///     <c>ToStringValueObjectExtension.ToStringValue</c> switch's dedicated tuple branch (which formats
///     elements as a bare comma-separated list, e.g. <c>"(a,1)"</c>, via
///     <see cref="System.Runtime.CompilerServices.ITuple" />) correctly triggers for every multi-element
///     <see cref="Tuple{T1,T2}" />-and-larger instance, rather than falling through to the default
///     <see cref="System.ComponentModel.TypeConverter" />-based branch that would otherwise just call the
///     tuple's own built-in <see cref="object.ToString" /> (elements separated by <c>", "</c>).
/// </remarks>
public class TupleExtensionTests
{
    [Fact]
    public void ToConsoleStringOnNullTwoItemTupleReturnsLiteralNull()
    {
        Tuple<string, int> instance = null;

        Assert.Equal("null", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnTwoItemTupleJoinsElementsWithComma()
    {
        Tuple<string, int> instance = new("a", 1);

        Assert.Equal("(a,1)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnThreeItemTupleJoinsElementsWithComma()
    {
        Tuple<string, int, bool> instance = new("a", 1, true);

        Assert.Equal("(a,1,true)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnFourItemTupleJoinsElementsWithComma()
    {
        Tuple<int, int, int, int> instance = new(1, 2, 3, 4);

        Assert.Equal("(1,2,3,4)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnFiveItemTupleJoinsElementsWithComma()
    {
        Tuple<int, int, int, int, int> instance = new(1, 2, 3, 4, 5);

        Assert.Equal("(1,2,3,4,5)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnSixItemTupleJoinsElementsWithComma()
    {
        Tuple<int, int, int, int, int, int> instance = new(1, 2, 3, 4, 5, 6);

        Assert.Equal("(1,2,3,4,5,6)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnSevenItemTupleJoinsElementsWithComma()
    {
        Tuple<int, int, int, int, int, int, int> instance = new(1, 2, 3, 4, 5, 6, 7);

        Assert.Equal("(1,2,3,4,5,6,7)", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnEightItemRestTupleJoinsElementsWithComma()
    {
        Tuple<int, int, int, int, int, int, int, Tuple<int>> instance = new(
            1,
            2,
            3,
            4,
            5,
            6,
            7,
            new Tuple<int>(8)
        );

        Assert.Equal("(1,2,3,4,5,6,7,8)", instance.ToConsoleString());
    }
}
