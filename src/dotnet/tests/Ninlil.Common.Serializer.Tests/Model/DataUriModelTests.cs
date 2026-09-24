using System.Text;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     This class contains unit tests for <see cref="DataUriModel" />, covering parsing,
///     round-tripping through JSON, and its implicit conversions.
/// </summary>
public class DataUriModelTests
{
    [Fact]
    public void Parses_BasicDataUri()
    {
        var uri = new DataUriModel("data:text/plain;base64,SGVsbG8=");

        Assert.Equal("text/plain", uri.MimeType);
        Assert.Equal("Hello", Encoding.UTF8.GetString(uri.Binary));
        Assert.True(uri.IsValid());
    }

    [Fact]
    public void Parses_DataUriWithFilenameAndCharset()
    {
        var uri = new DataUriModel(
            "data:text/plain;charset=utf-8;filename=test.txt;base64,SGVsbG8="
        );

        Assert.Equal("text/plain", uri.MimeType);
        Assert.Equal("utf-8", uri.CharacterSet);
        Assert.Equal("test.txt", uri.Filename);
    }

    [Fact]
    public void FromBinary_And_ToBase64_RoundTrip()
    {
        byte[] bytes = "round trip"u8.ToArray();

        DataUriModel uri = new(bytes, "application/octet-stream");

        Assert.Equal(Convert.ToBase64String(bytes), uri.ToBase64());
    }

    [Fact]
    public void ImplicitConversion_ToAndFromByteArray()
    {
        byte[] bytes = [1, 2, 3, 4];

        DataUriModel uri = bytes;
        byte[] result = uri;

        Assert.Equal(bytes, result);
    }

    [Fact]
    public void ImplicitConversion_ToAndFromStream()
    {
        byte[] bytes = "stream content"u8.ToArray();
        DataUriModel uri = new(bytes, "text/plain");

        using Stream stream = uri;
        using var reader = new StreamReader(stream);

        Assert.Equal("stream content", reader.ReadToEnd());
    }

    [Fact]
    public void IsDataUri_ValidatesWithoutThrowing()
    {
        Assert.True(DataUriModel.IsDataUri("data:text/plain;base64,SGVsbG8="));
        Assert.False(DataUriModel.IsDataUri("not a data uri"));
        Assert.False(DataUriModel.IsDataUri(null));
        Assert.False(DataUriModel.IsDataUri(""));
    }

    [Fact]
    public void IsValid_EmptyModel_IsFalse()
    {
        var uri = new DataUriModel();

        Assert.False(uri.IsValid());
        Assert.True(uri.IsNull());
    }

    [Fact]
    public void ToString_RoundTrips_ThroughParse()
    {
        var uri = new DataUriModel("data:text/plain;filename=a.txt;base64,SGVsbG8=");

        string serialized = uri.ToString();
        var reparsed = new DataUriModel(serialized);

        Assert.Equal(uri.MimeType, reparsed.MimeType);
        Assert.Equal(uri.Filename, reparsed.Filename);
        Assert.Equal(uri.Binary, reparsed.Binary);
    }

    [Fact]
    public void RoundTrips_ThroughJson()
    {
        var uri = new DataUriModel("data:text/plain;base64,SGVsbG8=");

        string json = JsonSerializer.Serialize(uri);
        DataUriModel result = JsonSerializer.Deserialize<DataUriModel>(json);

        Assert.Equal(uri.Binary, result.Binary);
        Assert.Equal(uri.MimeType, result.MimeType);
    }

    [Fact]
    public void TryParse_MalformedInput_ReturnsFalse()
    {
        bool success = DataUriModel.TryParse("not-a-data-uri", out DataUriModel uri);

        Assert.False(success);
        Assert.Null(uri);
    }
}
