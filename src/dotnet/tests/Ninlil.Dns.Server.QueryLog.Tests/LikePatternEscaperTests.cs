using Ninlil.Dns.Data.EntityFrameworkCore.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests;

/// <summary>
///     This class tests the pure string logic of <c>LikePatternEscaper</c>, which
///     <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.DnsQueryLogRepository" /> uses
///     to match a search term literally. The database behaviour is covered by the search tests on
///     each engine.
/// </summary>
public sealed class LikePatternEscaperTests
{
    [Theory]
    [InlineData("example", "example")]
    [InlineData("_dmarc", "\\_dmarc")]
    [InlineData("100%", "100\\%")]
    [InlineData("a[b]", "a\\[b]")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("\\%_[", "\\\\\\%\\_\\[")]
    [InlineData("", "")]
    public void Escape_SpecialCharacters_AreEscapedWithABackslash(string term, string expected) =>
        Assert.Equal(expected, LikePatternEscaper.Escape(term));

    [Fact]
    public void Contains_Term_IsWrappedInWildcardsAfterEscaping() =>
        Assert.Equal("%a\\_b%", LikePatternEscaper.Contains("a_b"));

    [Fact]
    public void EscapeCharacter_IsASingleBackslash() =>
        Assert.Equal("\\", LikePatternEscaper.EscapeCharacter);
}
