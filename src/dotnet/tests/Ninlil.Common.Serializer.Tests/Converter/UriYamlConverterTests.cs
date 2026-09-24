using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.UriYamlConverter" />,
///     including the parser-positioning fix (see <see cref="IpAddressYamlConverterTests" /> for
///     the full explanation).
/// </summary>
public class UriYamlConverterTests
{
    private class Wrapper
    {
        public Uri First { get; set; }
        public Uri Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingUris()
    {
        var wrapper = new Wrapper
        {
            First = new Uri("https://example.com/a"),
            Second = new Uri("https://example.com/b"),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(new Uri("https://example.com/a"), result.First);
        Assert.Equal(new Uri("https://example.com/b"), result.Second);
    }
}
