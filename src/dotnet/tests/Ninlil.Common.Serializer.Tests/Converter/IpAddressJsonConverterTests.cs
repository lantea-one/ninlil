using System.Net;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.IpAddressJsonConverter" />,
///     verifying round-trip JSON serialization of <see cref="IPAddress" /> values, including
///     IPv4, IPv6, null, and malformed input.
/// </summary>
public class IpAddressJsonConverterTests
{
    /// <summary>
    ///     This model wraps an <see cref="IPAddress" /> property so the converter, which
    ///     <see cref="JsonSerializer" /> registers for every call, runs through the normal entry points.
    /// </summary>
    private class Wrapper
    {
        public IPAddress Address { get; set; }
    }

    [Fact]
    public void RoundTrips_Ipv4Address()
    {
        var wrapper = new Wrapper { Address = IPAddress.Parse("192.168.1.1") };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal(IPAddress.Parse("192.168.1.1"), result.Address);
    }

    [Fact]
    public void RoundTrips_Ipv6Address()
    {
        var wrapper = new Wrapper { Address = IPAddress.Parse("::1") };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal(IPAddress.Parse("::1"), result.Address);
    }

    [Fact]
    public void RoundTrips_NullAddress()
    {
        var wrapper = new Wrapper { Address = null };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Null(result.Address);
    }

    [Fact]
    public void Deserialize_MalformedAddress_ReturnsNull()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Address":"not-an-ip"}""");

        Assert.Null(result.Address);
    }
}
