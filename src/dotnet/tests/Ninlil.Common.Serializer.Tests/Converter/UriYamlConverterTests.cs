using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip URIs through <see cref="Serializer.Converter.UriYamlConverter" />
///     with two sibling properties, to catch parser-positioning mistakes (see
///     <see cref="IpAddressYamlConverterTests" />).
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
