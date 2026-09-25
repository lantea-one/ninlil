using System.Drawing;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip the non-nullable and nullable <see cref="Color" /> forms through
///     <see cref="Serializer.Converter.RgbColorYamlConverter" />, with sibling properties to catch
///     parser-positioning mistakes (see <see cref="IpAddressYamlConverterTests" />).
/// </summary>
public class RgbColorYamlConverterTests
{
    private class Wrapper
    {
        public Color Color { get; set; }
        public Color? NullableColor { get; set; }
    }

    [Fact]
    public void RoundTrips_Color()
    {
        var wrapper = new Wrapper
        {
            Color = Color.FromArgb(255, 5, 6, 7),
            NullableColor = Color.FromArgb(255, 8, 9, 10),
        };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(5, result.Color.R);
        Assert.Equal(6, result.Color.G);
        Assert.Equal(7, result.Color.B);
        Assert.NotNull(result.NullableColor);
        Assert.Equal(8, result.NullableColor.Value.R);
    }

    [Fact]
    public void RoundTrips_EmptyColor_AsColorEmpty()
    {
        var wrapper = new Wrapper { Color = Color.Empty, NullableColor = null };

        string yaml = YamlSerializer.Serialize(wrapper);
        Wrapper result = YamlSerializer.Deserialize<Wrapper>(yaml);

        Assert.Equal(Color.Empty, result.Color);
        Assert.Null(result.NullableColor);
    }

    [Fact]
    public void Deserialize_MalformedColor_ReturnsEmpty_NotThrow()
    {
        // The written rgb(r, g, b) form must read back (see RgbColorJsonConverterTests);
        // ColorTranslator.FromHtml cannot parse it.
        Wrapper result = YamlSerializer.Deserialize<Wrapper>("color: \"not-a-color\"\n");

        Assert.Equal(Color.Empty, result.Color);
    }
}
