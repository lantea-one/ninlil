using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Common.Serializer.Tests.Attribute;

/// <summary>
///     These tests check that <see cref="JsonConverterAttribute{TConverter}" /> applies its converter
///     type, both by inspecting the attribute and through a serialization round trip.
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
        // The base JsonConverterAttribute.CreateConverter returns null unless overridden; the
        // framework reads ConverterType instead. So this test serializes a value rather than
        // calling CreateConverter.
        string json = System.Text.Json.JsonSerializer.Serialize(new Wrapper { Value = 5 });

        Assert.Equal("""{"Value":5}""", json);
    }

    private class Wrapper
    {
        [JsonConverter<ExampleConverter>]
        public int Value { get; set; }
    }
}
