using Ninlil.Common.Serializer;
using Ninlil.Common.Serializer.Converter;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests;

/// <summary>
///     These tests cover the <see cref="YamlSerializer" /> static wrapper: round trips, camelCase
///     naming and converter registration. A null payload must return null from
///     <see cref="YamlSerializer.Serialize(object, YamlDotNet.Serialization.ISerializer)" /> and
///     <see cref="YamlSerializer.SerializePretty(object, YamlDotNet.Serialization.ISerializer)" />
///     rather than throwing on <c>payload.GetType()</c>.
/// </summary>
public class YamlSerializerTests
{
    public class Simple
    {
        public string Value { get; set; }
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var payload = new Simple { Value = "hello" };

        string yaml = YamlSerializer.Serialize(payload);
        Simple result = YamlSerializer.Deserialize<Simple>(yaml);

        Assert.Equal("hello", result.Value);
    }

    [Fact]
    public void Serialize_NullPayload_ReturnsNull_NotThrow()
    {
        Assert.Null(YamlSerializer.Serialize((object)null));
    }

    [Fact]
    public void SerializePretty_NullPayload_ReturnsNull_NotThrow()
    {
        Assert.Null(YamlSerializer.SerializePretty((object)null));
    }

    [Fact]
    public void Deserialize_NullOrEmptyYaml_ReturnsDefault()
    {
        Assert.Null(YamlSerializer.Deserialize<Simple>(null));
        Assert.Null(YamlSerializer.Deserialize<Simple>(""));
    }

    [Fact]
    public void TryDeserialize_MalformedYaml_ReturnsFalse()
    {
        bool success = YamlSerializer.TryDeserialize(
            "not: valid: yaml: at: all: [",
            out Simple result
        );

        Assert.False(success);
        Assert.Null(result);
    }

    [Fact]
    public void TrySerialize_ValidPayload_ReturnsTrue()
    {
        bool success = YamlSerializer.TrySerialize(new Simple { Value = "x" }, out string yaml);

        Assert.True(success);

        // Unlike JsonSerializer (no naming policy), YamlSerializer.GetSerializer() applies a
        // camelCase naming convention.
        Assert.Contains("value", yaml);
    }

    [Fact]
    public void AddConverter_ByInstance_DoesNotDuplicate()
    {
        var converter = new IpAddressYamlConverter();

        // Adding an already-registered converter type must be a no-op. This is checked
        // indirectly: building the serializers does not throw and round trips still work.
        YamlSerializer.AddConverter(converter);
        YamlSerializer.AddConverter(converter);

        string yaml = YamlSerializer.Serialize(new Simple { Value = "x" });
        Simple result = YamlSerializer.Deserialize<Simple>(yaml);

        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void GetDeserializer_And_GetSerializer_ReturnUsableInstances()
    {
        var serializer = YamlSerializer.GetSerializer();
        var deserializer = YamlSerializer.GetDeserializer();

        string yaml = YamlSerializer.Serialize(new Simple { Value = "y" }, serializer);
        Simple result = YamlSerializer.Deserialize<Simple>(yaml, deserializer);

        Assert.Equal("y", result.Value);
    }
}
