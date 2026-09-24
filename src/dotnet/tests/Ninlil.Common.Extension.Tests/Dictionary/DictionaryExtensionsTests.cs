using Ninlil.Common.Extension.Dictionary;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Dictionary;

/// <summary>
///     This class tests <see cref="DictionaryExtensions" />.
/// </summary>
public class DictionaryExtensionsTests
{
    [Fact]
    public void MergeOverwritesExistingKeysByDefault()
    {
        Dictionary<string, int> source = new() { ["a"] = 1, ["b"] = 2 };
        Dictionary<string, int> additions = new() { ["b"] = 20, ["c"] = 3 };

        Dictionary<string, int> result = source.Merge(additions);

        Assert.Same(source, result);
        Assert.Equal(1, result["a"]);
        Assert.Equal(20, result["b"]);
        Assert.Equal(3, result["c"]);
    }

    [Fact]
    public void MergeWithOverwriteFalseAddsNewKeysWithoutTouchingExisting()
    {
        Dictionary<string, int> source = new() { ["a"] = 1 };
        Dictionary<string, int> additions = new() { ["b"] = 2 };

        Dictionary<string, int> result = source.Merge(additions, false);

        Assert.Equal(1, result["a"]);
        Assert.Equal(2, result["b"]);
    }

    [Fact]
    public void MergeWithOverwriteFalseThrowsWhenKeyAlreadyExists()
    {
        Dictionary<string, int> source = new() { ["a"] = 1 };
        Dictionary<string, int> additions = new() { ["a"] = 2 };

        Assert.Throws<ArgumentException>(() => source.Merge(additions, false));
    }

    [Fact]
    public void MergeWithEmptyAdditionsReturnsSourceUnchanged()
    {
        Dictionary<string, int> source = new() { ["a"] = 1 };

        Dictionary<string, int> result = source.Merge([]);

        Assert.Single(result);
        Assert.Equal(1, result["a"]);
    }

    [Fact]
    public void ToConsoleStringOnNullReturnsLiteralNull()
    {
        Dictionary<string, int> instance = null;

        Assert.Equal("null", instance.ToConsoleString());
    }

    /// <summary>
    ///     The underlying <c>ToStringValueObjectExtension.DictionaryToString</c> helper reads the
    ///     dictionary through the non-generic <see cref="System.Collections.IDictionary" /> interface,
    ///     which every closed <see cref="Dictionary{TKey,TValue}" /> implements regardless of its actual
    ///     key/value types, so <c>DictionaryExtensions.ToConsoleString</c> renders every real-world
    ///     dictionary's actual entries.
    /// </summary>
    [Fact]
    public void ToConsoleStringOnPopulatedDictionaryRendersItsEntries()
    {
        Dictionary<string, int> instance = new() { ["a"] = 1 };

        string result = instance.ToConsoleString();

        Assert.Equal("{a:1}", result);
    }
}
