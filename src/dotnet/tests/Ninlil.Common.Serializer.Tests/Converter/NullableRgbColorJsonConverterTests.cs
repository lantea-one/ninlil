using System.Drawing;
using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.NullableRgbColorJsonConverter" />
///     (the <see cref="Color" />? variant), with particular attention to null handling -- a
///     genuinely null <c>Color?</c> must round-trip as JSON <c>null</c>, distinct from
///     <see cref="Color.Empty" /> (which the non-nullable
///     <see cref="Serializer.Converter.RgbColorJsonConverter" /> variant uses to represent
///     "no color").
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
        // Locks in the fix for the same rgb(r, g, b) round-trip bug documented on
        // RgbColorJsonConverterTests -- ColorTranslator.FromHtml cannot parse this notation and
        // used to throw for it instead of gracefully returning null.
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Color":"not-a-color"}""");

        Assert.Null(result.Color);
    }
}
