using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.VersionYamlConverter" />,
///     including the parser-positioning fix (see <see cref="IpAddressYamlConverterTests" /> for
///     the full explanation). There is no JSON equivalent of this converter in the library, so
///     only YAML round-tripping is covered here.
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
