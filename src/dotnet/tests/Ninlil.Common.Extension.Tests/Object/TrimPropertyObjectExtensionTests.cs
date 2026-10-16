using Ninlil.Common.Extension.Object;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Object;

/// <summary>
///     These tests trim string properties on the <c>SampleModel</c> test type through
///     <see cref="TrimPropertyObjectExtension" />, by name and by selector, including the null
///     fallback value and the exceptions for invalid property names.
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

        SampleModel empty = new() { Name = "" };
        TrimPropertyObjectExtension.TrimProperty(empty, nameof(SampleModel.Name), "fallback");
        Assert.Equal("fallback", empty.Name);
    }

    [Fact]
    public void TrimPropertyByNameUsesDefaultValueWhenCurrentValueIsOnlyWhitespace()
    {
        SampleModel instance = new() { Name = "   " };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name), "fallback");

        Assert.Equal("fallback", instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameWithoutADefaultTurnsWhitespaceIntoAnEmptyString()
    {
        SampleModel instance = new() { Name = "   " };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name));

        Assert.Equal(string.Empty, instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameWithoutADefaultLeavesNullAsNull()
    {
        SampleModel instance = new() { Name = null };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name));

        Assert.Null(instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameKeepsANonBlankValueEvenWhenADefaultIsGiven()
    {
        SampleModel instance = new() { Name = "  kept  " };

        TrimPropertyObjectExtension.TrimProperty(instance, nameof(SampleModel.Name), "fallback");

        Assert.Equal("kept", instance.Name);
    }

    [Fact]
    public void TrimPropertyByNameThrowsForNullPropertyName()
    {
        SampleModel instance = new();

        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentNullException for null and
        // ArgumentException for an empty string (see the next test).
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
