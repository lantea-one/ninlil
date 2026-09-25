using System.Net.NetworkInformation;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip MAC addresses, null and malformed input through
///     <see cref="Serializer.Converter.PhysicalAddressJsonConverter" />.
/// </summary>
public class PhysicalAddressJsonConverterTests
{
    private class Wrapper
    {
        public PhysicalAddress Address { get; set; }
    }

    [Fact]
    public void RoundTrips_MacAddress()
    {
        var wrapper = new Wrapper { Address = PhysicalAddress.Parse("00-11-22-33-44-55") };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal(PhysicalAddress.Parse("00-11-22-33-44-55"), result.Address);
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
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Address":"not-a-mac"}""");

        Assert.Null(result.Address);
    }
}
