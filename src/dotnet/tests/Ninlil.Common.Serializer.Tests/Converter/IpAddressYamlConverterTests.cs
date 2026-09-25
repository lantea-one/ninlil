using System.Net;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip <see cref="IPAddress" /> values through
///     <see cref="Serializer.Converter.IpAddressYamlConverter" />. A converter must read
///     <c>parser.Current</c> before calling <c>parser.MoveNext()</c>; the wrapper model has two
///     sibling properties so reading the wrong node, or leaving the parser out of step for the next
///     property, shows up as a wrong or missing value. The other YAML converter tests rely on the
///     same setup.
/// </summary>
public class IpAddressYamlConverterTests
{
    private class Wrapper
    {
        public IPAddress First { get; set; }
        public IPAddress Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingAddresses()
    {
        var wrapper = new Wrapper
        {
            First = IPAddress.Parse("10.0.0.1"),
            Second = IPAddress.Parse("10.0.0.2"),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        // Both values must land in the right properties; a positioning mistake would swap them,
        // throw, or leave one null.
        Assert.Equal(IPAddress.Parse("10.0.0.1"), result.First);
        Assert.Equal(IPAddress.Parse("10.0.0.2"), result.Second);
    }

    [Fact]
    public void RoundTrips_Ipv6Address()
    {
        var wrapper = new Wrapper { First = IPAddress.Parse("2001:db8::1"), Second = null };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(IPAddress.Parse("2001:db8::1"), result.First);
    }

    [Fact]
    public void Deserialize_MalformedAddress_ReturnsNull()
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("first: \"not-an-ip\"\nsecond: ~\n");

        Assert.Null(result.First);
        Assert.Null(result.Second);
    }
}
