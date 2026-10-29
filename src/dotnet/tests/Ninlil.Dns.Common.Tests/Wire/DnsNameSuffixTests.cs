using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="DnsName.Suffix" />, and the cached hash code and text of a
///     <see cref="DnsName" />, on in-memory names.
/// </summary>
public class DnsNameSuffixTests
{
    /// <summary>
    ///     This test checks that skipping no labels returns the same instance.
    /// </summary>
    [Fact]
    public void Suffix_SkippingNoLabels_ReturnsTheSameInstance()
    {
        DnsName name = DnsName.Parse("www.example.com");

        Assert.Same(name, name.Suffix(0));
    }

    /// <summary>
    ///     This test checks the suffix of a middle position against the same name built by
    ///     <see cref="DnsName.Parse(string)" />.
    /// </summary>
    [Theory]
    [InlineData("www.example.com", 1, "example.com")]
    [InlineData("www.example.com", 2, "com")]
    [InlineData("a.b.c.d.e", 3, "d.e")]
    [InlineData(@"a\.b.c\\d.example.com", 1, @"c\\d.example.com")]
    [InlineData(@"x.\200\255.example.com", 1, @"\200\255.example.com")]
    public void Suffix_InTheMiddle_EqualsTheSameNameParsed(string name, int skip, string expected)
    {
        DnsName suffix = DnsName.Parse(name).Suffix(skip);

        Assert.Equal(DnsName.Parse(expected), suffix);
        Assert.Equal(DnsName.Parse(expected).ToString(), suffix.ToString());
        Assert.Equal(DnsName.Parse(expected).WireLength, suffix.WireLength);
        Assert.Equal(DnsName.Parse(expected).GetHashCode(), suffix.GetHashCode());
    }

    /// <summary>
    ///     This test checks that skipping every label returns the root name.
    /// </summary>
    [Fact]
    public void Suffix_SkippingEveryLabel_ReturnsTheRoot()
    {
        DnsName name = DnsName.Parse("www.example.com");

        DnsName suffix = name.Suffix(name.Labels.Count);

        Assert.Same(DnsName.Root, suffix);
        Assert.True(suffix.IsRoot);
        Assert.Equal(1, suffix.WireLength);
        Assert.Equal(".", suffix.ToString());
    }

    /// <summary>
    ///     This test checks that the root name's own suffix is the root.
    /// </summary>
    [Fact]
    public void Suffix_OfTheRootSkippingNothing_IsTheRoot()
    {
        Assert.Same(DnsName.Root, DnsName.Root.Suffix(0));
    }

    /// <summary>
    ///     This test checks that a position outside <c>0..Count</c> throws.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Suffix_OutsideTheLabelRange_Throws(int skip)
    {
        DnsName name = DnsName.Parse("www.example.com");

        Assert.Throws<ArgumentOutOfRangeException>(() => name.Suffix(skip));
    }

    /// <summary>
    ///     This test checks that a suffix shares the label arrays of the name it came from
    ///     instead of copying the bytes.
    /// </summary>
    [Fact]
    public void Suffix_SharesTheLabelArraysOfTheOriginalName()
    {
        DnsName name = DnsName.Parse("www.example.com");

        DnsName suffix = name.Suffix(1);

        Assert.Same(name.Labels[1], suffix.Labels[0]);
        Assert.Same(name.Labels[2], suffix.Labels[1]);
    }

    /// <summary>
    ///     This test checks that a suffix of a suffix equals the matching suffix of the name.
    /// </summary>
    [Fact]
    public void Suffix_OfASuffix_EqualsTheSuffixOfTheName()
    {
        DnsName name = DnsName.Parse("a.b.c.d.example.com");

        Assert.Equal(name.Suffix(3), name.Suffix(1).Suffix(2));
        Assert.Same(name.Labels[5], name.Suffix(1).Suffix(2).Suffix(2).Labels[0]);
    }

    /// <summary>
    ///     This test checks that names equal under ASCII case folding have the same hash code.
    /// </summary>
    [Fact]
    public void GetHashCode_OfNamesDifferingOnlyInCase_IsTheSame()
    {
        DnsName upper = DnsName.Parse("www.Example.COM");
        DnsName lower = DnsName.Parse("www.example.com");

        Assert.Equal(lower, upper);
        Assert.Equal(lower.GetHashCode(), upper.GetHashCode());
    }

    /// <summary>
    ///     This test checks that a name used as a dictionary key is found by a differently cased
    ///     name and by a suffix of a longer name.
    /// </summary>
    [Fact]
    public void ADictionaryKeyedByName_FindsAMixedCaseNameAndASuffix()
    {
        Dictionary<DnsName, int> map = new() { [DnsName.Parse("example.com")] = 7 };

        Assert.Equal(7, map[DnsName.Parse("EXAMPLE.com")]);
        Assert.Equal(7, map[DnsName.Parse("www.Example.com").Suffix(1)]);
    }

    /// <summary>
    ///     This test checks that the hash code stays the same on every call.
    /// </summary>
    [Fact]
    public void GetHashCode_CalledTwice_ReturnsTheSameValue()
    {
        DnsName name = DnsName.Parse("www.example.com");

        Assert.Equal(name.GetHashCode(), name.GetHashCode());
    }

    /// <summary>
    ///     This test checks that <see cref="DnsName.ToString" /> returns one cached instance.
    /// </summary>
    [Fact]
    public void ToString_CalledTwice_ReturnsTheSameInstance()
    {
        DnsName name = DnsName.Parse("www.example.com");

        Assert.Same(name.ToString(), name.ToString());
    }

    /// <summary>
    ///     This test checks that the cached text keeps the escapes for dots, backslashes,
    ///     control bytes and bytes above 0x7E, and the final dot.
    /// </summary>
    [Fact]
    public void ToString_OfALabelWithSpecialBytes_EscapesThemAsBefore()
    {
        DnsName name = new([
            [(byte)'a', (byte)'.', (byte)'\\', 0x01, 0x7F, 0xFF],
            [(byte)'c'],
        ]);

        Assert.Equal(@"a\.\\\001\127\255.c.", name.ToString());
        Assert.Equal(".", DnsName.Root.ToString());
    }

    /// <summary>
    ///     This test checks the wire length of names with several labels and of the root.
    /// </summary>
    [Fact]
    public void WireLength_OfSeveralNames_CountsLengthOctetsContentAndTheRoot()
    {
        Assert.Equal(1, DnsName.Root.WireLength);
        Assert.Equal(17, DnsName.Parse("www.example.com").WireLength);
        Assert.Equal(255, DnsName.Parse(string.Join('.', Enumerable.Repeat("a", 127))).WireLength);
    }
}
