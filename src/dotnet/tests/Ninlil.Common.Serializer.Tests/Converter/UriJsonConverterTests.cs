using Ninlil.Common.Serializer;

namespace Ninlil.Common.Serializer.Tests.Converter;

/// <summary>
///     This class contains unit tests for <see cref="Serializer.Converter.UriJsonConverter" />.
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
