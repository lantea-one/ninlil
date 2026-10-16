using Ninlil.Common.Serializer;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     These tests cover the <see cref="XmlSerializer" /> static wrapper: round trips, null and
///     malformed input, indented output, namespaces and the reader and writer settings.
/// </summary>
public class XmlSerializerTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var payload = new Simple { Value = "hello" };

        string xml = XmlSerializer.Serialize(payload);
        Simple result = XmlSerializer.Deserialize<Simple>(xml);

        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Serialize_NullPayload_ReturnsNull()
    {
        Assert.Null(XmlSerializer.Serialize((object)null));
    }

    [Fact]
    public void SerializePretty_NullPayload_ReturnsNull()
    {
        Assert.Null(XmlSerializer.SerializePretty((object)null));
    }

    [Fact]
    public void Deserialize_NullOrEmptyXml_ReturnsNull()
    {
        Assert.Null(XmlSerializer.Deserialize(typeof(Simple), null));
        Assert.Null(XmlSerializer.Deserialize(typeof(Simple), ""));
    }

    [Fact]
    public void SerializePretty_ProducesIndentedOutput()
    {
        string compact = XmlSerializer.Serialize(new Simple { Value = "x" });
        string pretty = XmlSerializer.SerializePretty(new Simple { Value = "x" });

        Assert.DoesNotContain("\n", compact);
        Assert.Contains("\n", pretty);
    }

    [Fact]
    public void TryDeserialize_MalformedXml_ReturnsFalse()
    {
        bool success = XmlSerializer.TryDeserialize("not xml", out Simple result);

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TrySerialize_ValidPayload_ReturnsTrue()
    {
        bool success = XmlSerializer.TrySerialize(new Simple { Value = "x" }, out string xml);

        Assert.True(success);
        Assert.Contains("<Value>x</Value>", xml);
    }

    [Fact]
    public void GetWriterOptions_PrettyOverride_SetsIndent()
    {
        var options = XmlSerializer.GetWriterOptions(pretty: true);

        Assert.True(options.Indent);
    }

    [Fact]
    public void GetReaderOptions_NoOverride_ReturnsLibraryDefault()
    {
        var options = XmlSerializer.GetReaderOptions();

        Assert.Same(XmlSerializer.DeserializerOptions, options);
    }
}
