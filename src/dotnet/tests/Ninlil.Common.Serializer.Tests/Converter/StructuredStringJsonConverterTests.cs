using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;
using JsonException = System.Text.Json.JsonException;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests cover <see cref="Serializer.Converter.StructuredStringJsonConverter{TObject}" />
///     through <see cref="TimeStringModel" />, whose converter <see cref="JsonSerializer" /> registers
///     for every call. Text that does not parse and tokens of the wrong kind are refused with a
///     <see cref="System.Text.Json.JsonException" />; null and blank values read as null.
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

    [Fact]
    public void Deserialize_JsonNull_ReturnsNull()
    {
        Wrapper result = JsonSerializer.Deserialize<Wrapper>("""{"Duration":null}""");

        Assert.Null(result.Duration);
    }

    [Fact]
    public void Deserialize_TextThatDoesNotParse_ThrowsJsonExceptionNamingTheValueAndTheModel()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Wrapper>("""{"Duration":"not a time"}""")
        );

        Assert.Contains("'not a time'", exception.Message);
        Assert.Contains(nameof(TimeStringModel), exception.Message);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void Deserialize_ANonStringToken_ThrowsJsonException(string token)
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Wrapper>("{\"Duration\":" + token + "}")
        );
    }

    [Fact]
    public void Deserialize_ANonStringTokenInsideAnObject_LeavesNoPartialResult()
    {
        // The reader must refuse the token instead of skipping it, so a later property never
        // reads from the middle of the value.
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Wrapper>("""{"Duration":{"a":1},"Other":"x"}""")
        );
    }
}
