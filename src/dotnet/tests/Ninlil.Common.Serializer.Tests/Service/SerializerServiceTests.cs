using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Service;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Service;

/// <summary>
///     These tests cover <see cref="SerializerService" />: format dispatch for JSON, XML and YAML
///     and converter registration. <see cref="SerializerService.AddConverter{TConverter}" /> must
///     test <c>typeof(JsonConverter).IsAssignableFrom(typeof(TConverter))</c> (base first); the
///     reverse direction is false for every converter and would reject valid ones.
/// </summary>
public class SerializerServiceTests
{
    /// <summary>
    ///     A minimal JSON converter type, used to check that <c>AddConverter&lt;TConverter&gt;()</c>
    ///     accepts a valid JSON converter.
    /// </summary>
    private class TestJsonConverter : JsonConverter<int>
    {
        public override int Read(
            ref System.Text.Json.Utf8JsonReader reader,
            Type typeToConvert,
            System.Text.Json.JsonSerializerOptions options
        ) => reader.GetInt32();

        public override void Write(
            System.Text.Json.Utf8JsonWriter writer,
            int value,
            System.Text.Json.JsonSerializerOptions options
        ) => writer.WriteNumberValue(value);
    }

    /// <summary>
    ///     A minimal YAML converter type, for the same reason as <see cref="TestJsonConverter" />.
    /// </summary>
    private class TestYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(int);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer) => 0;

        public void WriteYaml(
            IEmitter emitter,
            object value,
            Type type,
            ObjectSerializer serializer
        ) => emitter.Emit(new Scalar(value?.ToString() ?? "0"));
    }

    /// <summary>A type that implements neither converter contract, for the negative-path test.</summary>
    private class NotAConverter;

    [Fact]
    public void AddConverterGeneric_ValidJsonConverter_DoesNotThrow()
    {
        var exception = Record.Exception(SerializerService.AddConverter<TestJsonConverter>);

        Assert.Null(exception);
    }

    [Fact]
    public void AddConverterGeneric_ValidYamlConverter_DoesNotThrow()
    {
        var exception = Record.Exception(SerializerService.AddConverter<TestYamlConverter>);

        Assert.Null(exception);
    }

    [Fact]
    public void AddConverterGeneric_InvalidType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(SerializerService.AddConverter<NotAConverter>);
    }

    [Fact]
    public void AddConverter_ByInstance_Json_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            SerializerService.AddConverter(new TestJsonConverter())
        );

        Assert.Null(exception);
    }

    [Fact]
    public void AddConverter_ByType_ValidJsonConverter_DoesNotThrow()
    {
        var exception = Record.Exception(() =>
            SerializerService.AddConverter(typeof(TestJsonConverter))
        );

        Assert.Null(exception);
    }

    [Fact]
    public void AddConverter_ByType_InvalidType_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            SerializerService.AddConverter(typeof(NotAConverter))
        );
    }

    [Fact]
    public void AddConverter_ByTypeAndFormat_Json_DispatchesToJsonSerializer()
    {
        var exception = Record.Exception(() =>
            SerializerService.AddConverter(typeof(TestJsonConverter), SerializerFormatEnum.Json)
        );

        Assert.Null(exception);
    }

    [Fact]
    public void AddConverter_ByTypeAndFormat_UnsupportedFormat_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            SerializerService.AddConverter(typeof(TestJsonConverter), SerializerFormatEnum.Xml)
        );
    }

    // Must be public -- System.Xml.Serialization.XmlSerializer refuses to process non-public types.
    public class Payload
    {
        public string Value { get; set; }
    }

    [Fact]
    public void Serialize_Deserialize_JsonFormat_RoundTrips()
    {
        string serialized = SerializerService.Serialize(
            new Payload { Value = "x" },
            SerializerFormatEnum.Json
        );
        Payload result = SerializerService.Deserialize<Payload>(
            serialized,
            SerializerFormatEnum.Json
        );

        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void Serialize_Deserialize_XmlFormat_RoundTrips()
    {
        string serialized = SerializerService.Serialize(
            new Payload { Value = "x" },
            SerializerFormatEnum.Xml
        );
        Payload result = SerializerService.Deserialize<Payload>(
            serialized,
            SerializerFormatEnum.Xml
        );

        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void Serialize_Deserialize_YamlFormat_RoundTrips()
    {
        string serialized = SerializerService.Serialize(
            new Payload { Value = "x" },
            SerializerFormatEnum.Yaml
        );
        Payload result = SerializerService.Deserialize<Payload>(
            serialized,
            SerializerFormatEnum.Yaml
        );

        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void Serialize_UnsupportedFormat_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            SerializerService.Serialize(new Payload(), (SerializerFormatEnum)999)
        );
    }

    [Fact]
    public void Deserialize_UnsupportedFormat_Throws()
    {
        Assert.Throws<NotSupportedException>(() =>
            SerializerService.Deserialize<Payload>("x", (SerializerFormatEnum)999)
        );
    }

    [Fact]
    public void TrySerialize_TryDeserialize_JsonFormat_RoundTrip()
    {
        bool serializeSuccess = SerializerService.TrySerialize(
            new Payload { Value = "x" },
            out string json,
            SerializerFormatEnum.Json
        );
        Assert.True(serializeSuccess);

        bool deserializeSuccess = SerializerService.TryDeserialize(
            json,
            out Payload result,
            SerializerFormatEnum.Json
        );

        Assert.True(deserializeSuccess);
        Assert.Equal("x", result.Value);
    }

    [Fact]
    public void SerializePretty_JsonFormat_ProducesIndentedOutput()
    {
        string pretty = SerializerService.SerializePretty(
            new Payload { Value = "x" },
            SerializerFormatEnum.Json
        );

        Assert.Contains("\n", pretty);
    }
}
