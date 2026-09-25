using System.Drawing;
using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip nullable colors through
///     <see cref="Serializer.Converter.NullableRgbColorJsonConverter" />. A null <c>Color?</c> must
///     round-trip as JSON <c>null</c>, which is separate from <see cref="Color.Empty" /> (the
///     "no color" value of the non-nullable <see cref="Serializer.Converter.RgbColorJsonConverter" />).
/// </summary>
public class NullableRgbColorJsonConverterTests
{
    private class Wrapper
    {
        public Color? Color { get; set; }
    }

    [Fact]
    public void RoundTrips_Color()
    {
        var wrapper = new Wrapper { Color = Color.FromArgb(255, 100, 150, 200) };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.NotNull(result.Color);
        Assert.Equal(100, result.Color.Value.R);
        Assert.Equal(150, result.Color.Value.G);
        Assert.Equal(200, result.Color.Value.B);
    }

    [Fact]
    public void RoundTrips_NullColor()
    {
        var wrapper = new Wrapper { Color = null };

        string json = JsonSerializer.Serialize(wrapper);

        Assert.Contains("\"Color\":null", json);

        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);
        Assert.Null(result.Color);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Color":""}""");

        Assert.Null(result.Color);
    }

    [Fact]
    public void Deserialize_MalformedColor_ReturnsNull_NotThrow()
    {
        // The written rgb(r, g, b) form must read back (see RgbColorJsonConverterTests);
        // ColorTranslator.FromHtml cannot parse it.
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Color":"not-a-color"}""");

        Assert.Null(result.Color);
    }
}
