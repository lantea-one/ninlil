using Ninlil.Common.Extension.List;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.List;

/// <summary>
///     This class tests <see cref="ListExtension" />.
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
    ///     The underlying <c>ToStringValueObjectExtension.ListToString</c> helper enumerates through the
    ///     non-generic <see cref="System.Collections.IEnumerable" /> interface, which every array/list
    ///     implements regardless of its element type, so a <see cref="List{T}" /> of a value type (like
    ///     <see cref="int" />) stringifies its elements correctly instead of throwing.
    /// </summary>
    [Fact]
    public void ToConsoleStringOnValueTypeListJoinsElementsWithComma()
    {
        List<int> instance = [1, 2, 3];

        Assert.Equal("1,2,3", instance.ToConsoleString());
    }
}
