using System.Text.RegularExpressions;
using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     These tests cover <see cref="LikeStringExtension.Like(string, string)" />: blank input,
///     regular-expression matches, case handling and the glob fallback for patterns that are not
///     valid regular expressions.
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
        // "*.txt" is not a valid regular expression (a leading '*' has nothing to repeat), so
        // this takes the glob fallback.
        Assert.True("report.txt".Like("*.txt"));
        Assert.False("report.csv".Like("*.txt"));
    }

    /// <summary>
    ///     Uses "?at" because a pattern like "c?t" is also a valid regular expression (an optional
    ///     'c' then 't') and would never reach the glob fallback. A leading "?" is not a valid
    ///     regular expression, so this pattern is read as a glob.
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
