using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This class covers <see cref="DhcpRawOptionRules" />: the reserved DHCPv4 tags, the 255-byte
///     DHCPv4 limit, DHCPv6 code 0, the DHCPv6 codes the engine writes itself, the 65 535-byte
///     DHCPv6 limit and the value rules for DHCPv4 tags 66 and 67 and DHCPv6 code 24. Pure functions, no I/O.
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
    ///     This test proves DHCPv6 code 0 is rejected and code 7 (Preference) accepted. Code 1 is no
    ///     longer the accepted example because it is the engine-written Client Identifier.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv6_ForCodeZero_IsRejected()
    {
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv6(0, 4, out string error));
        Assert.Contains("0", error);
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6(7, 4, out _));
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

    /// <summary>
    ///     This test proves every engine-written DHCPv6 code is rejected with a message naming it.
    /// </summary>
    [Theory]
    [InlineData((ushort)1, "Client Identifier")]
    [InlineData((ushort)2, "Server Identifier")]
    [InlineData((ushort)3, "IA_NA")]
    [InlineData((ushort)4, "IA_TA")]
    [InlineData((ushort)5, "IA Address")]
    [InlineData((ushort)6, "Option Request")]
    [InlineData((ushort)8, "Elapsed Time")]
    [InlineData((ushort)9, "Relay Message")]
    [InlineData((ushort)11, "Authentication")]
    [InlineData((ushort)13, "Status Code")]
    [InlineData((ushort)14, "Rapid Commit")]
    [InlineData((ushort)18, "Interface-ID")]
    [InlineData((ushort)19, "Reconfigure Message")]
    [InlineData((ushort)20, "Reconfigure Accept")]
    [InlineData((ushort)25, "IA_PD")]
    [InlineData((ushort)26, "IA Prefix")]
    [InlineData((ushort)37, "Relay Agent Remote-ID")]
    [InlineData((ushort)39, "Client FQDN")]
    public void TryValidateDhcpv6_ForAnEngineWrittenCode_IsRejected(ushort code, string name)
    {
        Assert.False(DhcpRawOptionRules.TryValidateDhcpv6(code, 4, out string error));
        Assert.Contains($"Option code {code} ({name})", error);
        Assert.Contains("set by the server itself", error);
    }

    /// <summary>
    ///     This test proves Preference (7), DNS servers (23) and Domain Search List (24) stay
    ///     allowed as custom options.
    /// </summary>
    [Theory]
    [InlineData((ushort)7)]
    [InlineData((ushort)23)]
    [InlineData((ushort)24)]
    [InlineData((ushort)59)]
    public void TryValidateDhcpv6_ForAnOperatorSettableCode_IsAccepted(ushort code) =>
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6(code, 4, out _));

    /// <summary>
    ///     This test proves a TFTP Server Name or Bootfile Name made of text passes and one made of
    ///     the 4 octets of an address fails.
    /// </summary>
    [Theory]
    [InlineData((byte)66)]
    [InlineData((byte)67)]
    public void TryValidateDhcpv4Value_ForTextTags_AcceptsTextAndRejectsBinary(byte tag)
    {
        Assert.True(
            DhcpRawOptionRules.TryValidateDhcpv4Value(
                tag,
                System.Text.Encoding.ASCII.GetBytes("192.0.2.1"),
                out _
            )
        );
        Assert.False(
            DhcpRawOptionRules.TryValidateDhcpv4Value(
                tag,
                [0xC0, 0x00, 0x02, 0x01],
                out string error
            )
        );
        Assert.Contains(tag.ToString(), error);
    }

    /// <summary>
    ///     This test proves a tag without a value rule accepts any bytes.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv4Value_ForAnotherTag_AcceptsAnyBytes() =>
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv4Value(6, [0xC0, 0x00, 0x02, 0x01], out _));

    /// <summary>
    ///     This test proves a Domain Search List in wire form passes and the text bytes of a name
    ///     fail (the first byte is read as a label length that runs past the end).
    /// </summary>
    [Fact]
    public void TryValidateDhcpv6Value_ForTheDomainSearchList_ChecksTheWireForm()
    {
        byte[] wire = [.. "\u0007example\u0003com"u8, 0];
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6Value(24, wire, out _));
        Assert.False(
            DhcpRawOptionRules.TryValidateDhcpv6Value(
                24,
                System.Text.Encoding.ASCII.GetBytes("example.com"),
                out string error
            )
        );
        Assert.Contains("24", error);
    }

    /// <summary>
    ///     This test proves a code without a value rule accepts any bytes.
    /// </summary>
    [Fact]
    public void TryValidateDhcpv6Value_ForAnotherCode_AcceptsAnyBytes() =>
        Assert.True(DhcpRawOptionRules.TryValidateDhcpv6Value(23, [1, 2, 3], out _));
}
