using System.Net.NetworkInformation;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip MAC addresses through
///     <see cref="Serializer.Converter.PhysicalAddressYamlConverter" /> with two sibling properties,
///     to catch parser-positioning mistakes (see <see cref="IpAddressYamlConverterTests" />).
/// </summary>
public class PhysicalAddressYamlConverterTests
{
    private class Wrapper
    {
        public PhysicalAddress First { get; set; }
        public PhysicalAddress Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingAddresses()
    {
        var wrapper = new Wrapper
        {
            First = PhysicalAddress.Parse("00-11-22-33-44-55"),
            Second = PhysicalAddress.Parse("AA-BB-CC-DD-EE-FF"),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(PhysicalAddress.Parse("00-11-22-33-44-55"), result.First);
        Assert.Equal(PhysicalAddress.Parse("AA-BB-CC-DD-EE-FF"), result.Second);
    }

    [Fact]
    public void Deserialize_MalformedAddress_ReturnsNull()
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("first: \"not-a-mac\"\n");

        Assert.Null(result.First);
    }
}
