using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     These tests round-trip absolute, relative and null URIs through
///     <see cref="Serializer.Converter.UriJsonConverter" />.
/// </summary>
public class UriJsonConverterTests
{
    private class Wrapper
    {
        public Uri Address { get; set; }
    }

    [Fact]
    public void RoundTrips_AbsoluteUri()
    {
        var wrapper = new Wrapper { Address = new Uri("https://example.com/path?query=1") };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal(new Uri("https://example.com/path?query=1"), result.Address);
    }

    [Fact]
    public void RoundTrips_RelativeUri()
    {
        var wrapper = new Wrapper { Address = new Uri("relative/path", UriKind.Relative) };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Equal("relative/path", result.Address.ToString());
    }

    [Fact]
    public void RoundTrips_NullUri()
    {
        var wrapper = new Wrapper { Address = null };

        string json = JsonSerializer.Serialize(wrapper);
        Wrapper result = JsonSerializer.Deserialize<Wrapper>(json);

        Assert.Null(result.Address);
    }
}
