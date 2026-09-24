using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.StructuredStringYamlConverter{TObject}" />,
///     exercised through <see cref="TimeStringModel" />, including the parser-positioning fix (see
///     <see cref="IpAddressYamlConverterTests" /> for the full explanation).
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
