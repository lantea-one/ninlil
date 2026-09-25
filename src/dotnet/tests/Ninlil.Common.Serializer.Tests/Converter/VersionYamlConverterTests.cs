using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip versions through <see cref="Serializer.Converter.VersionYamlConverter" />
///     with sibling properties, to catch parser-positioning mistakes (see
///     <see cref="IpAddressYamlConverterTests" />). The library has no JSON version converter.
/// </summary>
public class VersionYamlConverterTests
{
    private class Wrapper
    {
        public Version First { get; set; }
        public Version Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingVersions()
    {
        var wrapper = new Wrapper { First = new Version(1, 2, 3), Second = new Version(4, 5) };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(new Version(1, 2, 3), result.First);
        Assert.Equal(new Version(4, 5), result.Second);
    }

    [Fact]
    public void Deserialize_MalformedVersion_ReturnsNull()
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("first: \"not-a-version\"\n");

        Assert.Null(result.First);
    }
}
