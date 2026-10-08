using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This class covers <see cref="DhcpRawOptionRules" />: the reserved DHCPv4 tags, the 255-byte
///     DHCPv4 limit, DHCPv6 code 0 and the 65 535-byte DHCPv6 limit. Pure functions, no I/O.
/// </summary>
public class DhcpRawOptionRulesTests
{
    /// <summary>
    ///     This test proves each reserved DHCPv4 tag is rejected with a message naming it.
    /// </summary>
    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)52)]
    [InlineData((byte)53)]
    [InlineData((byte)82)]
    [InlineData((byte)255)]
    public void TryValidateDhcpv4_ForAReservedTag_IsRejected(byte tag)
    {
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv4(tag, 4, out string error));
        Assert.Contains(tag.ToString(), error);
    }

    /// <summary>
    ///     This test proves ordinary tags are accepted, including the edges 1 and 254.
    /// </summary>
    [Theory]
    [InlineData((byte)1)]
    [InlineData((byte)66)]
    [InlineData((byte)254)]
    public void TryValidateDhcpv4_ForAnOrdinaryTag_IsAccepted(byte tag) =>
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv4(tag, 4, out _));

    /// <summary>
    ///     This test proves 255 bytes pass and 256 bytes fail, with the limit in the message.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv4_At255And256Bytes_AcceptsThenRejects()
    {
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv4(66, 255, out _));
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv4(66, 256, out string error));
        Assert.Contains("255", error);
    }

    /// <summary>
    ///     This test proves DHCPv6 code 0 is rejected and code 1 accepted.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv6_ForCodeZero_IsRejected()
    {
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv6(0, 4, out string error));
        Assert.Contains("0", error);
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6(1, 4, out _));
    }

    /// <summary>
    ///     This test proves 65 535 bytes pass and 65 536 bytes fail for DHCPv6.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv6_At65535And65536Bytes_AcceptsThenRejects()
    {
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6(23, 65535, out _));
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv6(23, 65536, out string error));
        Assert.Contains("65535", error);
    }
}
