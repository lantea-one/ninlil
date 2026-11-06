using System.Security.Authentication;
using Ninlil.Common.Extension.Enum;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Enum;

/// <summary>
///     These tests cover <see cref="EnumNameParser" /> with a plain enum and the flags enum
///     <see cref="SslProtocols" />: exact and case-insensitive names, and refusal of digit strings,
///     undefined numbers, masks, comma lists, whitespace and null.
/// </summary>
public class EnumNameParserTests
{
    /// <summary>A plain enum with a non-contiguous value, used as the non-flags sample.</summary>
    private enum Sample
    {
        Zero = 0,
        Five = 5,
    }

    /// <summary>A member name parses to that member and reports its declared name.</summary>
    [Fact]
    public void TryParseName_ForAMemberName_ReturnsTheMemberAndCanonicalName()
    {
        bool ok = EnumNameParser.TryParseName("Five", false, out Sample value, out string name);

        Assert.True(ok);
        Assert.Equal(Sample.Five, value);
        Assert.Equal("Five", name);
    }

    /// <summary>With case folding on, a differently cased name gives the declared name.</summary>
    [Fact]
    public void TryParseName_IgnoringCase_ReturnsTheDeclaredName()
    {
        bool ok = EnumNameParser.TryParseName("fIVE", true, out Sample value, out string name);

        Assert.True(ok);
        Assert.Equal(Sample.Five, value);
        Assert.Equal("Five", name);
    }

    /// <summary>With case folding off, a differently cased name is refused.</summary>
    [Fact]
    public void TryParseName_CaseSensitive_RefusesOtherCase()
    {
        Assert.False(EnumNameParser.TryParseName("five", false, out Sample _));
    }

    /// <summary>Digit strings, defined or not, and blank or null text are refused.</summary>
    [Theory]
    [InlineData("5")]
    [InlineData("0")]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData(" Five")]
    [InlineData("Five ")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseName_ForNonNames_IsRefused(string text)
    {
        Assert.False(EnumNameParser.TryParseName(text, true, out Sample value, out string name));
        Assert.Equal(default, value);
        Assert.Null(name);
    }

    /// <summary>A single flags member name parses.</summary>
    [Fact]
    public void TryParseName_ForAFlagsMemberName_Parses()
    {
        Assert.True(EnumNameParser.TryParseName("Tls13", true, out SslProtocols value));
        Assert.Equal(SslProtocols.Tls13, value);
    }

    /// <summary>A flags digit string, mask or comma list is refused.</summary>
    [Theory]
    [InlineData("3072")]
    [InlineData("12288")]
    [InlineData("Tls12, Tls13")]
    [InlineData("Tls12,Tls13")]
    public void TryParseName_ForAFlagsMaskOrList_IsRefused(string text)
    {
        Assert.False(EnumNameParser.TryParseName(text, true, out SslProtocols _));
    }

    /// <summary>The names list holds every declared member name.</summary>
    [Fact]
    public void GetNames_ListsTheMembers()
    {
        Assert.Equal("Zero, Five", EnumNameParser.GetNames<Sample>());
    }
}
