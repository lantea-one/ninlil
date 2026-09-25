using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     These tests call <see cref="EnumerableTypeExtension.GetEnumerableElementType" /> on arrays,
///     lists, <see cref="IEnumerable{T}" /> and a scalar type.
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
