using System.Drawing;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip colors through <see cref="Serializer.Converter.RgbColorJsonConverter" />
///     (the non-nullable <see cref="Color" /> variant). The converter writes <c>rgb(r, g, b)</c>,
///     which <see cref="ColorTranslator.FromHtml(string)" /> cannot parse, so reading must go through
///     <see cref="Serializer.Converter.RgbColorParsingHelper" />; the round-trip tests check that.
/// </summary>
public class RgbColorJsonConverterTests
{
    private class Wrapper
    {
        public Color Color { get; set; }
    }

    [Fact]
    public void RoundTrips_Color()
    {
        var wrapper = new Wrapper { Color = Color.FromArgb(255, 10, 20, 30) };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal(10, result.Color.R);
        Assert.Equal(20, result.Color.G);
        Assert.Equal(30, result.Color.B);
    }

    [Fact]
    public void RoundTrips_EmptyColor_AsNull()
    {
        var wrapper = new Wrapper { Color = Color.Empty };

        string json = JsonSerializer.Serialize(wrapper);

        Assert.Contains("\"Color\":null", json);

        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);
        Assert.Equal(Color.Empty, result.Color);
    }

    [Fact]
    public void RoundTrips_ColorWrittenBySerializer_ThroughTheRealWriter()
    {
        // Use the Write() output for Read() rather than a hand-written rgb(...) literal, so the
        // full round trip is covered.
        var wrapper = new Wrapper { Color = Color.FromArgb(255, 1, 2, 3) };

        string json = JsonSerializer.Serialize(wrapper);
        Assert.Contains("rgb(1, 2, 3)", json);

        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);
        Assert.Equal(Color.FromArgb(255, 1, 2, 3), result.Color);
    }

    [Fact]
    public void Deserialize_MalformedColor_ReturnsEmpty_NotThrow()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Color":"not-a-color"}""");

        Assert.Equal(Color.Empty, result.Color);
    }
}
