using System.Drawing;
using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.RgbColorJsonConverter" />
///     (the non-nullable <see cref="Color" /> variant). These tests specifically lock in the fix
///     for a round-trip-breaking production bug: the converter wrote colors as an
///     <c>rgb(r, g, b)</c> string, but read them back using <see cref="ColorTranslator.FromHtml(string)" />,
///     which -- despite its name -- does not actually support that functional notation at all and
///     throws for every non-empty color value written. The fix routes reading through
///     <see cref="Serializer.Converter.RgbColorParsingHelper" /> instead.
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
        // This exercises the real Write() -> Read() round trip end to end (rather than a
        // hand-authored rgb(...) literal), which is exactly the path that used to throw.
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
