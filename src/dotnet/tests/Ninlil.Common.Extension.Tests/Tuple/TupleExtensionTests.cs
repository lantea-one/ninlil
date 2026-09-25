using Ninlil.Common.Extension.Tuple;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Tuple;

/// <summary>
///     These tests check <see cref="TupleExtension" /> output for a null tuple and for tuples of
///     two to eight elements.
/// </summary>
/// <remarks>
///     <see cref="Ninlil.Common.Extension.Type.IsTypeExtension.IsTuple" /> recognizes every
///     <c>Tuple&lt;...&gt;</c> and <c>ValueTuple&lt;...&gt;</c> arity, so
///     <c>ToStringValueObjectExtension.ToStringValue</c> takes its tuple branch, which formats
///     elements through <see cref="System.Runtime.CompilerServices.ITuple" /> as <c>"(a,1)"</c>.
///     Otherwise the tuple's own <see cref="object.ToString" /> would separate elements with
///     <c>", "</c>.
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
