using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.String;

/// <summary>
///     These tests cover the three <see cref="SanitizeStringExtension" /> methods: quote removal,
///     brace stripping and parenthesis stripping, including null input.
/// </summary>
/// <remarks>
///     <c>SanitizeForValueTypingDictionary</c> and <c>SanitizeForValueTypingTuple</c> run
///     <c>TrimEnd</c>/<c>TrimStart</c> on the delimiter characters before the final
///     <c>.Trim()</c>. With whitespace outside the delimiters (e.g. <c>"  { a:1 }  "</c>), the
///     delimiters are not at the ends when those calls run, so they stay and only the whitespace is
///     removed. The tests below pin that behavior.
/// </remarks>
public class SanitizeStringExtensionTests
{
    /// <summary>
    ///     The surrounding quote pair is stripped once, before the unescaping loop, so a short quoted
    ///     value survives the default <c>depth</c> (13) iterations without throwing
    ///     <see cref="ArgumentOutOfRangeException" />.
    /// </summary>
    [Fact]
    public void SanitizeForValueTypingWithDefaultDepthUnwrapsARealisticShortQuotedString()
    {
        Assert.Equal("hello", "\"hello\"".SanitizeForValueTyping());
    }

    /// <summary>
    ///     Passing <c>depth: 1</c> shows quote removal for a single quote pair with only one unescaping
    ///     iteration.
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
    public void SanitizeForValueTyping_InsideSingleQuotes_UnescapesAnEscapedSingleQuote()
    {
        Assert.Equal("it's", "'it\\'s'".SanitizeForValueTyping());
    }

    /// <summary>
    ///     Only one quote pair is stripped: the inner pair of <c>"'x'"</c> belongs to the value.
    /// </summary>
    [Fact]
    public void SanitizeForValueTyping_WithTwoNestedQuotePairs_StripsOnlyTheOuterPair()
    {
        Assert.Equal("'x'", "\"'x'\"".SanitizeForValueTyping());
        Assert.Equal("\"x\"", "'\"x\"'".SanitizeForValueTyping());
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("'")]
    public void SanitizeForValueTyping_WithALoneQuoteCharacter_ReturnsItUnchanged(string value)
    {
        Assert.Equal(value, value.SanitizeForValueTyping());
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
    ///     See the class remarks: with whitespace outside the braces, the <c>{</c> and <c>}</c> stay and
    ///     only the outer whitespace is removed.
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
    ///     See the class remarks: with whitespace outside the parentheses, the <c>(</c> and <c>)</c>
    ///     stay and only the outer whitespace is removed.
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
