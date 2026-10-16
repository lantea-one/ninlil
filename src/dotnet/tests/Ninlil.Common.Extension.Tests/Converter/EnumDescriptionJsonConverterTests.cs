using System.Runtime.Serialization;
using System.Text.Json;
using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Extension.Tests.TestSupport;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.Converter;

/// <summary>
///     These tests round-trip a sample enum through System.Text.Json with
///     <see cref="EnumDescriptionJsonConverter{TEnum}" />: attributed values, raw member names and
///     unattributed members, and the refusal of unknown names, blank strings and null.
/// </summary>
public class EnumDescriptionJsonConverterTests
{
    /// <summary>
    ///     A sample enum decorated with <see cref="EnumMemberAttribute" /> on one member, used to
    ///     round-trip through JSON serialization.
    /// </summary>
    private enum SampleEnum
    {
        [EnumMember(Value = "first-value")]
        First,

        Second,
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new EnumDescriptionJsonConverter<SampleEnum>() },
    };

    [Fact]
    public void WriteOnAttributedMemberSerializesTheAttributeValue()
    {
        string json = JsonSerializer.Serialize(SampleEnum.First, Options);

        Assert.Equal("\"first-value\"", json);
    }

    [Fact]
    public void ReadOnAttributedValueDeserializesToTheAttributedMember()
    {
        SampleEnum result = JsonSerializer.Deserialize<SampleEnum>("\"first-value\"", Options);

        Assert.Equal(SampleEnum.First, result);
    }

    [Fact]
    public void ReadOnRawMemberNameDeserializesToThatMember()
    {
        SampleEnum result = JsonSerializer.Deserialize<SampleEnum>("\"Second\"", Options);

        Assert.Equal(SampleEnum.Second, result);
    }

    /// <summary>
    ///     <c>Write</c> calls <c>value.ToDescription(...)</c>, which falls back to the member's own
    ///     name (see <c>ToDescriptionEnumExtensionTests</c>). An unattributed member that is not the
    ///     first declared must be written as its name, not the first member's.
    /// </summary>
    [Fact]
    public void WriteOnUnattributedMemberSerializesItsOwnName()
    {
        string json = JsonSerializer.Serialize(SampleEnum.Second, Options);

        Assert.Equal("\"Second\"", json);
    }

    private static readonly JsonSerializerOptions SecondValueOptions = new()
    {
        Converters =
        {
            new EnumDescriptionJsonConverter<SampleSecondValueEnum>(),
            new EnumDescriptionJsonConverter<SampleWireEnum>(),
        },
    };

    [Fact]
    public void Read_OnAnUnknownName_ThrowsJsonException()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SampleSecondValueEnum>("\"bogus\"", SecondValueOptions)
        );

        Assert.Contains("bogus", exception.Message);
        Assert.Contains(nameof(SampleSecondValueEnum), exception.Message);
    }

    [Fact]
    public void Read_OnAnEmptyString_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SampleSecondValueEnum>("\"\"", SecondValueOptions)
        );
    }

    [Fact]
    public void Read_OnTheSecondMembersDescription_ReturnsTheSecondMember()
    {
        Assert.Equal(
            SampleSecondValueEnum.Second,
            JsonSerializer.Deserialize<SampleSecondValueEnum>(
                "\"second-value\"",
                SecondValueOptions
            )
        );
    }

    [Fact]
    public void Read_OnNullIntoANonNullableProperty_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SampleEnumHolder>("{\"Value\":null}", SecondValueOptions)
        );
    }

    [Fact]
    public void Read_OnNullIntoANullableProperty_ReturnsNull()
    {
        SampleEnumHolder holder = JsonSerializer.Deserialize<SampleEnumHolder>(
            "{\"Optional\":null}",
            SecondValueOptions
        );

        Assert.Null(holder.Optional);
    }

    /// <summary>
    ///     An undefined code is written as its number, and reads back as the same code, so wire
    ///     values survive a round trip.
    /// </summary>
    [Fact]
    public void WriteThenRead_OnAnUndefinedCode_RoundTripsTheNumber()
    {
        string json = JsonSerializer.Serialize((SampleWireEnum)99, SecondValueOptions);

        Assert.Equal(
            (SampleWireEnum)99,
            JsonSerializer.Deserialize<SampleWireEnum>(json, SecondValueOptions)
        );
    }

    /// <summary>
    ///     The library's own serializer has an options-level <c>JsonStringEnumConverter</c>, which
    ///     takes precedence over the enum's type-level converter attribute. Stored JSON therefore
    ///     keeps the camelCase member name and never passes through
    ///     <see cref="EnumDescriptionJsonConverter{TEnum}" />. If this fails, stored data would change.
    /// </summary>
    [Fact]
    public void LibrarySerializer_OnAnEnumWithTheConverterAttribute_WritesTheCamelCaseMemberName()
    {
        string json = Ninlil.Common.Serializer.JsonSerializer.Serialize(
            SampleConverterAttributeEnum.Second
        );

        Assert.Equal("\"second\"", json);
    }
}
