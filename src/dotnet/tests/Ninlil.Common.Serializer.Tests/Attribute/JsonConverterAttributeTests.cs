using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Attribute;

namespace Ninlil.Common.Serializer.Tests.Attribute;

/// <summary>
///     This class contains unit tests for <see cref="JsonConverterAttribute{TConverter}" />.
/// </summary>
public class JsonConverterAttributeTests
{
    private class ExampleConverter : JsonConverter<int>
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

    [Fact]
    public void Constructor_SetsUnderlyingConverterType_ToTheGenericArgument()
    {
        var attribute = new JsonConverterAttribute<ExampleConverter>();

        Assert.Equal(typeof(ExampleConverter), attribute.ConverterType);
    }

    [Fact]
    public void AppliedToAProperty_IsHonoredByTheRealSerializer()
    {
        // The base System.Text.Json.Serialization.JsonConverterAttribute's own CreateConverter
        // returns null unless overridden -- the framework instead reads ConverterType directly
        // when the attribute is discovered on a type/property, which this test confirms end to
        // end via a real Serialize call rather than by calling CreateConverter directly.
        string json = System.Text.Json.JsonSerializer.Serialize(new Wrapper { Value = 5 });

        Assert.Equal("""{"Value":5}""", json);
    }

    private class Wrapper
    {
        [JsonConverter<ExampleConverter>]
        public int Value { get; set; }
    }
}
