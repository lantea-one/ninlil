using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     This class tests <see cref="SetPropertyObjectExtension" />.
/// </summary>
public class SetPropertyObjectExtensionTests
{
    [Fact]
    public void SetPropertyByNameSetsTheValueAndReturnsTheInstance()
    {
        SampleModel instance = new();

        SampleModel result = instance.SetProperty(nameof(SampleModel.Name), "hello");

        Assert.Same(instance, result);
        Assert.Equal("hello", result.Name);
    }

    [Fact]
    public void SetPropertyByNameTrimsTheProvidedPropertyName()
    {
        SampleModel instance = new();

        instance.SetProperty("  Name  ", "trimmed-name");

        Assert.Equal("trimmed-name", instance.Name);
    }

    [Fact]
    public void SetPropertyByNameThrowsForNullPropertyName()
    {
        SampleModel instance = new();

        // ArgumentException.ThrowIfNullOrEmpty throws the more specific ArgumentNullException for a
        // null argument, and the base ArgumentException for an empty one -- see the sibling test below.
        Assert.Throws<ArgumentNullException>(() => instance.SetProperty((string)null, "value"));
    }

    [Fact]
    public void SetPropertyByNameThrowsForEmptyPropertyName()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentException>(() => instance.SetProperty("", "value"));
    }

    [Fact]
    public void SetPropertyByNameThrowsForUnknownPropertyName()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentNullException>(() => instance.SetProperty("DoesNotExist", "value"));
    }

    [Fact]
    public void SetPropertyByNameThrowsForReadOnlyProperty()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentException>(() =>
            instance.SetProperty(nameof(SampleModel.FixedValue), "new-value")
        );
    }

    [Fact]
    public void SetPropertyBySelectorSetsTheValueAndReturnsTheInstance()
    {
        SampleModel instance = new();

        SampleModel result = instance.SetProperty(m => m.Number, 42);

        Assert.Same(instance, result);
        Assert.Equal(42, result.Number);
    }
}
