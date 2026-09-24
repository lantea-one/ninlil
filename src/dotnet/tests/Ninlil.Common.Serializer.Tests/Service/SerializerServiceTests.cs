using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Enum;
using Ninlil.Common.Serializer.Service;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Ninlil.Common.Serializer.Tests.Service;

/// <summary>
///     This class contains unit tests for <see cref="SerializerService" />. This test class
///     specifically locks in the fix for a bug in
///     <see cref="SerializerService.AddConverter{TConverter}" />: the original checks used
///     <c>typeof(TConverter).IsAssignableFrom(typeof(JsonConverter))</c> /
///     <c>typeof(TConverter).IsAssignableFrom(typeof(IYamlTypeConverter))</c> -- backwards from
///     what <see cref="Type.IsAssignableFrom" /> actually needs to answer "does TConverter derive
///     from/implement this contract?" (that requires calling <c>IsAssignableFrom</c> ON the base
///     type/interface, passing the derived type as the argument, the reverse of what was written).
///     Since <c>JsonConverter</c>/<c>IYamlTypeConverter</c> are never themselves assignable *from*
///     an unrelated concrete <c>TConverter</c>, the original checks were always false for every
///     real converter type, so <c>AddConverter&lt;TConverter&gt;()</c> always threw
///     <see cref="ArgumentException" />, for every input, including entirely valid converters.
/// </summary>
public class SerializerServiceTests
{
    /// <summary>
    ///     A minimal real JSON converter type, used to prove <c>AddConverter&lt;TConverter&gt;()</c>
    ///     now actually accepts a valid JSON converter instead of always throwing.
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
    ///     A minimal real YAML converter type, for the same reason as <see cref="TestJsonConverter" />.
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
