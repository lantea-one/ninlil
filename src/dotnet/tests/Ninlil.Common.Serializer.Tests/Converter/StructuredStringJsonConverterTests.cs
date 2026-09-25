using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests cover <see cref="Serializer.Converter.StructuredStringJsonConverter{TObject}" />
///     through <see cref="TimeStringModel" />, whose converter <see cref="JsonSerializer" /> registers
///     for every call.
/// </summary>
public class StructuredStringJsonConverterTests
{
    private class Wrapper
    {
        public TimeStringModel Duration { get; set; }
    }

    [Fact]
    public void RoundTrips_PopulatedValue()
    {
        var wrapper = new Wrapper { Duration = TimeStringModel.FromString("1d2h3m") };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.NotNull(result.Duration);
        Assert.Equal("1d2h3m", result.Duration.ToString());
    }

    [Fact]
    public void RoundTrips_NullValue()
    {
        var wrapper = new Wrapper { Duration = null };

        string json = JsonSerializer.Serialize(wrapper);

        Assert.Contains("\"Duration\":null", json);

        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);
        Assert.Null(result.Duration);
    }

    [Fact]
    public void Deserialize_EmptyString_ReturnsNull()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Duration":""}""");

        Assert.Null(result.Duration);
    }

    [Fact]
    public void Deserialize_WhitespaceOnlyString_ReturnsNull()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Duration":"   "}""");

        Assert.Null(result.Duration);
    }
}
