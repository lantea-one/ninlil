using System.Net;
using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.IpAddressYamlConverter" />.
///     These tests specifically lock in the fix for the parser-positioning bug where the converter
///     used to call <c>parser.MoveNext()</c> before reading <c>parser.Current</c>, which read the
///     wrong (already-advanced-past) scalar node. A wrapper model with two sibling properties is
///     used so a positioning bug -- reading the wrong value or leaving the parser out of sync for
///     the next property -- would be observable.
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

        // Both values must be read correctly and in the right slots -- a parser-positioning bug
        // would either swap these, throw, or leave one of them null.
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
