using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests cover <see cref="Serializer.Converter.StructuredStringYamlConverter{TObject}" />
///     through <see cref="TimeStringModel" />, with sibling properties to catch parser-positioning
///     mistakes (see <see cref="IpAddressYamlConverterTests" />).
/// </summary>
public class StructuredStringYamlConverterTests
{
    private class Wrapper
    {
        public TimeStringModel First { get; set; }
        public TimeStringModel Second { get; set; }
    }

    [Fact]
    public void RoundTrips_TwoSiblingValues()
    {
        var wrapper = new Wrapper
        {
            First = TimeStringModel.FromString("5m"),
            Second = TimeStringModel.FromString("1h"),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal("5m", result.First.ToString());
        Assert.Equal("1h", result.Second.ToString());
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("first: \"\"\n");

        Assert.Null(result.First);
    }
}
