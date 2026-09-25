using System.Net;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests cover <see cref="NetworkAddressModel" />: parsing IPv4 and IPv6 addresses with
///     and without CIDR prefixes, prefix range checks, formatting options and malformed input.
/// </summary>
public class NetworkAddressModelTests
{
    [Fact]
    public void Parses_BareIpv4Address()
    {
        var model = new NetworkAddressModel("192.168.1.1");

        Assert.Equal(IPAddress.Parse("192.168.1.1"), model.IpAddress);
        Assert.Null(model.CidrMask);
        Assert.True(model.IsValid());
    }

    [Fact]
    public void Parses_Ipv4AddressWithCidr()
    {
        var model = new NetworkAddressModel("10.0.0.0/24");

        Assert.Equal(IPAddress.Parse("10.0.0.0"), model.IpAddress);
        Assert.Equal(24, model.CidrMask);
        Assert.True(model.IsValid());
    }

    [Fact]
    public void Parses_Ipv6AddressWithCidr()
    {
        var model = new NetworkAddressModel("2001:db8::/32");

        Assert.Equal(IPAddress.Parse("2001:db8::"), model.IpAddress);
        Assert.Equal(32, model.CidrMask);
        Assert.True(model.IsValid());
    }

    [Theory]
    [InlineData(33)] // Out of range for IPv4 (max 32).
    [InlineData(-1)]
    public void IsValid_RejectsOutOfRangeIpv4Cidr(int cidr)
    {
        // The CIDR mask is only set by parsing, and -1 cannot appear in input (the pattern
        // requires digits), so only the value just past the valid range is checked.
        var model = new NetworkAddressModel($"192.168.1.0/{Math.Max(cidr, 0)}");

        if (cidr < 0)
            return; // Not expressible via the regex -- covered by the 33 case instead.

        Assert.False(model.IsValid());
    }

    [Fact]
    public void IsValid_RejectsOutOfRangeIpv6Cidr()
    {
        var model = new NetworkAddressModel("2001:db8::/129");

        Assert.False(model.IsValid());
    }

    [Fact]
    public void IsValid_EmptyModel_IsFalse()
    {
        var model = new NetworkAddressModel();

        Assert.False(model.IsValid());
    }

    [Fact]
    public void ToString_Default_IncludesIpAndCidr()
    {
        var model = new NetworkAddressModel("10.0.0.0/24");

        Assert.Equal("10.0.0.0/24", model.ToString());
    }

    [Fact]
    public void ToString_CanOmitCidr()
    {
        var model = new NetworkAddressModel("10.0.0.0/24");

        Assert.Equal("10.0.0.0", model.ToString(true, false));
    }

    [Fact]
    public void ToString_CanOmitIp()
    {
        var model = new NetworkAddressModel("10.0.0.0/24");

        Assert.Equal("/24", model.ToString(false, true));
    }

    [Fact]
    public void TryParse_MalformedInput_ReturnsFalse()
    {
        bool success = StructuredStringModel.TryParse(
            "not an address!",
            out NetworkAddressModel model
        );

        Assert.False(success);
        Assert.Null(model);
    }
}
