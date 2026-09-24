using System.Net.NetworkInformation;
using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.PhysicalAddressYamlConverter" />,
///     including the parser-positioning fix (see <see cref="IpAddressYamlConverterTests" /> for
///     the full explanation of the bug this guards against).
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
