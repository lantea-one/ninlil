using Ninlil.Common.Extension.List;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.List;

/// <summary>
///     These tests check <see cref="ListExtension.ToConsoleString{TValue}" /> output for null,
///     empty, string and value-type lists.
/// </summary>
public class ListExtensionTests
{
    [Fact]
    public void ToConsoleStringOnNullReturnsLiteralNull()
    {
        List<string> instance = null;

        Assert.Equal("null", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnEmptyListReturnsEmptyBraces()
    {
        List<string> instance = [];

        Assert.Equal("", instance.ToConsoleString());
    }

    [Fact]
    public void ToConsoleStringOnPopulatedListJoinsElementsWithComma()
    {
        List<string> instance = ["one", "two", "three"];

        string result = instance.ToConsoleString();

        Assert.Equal("one,two,three", result);
    }

    /// <summary>
    ///     <c>ToStringValueObjectExtension.ListToString</c> enumerates the non-generic
    ///     <see cref="System.Collections.IEnumerable" />, so a <see cref="List{T}" /> of a value type
    ///     (like <see cref="int" />) is converted instead of throwing.
    /// </summary>
    [Fact]
    public void ToConsoleStringOnValueTypeListJoinsElementsWithComma()
    {
        List<int> instance = [1, 2, 3];

        Assert.Equal("1,2,3", instance.ToConsoleString());
    }
}
