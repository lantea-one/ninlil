using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     This class tests <see cref="SanitizeStringExtension" />.
/// </summary>
/// <remarks>
///     <c>SanitizeForValueTypingDictionary</c>/<c>SanitizeForValueTypingTuple</c> call
///     <c>TrimEnd('}'/')').TrimStart('{'/'(')</c> <em>before</em> the final <c>.Trim()</c>. When the input
///     has whitespace outside the delimiter characters (e.g. <c>"  { a:1 }  "</c>), the delimiter
///     characters are not actually at the string's start/end at the point <c>TrimEnd</c>/<c>TrimStart</c>
///     run, so neither call removes anything, and only the surrounding whitespace ends up trimmed -- the
///     braces/parentheses themselves are left in place. (This is a separate, narrower behavior from
///     <c>SanitizeForValueTyping</c>'s own quote-unwrapping loop below and is not part of this pass's fix
///     scope.)
/// </remarks>
public class SanitizeStringExtensionTests
{
    /// <summary>
    ///     The quote-unwrapping loop now strips the surrounding quote pair exactly once, outside the
    ///     loop, then only progressively unescapes backslash-escaped inner quotes on each subsequent
    ///     iteration -- so a realistic short quoted value no longer exhausts the string and throws
    ///     <see cref="ArgumentOutOfRangeException" /> before the default <c>depth</c> (13) iterations
    ///     complete.
    /// </summary>
    [Fact]
    public void SanitizeForValueTypingWithDefaultDepthUnwrapsARealisticShortQuotedString()
    {
        Assert.Equal("hello", "\"hello\"".SanitizeForValueTyping());
    }

    /// <summary>
    ///     Passing <c>depth: 1</c> demonstrates the quote-removal behavior for a single-quote-pair input
    ///     (only one iteration runs).
    /// </summary>
    [Fact]
    public void SanitizeForValueTypingWithDepthOneRemovesASimpleSurroundingDoubleQuotePair()
    {
        Assert.Equal("hello", "\"hello\"".SanitizeForValueTyping(1));
    }

    [Fact]
    public void SanitizeForValueTypingWithDepthOneRemovesASimpleSurroundingSingleQuotePair()
    {
        Assert.Equal("hello", "'hello'".SanitizeForValueTyping(1));
    }

    [Fact]
    public void SanitizeForValueTypingTrimsWhitespaceOnAnUnquotedString()
    {
        Assert.Equal("hello", "  hello  ".SanitizeForValueTyping());
    }

    [Fact]
    public void SanitizeForValueTypingReturnsNullForNullInput()
    {
        Assert.Null(((string)null).SanitizeForValueTyping());
    }

    [Fact]
    public void SanitizeForValueTypingDictionaryStripsSurroundingBraces()
    {
        Assert.Equal("a:1,b:2", "{a:1,b:2}".SanitizeForValueTypingDictionary());
    }

    /// <summary>
    ///     See the class-level remarks: whitespace outside the braces prevents <c>TrimEnd</c>/
    ///     <c>TrimStart</c> from ever reaching the actual <c>{</c>/<c>}</c> characters, so they are left
    ///     in place -- only the outer whitespace is removed.
    /// </summary>
    [Fact]
    public void SanitizeForValueTypingDictionaryLeavesBracesInPlaceWhenSurroundedByWhitespace()
    {
        Assert.Equal("{ a:1 }", "  { a:1 }  ".SanitizeForValueTypingDictionary());
    }

    [Fact]
    public void SanitizeForValueTypingDictionaryReturnsNullForNullInput()
    {
        Assert.Null(((string)null).SanitizeForValueTypingDictionary());
    }

    [Fact]
    public void SanitizeForValueTypingTupleStripsSurroundingParentheses()
    {
        Assert.Equal("1,2,3", "(1,2,3)".SanitizeForValueTypingTuple());
    }

    /// <summary>
    ///     See the class-level remarks: whitespace outside the parentheses prevents <c>TrimEnd</c>/
    ///     <c>TrimStart</c> from ever reaching the actual <c>(</c>/<c>)</c> characters, so they are left
    ///     in place -- only the outer whitespace is removed.
    /// </summary>
    [Fact]
    public void SanitizeForValueTypingTupleLeavesParenthesesInPlaceWhenSurroundedByWhitespace()
    {
        Assert.Equal("( 1,2 )", "  ( 1,2 )  ".SanitizeForValueTypingTuple());
    }

    [Fact]
    public void SanitizeForValueTypingTupleReturnsNullForNullInput()
    {
        Assert.Null(((string)null).SanitizeForValueTypingTuple());
    }
}
