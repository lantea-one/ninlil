using System.Text.Json;
using Ninlil.Dns.Common.Serialization;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Serialization;

/// <summary>
///     This class tests <see cref="DnsNameJsonConverter" /> on in-memory JSON: valid names
///     round-trip, and a malformed name or a non-string token throws <see cref="JsonException" />
///     instead of reading as null.
/// </summary>
public class DnsNameJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new DnsNameJsonConverter() },
    };

    [Theory]
    [InlineData("a..b.")]
    [InlineData(".a.")]
    public void Read_WithAnEmptyLabel_ThrowsJsonExceptionWithTheParseError(string name)
    {
        AssertRejected(name);
    }

    [Fact]
    public void Read_WithA64OctetLabel_ThrowsJsonExceptionWithTheParseError()
    {
        AssertRejected($"{new string('a', 64)}.example.");
    }

    [Fact]
    public void Read_WithANameOver255Octets_ThrowsJsonExceptionWithTheParseError()
    {
        AssertRejected(string.Join('.', Enumerable.Repeat(new string('a', 62), 5)) + ".");
    }

    [Fact]
    public void Read_WithANumberToken_ThrowsJsonException()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DnsName>("12", Options)
        );

        Assert.Equal("A DNS name must be a JSON string.", exception.Message);
    }

    [Theory]
    [InlineData("www.example.")]
    [InlineData(".")]
    [InlineData("a\\.b.example.")]
    [InlineData("a\\032b.example.")]
    public void ReadThenWrite_WithAValidName_RoundTrips(string presentation)
    {
        DnsName parsed = DnsName.Parse(presentation);
        string json = JsonSerializer.Serialize(parsed, Options);

        DnsName read = JsonSerializer.Deserialize<DnsName>(json, Options);

        Assert.Equal(parsed.ToString(), read.ToString());
        Assert.Equal(json, JsonSerializer.Serialize(read, Options));
    }

    [Fact]
    public void Read_WithJsonNull_ReturnsNull()
    {
        Assert.Null(JsonSerializer.Deserialize<DnsName>("null", Options));
    }

    private static void AssertRejected(string presentation)
    {
        Assert.False(DnsName.TryParse(presentation, out _, out string error));

        JsonException exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<DnsName>(JsonSerializer.Serialize(presentation), Options)
        );

        Assert.Equal(error, exception.Message);
    }
}
