using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Type;

/// <summary>
///     This class tests <see cref="DefaultValueTypeExtension" />.
/// </summary>
public class DefaultValueTypeExtensionTests
{
    [Fact]
    public void GetDefaultValueOnIntReturnsZero()
    {
        Assert.Equal(0, typeof(int).GetDefaultValue());
    }

    [Fact]
    public void GetDefaultValueOnBoolReturnsFalse()
    {
        Assert.Equal(false, typeof(bool).GetDefaultValue());
    }

    [Fact]
    public void GetDefaultValueOnStringReturnsNull()
    {
        Assert.Null(typeof(string).GetDefaultValue());
    }

    [Fact]
    public void GetDefaultValueOnNullableIntReturnsNull()
    {
        Assert.Null(typeof(int?).GetDefaultValue());
    }

    [Fact]
    public void GetDefaultValueOnGuidReturnsEmptyGuid()
    {
        Assert.Equal(Guid.Empty, typeof(Guid).GetDefaultValue());
    }

    [Fact]
    public void GetDefaultValueGenericOnIntReturnsZero()
    {
        Assert.Equal(0, typeof(int).GetDefaultValue<int>());
    }

    [Fact]
    public void GetDefaultValueGenericOnStringReturnsNull()
    {
        Assert.Null(typeof(string).GetDefaultValue<string>());
    }
}
