using System.Net;
using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     This class contains unit tests for <see cref="NetworkAclModel" />, which layers
///     allow/deny/remove semantics (a "!" deny prefix and a "false" remove sentinel) on top of a
///     bare <see cref="NetworkAddressModel" />.
/// </summary>
public class NetworkAclModelTests
{
    [Fact]
    public void Parses_PlainAddress_AsAllow()
    {
        var model = new NetworkAclModel("10.0.0.0/24");

        Assert.True(model.Allow);
        Assert.False(model.Deny);
        Assert.False(model.Remove);
        Assert.Equal(IPAddress.Parse("10.0.0.0"), model.Address.IpAddress);
        Assert.Equal(24, model.Address.CidrMask);
    }

    [Fact]
    public void Parses_DenyPrefixedAddress()
    {
        var model = new NetworkAclModel("!192.168.1.1");

        Assert.False(model.Allow);
        Assert.True(model.Deny);
        Assert.False(model.Remove);
        Assert.Equal(IPAddress.Parse("192.168.1.1"), model.Address.IpAddress);
    }

    [Fact]
    public void Parses_FalseSentinel_AsRemove()
    {
        var model = new NetworkAclModel("false");

        Assert.True(model.Remove);
        Assert.False(model.Allow);
        Assert.False(model.Deny);
        Assert.Null(model.Address);
        Assert.True(model.IsValid());
    }

    [Fact]
    public void ToString_Remove_IsFalseLiteral()
    {
        var model = new NetworkAclModel("false");

        Assert.Equal("false", model.ToString());
    }

    [Fact]
    public void ToString_Deny_IncludesPrefix()
    {
        var model = new NetworkAclModel("!10.0.0.0/24");

        Assert.Equal("!10.0.0.0/24", model.ToString());
    }

    [Fact]
    public void ToString_Allow_HasNoPrefix()
    {
        var model = new NetworkAclModel("10.0.0.0/24");

        Assert.Equal("10.0.0.0/24", model.ToString());
    }

    [Fact]
    public void ToString_CanSelectivelyOmitComponents()
    {
        var model = new NetworkAclModel("!10.0.0.0/24");

        Assert.Equal("10.0.0.0/24", model.ToString(false, true, true));
        Assert.Equal("!10.0.0.0", model.ToString(true, true, false));
    }

    [Fact]
    public void IsValid_EmptyModel_IsFalse()
    {
        var model = new NetworkAclModel();

        Assert.False(model.IsValid());
    }

    [Fact]
    public void TryParse_MalformedInput_ReturnsFalse()
    {
        bool success = StructuredStringModel.TryParse("!!not-valid", out NetworkAclModel model);

        Assert.False(success);
        Assert.Null(model);
    }

    [Fact]
    public void RoundTrips_ThroughToStringAndBack()
    {
        var model = new NetworkAclModel("!2001:db8::/32");

        var reparsed = new NetworkAclModel(model.ToString());

        Assert.Equal(model.Deny, reparsed.Deny);
        Assert.Equal(model.Address.IpAddress, reparsed.Address.IpAddress);
        Assert.Equal(model.Address.CidrMask, reparsed.Address.CidrMask);
    }
}
