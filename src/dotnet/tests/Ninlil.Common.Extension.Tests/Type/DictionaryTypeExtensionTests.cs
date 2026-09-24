using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="DictionaryTypeExtension" />.
/// </summary>
public class DictionaryTypeExtensionTests
{
    [Fact]
    public void GetDictionaryKeyTypeReturnsTheKeyType()
    {
        Assert.Equal(typeof(string), typeof(Dictionary<string, int>).GetDictionaryKeyType());
    }

    [Fact]
    public void GetDictionaryValueTypeReturnsTheValueType()
    {
        Assert.Equal(typeof(int), typeof(Dictionary<string, int>).GetDictionaryValueType());
    }

    [Fact]
    public void GetDictionaryKeyTypeReturnsNullForNonDictionaryType()
    {
        Assert.Null(typeof(List<int>).GetDictionaryKeyType());
    }

    [Fact]
    public void GetDictionaryValueTypeReturnsNullForNonDictionaryType()
    {
        Assert.Null(typeof(List<int>).GetDictionaryValueType());
    }
}
