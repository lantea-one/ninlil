using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Definition;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     These tests cover how <see cref="UpstreamForwarderDefinitionParser" /> splits the host and
///     port of address-literal definitions, IPv6 literals in particular (RFC 3986 §3.2.2: an IPv6
///     literal with a port is bracketed). They call the public <c>Parse</c> method with literals
///     only, so no host name is resolved.
/// </summary>
public sealed class UpstreamForwarderDefinitionParserTests
{
    /// <summary>
    ///     This method checks that a literal definition yields the expected protocol, address and
    ///     port.
    /// </summary>
    [Theory]
    [InlineData(
        "2606:4700:4700::1111",
        UpstreamForwarderProtocolEnum.Plain,
        "2606:4700:4700::1111",
        53
    )]
    [InlineData("2001:db8::1:53", UpstreamForwarderProtocolEnum.Plain, "2001:db8::1:53", 53)]
    [InlineData("tls://2001:db8::1", UpstreamForwarderProtocolEnum.Tls, "2001:db8::1", 853)]
    [InlineData(
        "quic://2001:db8::1:853",
        UpstreamForwarderProtocolEnum.Quic,
        "2001:db8::1:853",
        853
    )]
    [InlineData("::ffff:192.0.2.1", UpstreamForwarderProtocolEnum.Plain, "::ffff:192.0.2.1", 53)]
    [InlineData("[2001:db8::1]:5353", UpstreamForwarderProtocolEnum.Plain, "2001:db8::1", 5353)]
    [InlineData("[2001:db8::1]", UpstreamForwarderProtocolEnum.Plain, "2001:db8::1", 53)]
    [InlineData("tls://[2001:db8::1]:853", UpstreamForwarderProtocolEnum.Tls, "2001:db8::1", 853)]
    [InlineData("1.1.1.1:5353", UpstreamForwarderProtocolEnum.Plain, "1.1.1.1", 5353)]
    [InlineData("1.1.1.1", UpstreamForwarderProtocolEnum.Plain, "1.1.1.1", 53)]
    public void Parse_AnAddressLiteral_YieldsProtocolAddressAndPort(
        string definition,
        UpstreamForwarderProtocolEnum protocol,
        string address,
        int port
    )
    {
        UpstreamForwarderDefinition parsed = new UpstreamForwarderDefinitionParser().Parse(
            definition
        );

        Assert.Equal(protocol, parsed.Protocol);
        Assert.Equal(IPAddress.Parse(address), parsed.Address);
        Assert.Equal(port, parsed.Port);
    }

    /// <summary>
    ///     This method checks that text after the closing bracket other than <c>:port</c> is
    ///     rejected rather than ignored.
    /// </summary>
    [Fact]
    public void Parse_TextAfterTheClosingBracket_ThrowsFormatException()
    {
        FormatException exception = Assert.Throws<FormatException>(() =>
            new UpstreamForwarderDefinitionParser().Parse("[2001:db8::1]x")
        );

        Assert.Contains("':port'", exception.Message);
    }

    /// <summary>
    ///     This method checks that an invalid unbracketed IPv6-looking string is rejected with a
    ///     message that names the bracket form.
    /// </summary>
    [Fact]
    public void Parse_AnInvalidUnbracketedIPv6Text_ThrowsFormatExceptionNamingTheBracketForm()
    {
        FormatException exception = Assert.Throws<FormatException>(() =>
            new UpstreamForwarderDefinitionParser().Parse("2001:db8::zz")
        );

        Assert.Contains("[address]:port", exception.Message);
    }

    /// <summary>
    ///     This method checks that a bracket that is never closed is rejected.
    /// </summary>
    [Fact]
    public void Parse_AnUnclosedBracket_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() =>
            new UpstreamForwarderDefinitionParser().Parse("[2001:db8::1")
        );
    }
}
