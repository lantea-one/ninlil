using System.Text.RegularExpressions;
using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     This class tests <see cref="LikeStringExtension" />.
/// </summary>
public class LikeStringExtensionTests
{
    [Fact]
    public void LikeReturnsFalseForNullOrEmptyInstance()
    {
        Assert.False(((string)null).Like("anything"));
        Assert.False("".Like("anything"));
    }

    [Fact]
    public void LikeMatchesAValidRegularExpressionDirectly()
    {
        Assert.True("hello world".Like(@"^hello\s\w+$"));
    }

    [Fact]
    public void LikeIsCaseInsensitiveByDefault()
    {
        Assert.True("HELLO".Like("hello"));
    }

    [Fact]
    public void LikeFallsBackToGlobConversionWhenTheRegexIsInvalid()
    {
        // "*.txt" is not valid regex syntax on its own (a leading, unquantifiable '*'), so this
        // exercises the ArgumentException catch/glob-conversion fallback path.
        Assert.True("report.txt".Like("*.txt"));
        Assert.False("report.csv".Like("*.txt"));
    }

    /// <summary>
    ///     "c?t" is coincidentally also a syntactically valid (if semantically different) regular
    ///     expression -- an optional literal 'c' followed by 't' -- so it always matches via the primary
    ///     regex path (e.g. it matches "coat" too, via the trailing "t") and never reaches the glob
    ///     fallback at all. A pattern that is only valid as a glob, like a leading bare "?" (an invalid
    ///     regex quantifier with nothing to repeat), is needed to actually exercise the fallback.
    /// </summary>
    [Fact]
    public void LikeGlobFallbackSupportsSingleCharacterWildcard()
    {
        Assert.True("cat".Like("?at"));
        Assert.False("boat".Like("?at"));
    }

    [Fact]
    public void LikeWithExplicitRegexOptionsHonorsThoseOptions()
    {
        Assert.False("HELLO".Like("hello", RegexOptions.None));
        Assert.True("HELLO".Like("hello", RegexOptions.IgnoreCase));
    }
}
