using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     This class tests <see cref="TrimPropertyObjectExtension" />.
/// </summary>
public class TrimPropertyObjectExtensionTests
{
    [Fact]
    public void TrimPropertyByNameTrimsTheCurrentValue()
    {
        SampleModel instance = new() { Name = "  padded  " };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name));

        Assert.Equal("padded", instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameUsesDefaultValueWhenCurrentValueIsNullOrEmpty()
    {
        SampleModel instance = new() { Name = null };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name), "fallback");

        Assert.Equal("fallback", instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameThrowsForNullPropertyName()
    {
        SampleModel instance = new();

        // ArgumentException.ThrowIfNullOrEmpty throws the more specific ArgumentNullException for a
        // null argument, and the base ArgumentException for an empty one -- see the sibling test below.
        Assert.Throws<ArgumentNullException>(() =>
            TrimPropertyObjectExtension.TrimProperty(instance, (string)null)
        );
    }

    [Fact]
    public void TrimPropertyByNameThrowsForEmptyPropertyName()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentException>(() =>
            TrimPropertyObjectExtension.TrimProperty(instance, "")
        );
    }

    [Fact]
    public void TrimPropertyByNameThrowsForUnknownPropertyName()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentNullException>(() =>
            TrimPropertyObjectExtension.TrimProperty(instance, "DoesNotExist")
        );
    }

    [Fact]
    public void TrimPropertyByNameThrowsForANonStringProperty()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentException>(() =>
            TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Number))
        );
    }

    [Fact]
    public void TrimPropertyByNameThrowsForAReadOnlyProperty()
    {
        SampleModel instance = new();

        Assert.Throws<ArgumentException>(() =>
            TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.FixedValue))
        );
    }

    [Fact]
    public void TrimPropertyBySelectorTrimsTheCurrentValue()
    {
        SampleModel instance = new() { Name = "  selector-trimmed  " };

        instance.TrimProperty(m => m.Name);

        Assert.Equal("selector-trimmed", instance.Name);
    }
}
