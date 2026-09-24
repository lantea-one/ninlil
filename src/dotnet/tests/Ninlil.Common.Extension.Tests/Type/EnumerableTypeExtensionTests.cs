using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="EnumerableTypeExtension" />.
/// </summary>
public class EnumerableTypeExtensionTests
{
    [Fact]
    public void GetEnumerableElementTypeReturnsElementTypeForArray()
    {
        Assert.Equal(typeof(int), typeof(int[]).GetEnumerableElementType());
    }

    [Fact]
    public void GetEnumerableElementTypeReturnsElementTypeForList()
    {
        Assert.Equal(typeof(string), typeof(List<string>).GetEnumerableElementType());
    }

    [Fact]
    public void GetEnumerableElementTypeReturnsElementTypeForIEnumerable()
    {
        Assert.Equal(typeof(bool), typeof(IEnumerable<bool>).GetEnumerableElementType());
    }

    [Fact]
    public void GetEnumerableElementTypeReturnsNullForNonEnumerableType()
    {
        Assert.Null(typeof(int).GetEnumerableElementType());
    }
}
