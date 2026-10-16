using System.Text;
using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Model;

/// <summary>
///     These tests cover <see cref="DataUriModel" />: parsing with and without optional
///     parameters, validity, round trips through its string form and JSON, the implicit
///     conversions and malformed input.
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

    /// <summary>
    ///     Returns every byte value 0x00..0xFF once, the smallest input that is not valid UTF-8 text.
    /// </summary>
    private static byte[] AllByteValues() =>
        Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

    [Fact]
    public async Task FromStreamAsync_WithBinaryBytes_KeepsEveryByte()
    {
        byte[] bytes = AllByteValues();
        using MemoryStream stream = new(bytes);

        DataUriModel uri = await DataUriModel.FromStreamAsync(stream, "all.bin", "image/png");

        Assert.Equal(bytes, uri.Binary);
        Assert.Equal("all.bin", uri.Filename);
        Assert.Equal("image/png", uri.MimeType);
    }

    [Fact]
    public async Task FromStreamAsync_AfterReading_LeavesTheCallersStreamOpen()
    {
        byte[] bytes = AllByteValues();
        using MemoryStream stream = new(bytes);

        await DataUriModel.FromStreamAsync(stream, "all.bin", "image/png");

        Assert.True(stream.CanRead);
        stream.Position = 0;
        Assert.Equal(0, stream.ReadByte());
    }

    [Fact]
    public async Task FromStreamAsync_WithBinaryBytes_SetsNoCharacterSet()
    {
        using MemoryStream stream = new(AllByteValues());

        DataUriModel uri = await DataUriModel.FromStreamAsync(stream, "all.bin", "image/png");

        Assert.Null(uri.CharacterSet);
    }

    [Fact]
    public void FromStream_WithBinaryBytes_KeepsEveryByte()
    {
        byte[] bytes = AllByteValues();
        using MemoryStream stream = new(bytes);

        DataUriModel uri = DataUriModel.FromStream(stream, "all.bin", "image/png");

        Assert.Equal(bytes, uri.Binary);
    }

    [Fact]
    public void ImplicitConversion_FromAStreamOfBinaryBytes_KeepsEveryByteAndLeavesItOpen()
    {
        byte[] bytes = AllByteValues();
        using MemoryStream stream = new(bytes);

        DataUriModel uri = stream;

        Assert.Equal(bytes, uri.Binary);
        Assert.Null(uri.CharacterSet);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ImplicitConversion_FromAFileStream_KeepsTheFileNameOnly()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ninlil-{Guid.NewGuid():N}.png");
        byte[] bytes = AllByteValues();
        File.WriteAllBytes(path, bytes);
        try
        {
            using FileStream stream = File.OpenRead(path);

            DataUriModel uri = stream;

            Assert.Equal(Path.GetFileName(path), uri.Filename);
            Assert.Equal("image/png", uri.MimeType);
            Assert.Equal(bytes, uri.Binary);
            Assert.DoesNotContain(Path.GetTempPath(), uri.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Filename_WithSpacesAndPlus_RoundTripsThroughTheStringForm()
    {
        DataUriModel uri = new([1, 2, 3], "image/png", "my logo+1.png");

        Assert.True(DataUriModel.TryParse(uri.ToString(), out DataUriModel parsed));

        Assert.Equal("my logo+1.png", parsed.Filename);
        Assert.Equal(uri.Binary, parsed.Binary);
    }
}
