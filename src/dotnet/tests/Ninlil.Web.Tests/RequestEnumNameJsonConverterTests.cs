using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Serialization;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="RequestEnumNameJsonConverterFactory" /> and
///     <see cref="RequestEnumNameJsonConverter{TEnum}" /> directly through
///     <see cref="JsonSerializer" />, without a host. It covers the names-only read rule (digit
///     strings, numbers, undefined values and bad flags lists are refused; member names, declared
///     wire names and flags lists are accepted) and that the write side matches the type's own
///     converter byte for byte, including a digit string for an unnamed wire code.
/// </summary>
public class RequestEnumNameJsonConverterTests
{
    private static readonly JsonSerializerOptions requestOptions = new()
    {
        Converters = { new RequestEnumNameJsonConverterFactory() },
    };

    /// <summary>An enum with no converter attribute, to show the factory leaves it alone.</summary>
    private enum PlainEnum
    {
        One = 1,
    }

    /// <summary>A non-flags enum with a name that needs an <c>EnumMember</c> value.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<SecondEnum>))]
    private enum SecondEnum
    {
        A = 1,
        B = 2,
    }

    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, requestOptions);

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("\"99\"")]
    [InlineData("\"0\"")]
    public void Read_ForADigitString_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => Read<ZoneTypeEnum>(json));
        Assert.Throws<JsonException>(() => Read<SecondEnum>(json));
        Assert.Throws<JsonException>(() => Read<DnsScheduleDayOfWeekEnum>(json));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("99")]
    public void Read_ForAJsonNumber_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => Read<ZoneTypeEnum>(json));
        Assert.Throws<JsonException>(() => Read<SecondEnum>(json));
    }

    [Fact]
    public void Read_ForNullOnANonNullableEnum_ThrowsJsonExceptionSayingAValueIsRequired()
    {
        JsonException exception = Assert.Throws<JsonException>(() => Read<ZoneTypeEnum>("null"));

        Assert.Contains("A value is required for ZoneTypeEnum", exception.Message);
        Assert.Contains("Primary", exception.Message);
    }

    [Fact]
    public void Read_ForAJsonNumber_QuotesTheNumberInTheMessage()
    {
        JsonException exception = Assert.Throws<JsonException>(() => Read<ZoneTypeEnum>("99"));

        Assert.Contains("'99' is not a valid value for ZoneTypeEnum", exception.Message);
    }

    [Fact]
    public void Read_ForAnUndefinedNumberOnABuiltInConverterEnum_ThrowsJsonException() =>
        Assert.Throws<JsonException>(() => Read<SecondEnum>("5"));

    [Theory]
    [InlineData("\"Primary\"", ZoneTypeEnum.Primary)]
    [InlineData("\"primary\"", ZoneTypeEnum.Primary)]
    [InlineData("\"  PRIMARY \"", ZoneTypeEnum.Primary)]
    public void Read_ForAMemberNameInAnyCase_ReturnsTheMember(string json, ZoneTypeEnum expected) =>
        Assert.Equal(expected, Read<ZoneTypeEnum>(json));

    [Fact]
    public void Read_ForAMemberNameOfABuiltInConverterEnum_ReturnsTheMember() =>
        Assert.Equal(SecondEnum.B, Read<SecondEnum>("\"b\""));

    [Theory]
    [InlineData("\"RSASHA256\"", DnssecAlgorithmEnum.RsaSha256)]
    [InlineData("\"rsasha256\"", DnssecAlgorithmEnum.RsaSha256)]
    [InlineData("\"RsaSha256\"", DnssecAlgorithmEnum.RsaSha256)]
    [InlineData("\"DSA-NSEC3-SHA1\"", DnssecAlgorithmEnum.DsaNsec3Sha1)]
    [InlineData("\"ECDSAP256SHA256\"", DnssecAlgorithmEnum.EcdsaP256Sha256)]
    public void Read_ForADeclaredWireName_ReturnsTheMember(
        string json,
        DnssecAlgorithmEnum expected
    ) => Assert.Equal(expected, Read<DnssecAlgorithmEnum>(json));

    [Theory]
    [InlineData(
        "\"Sunday, Monday\"",
        DnsScheduleDayOfWeekEnum.Sunday | DnsScheduleDayOfWeekEnum.Monday
    )]
    [InlineData(
        "\"sunday,monday\"",
        DnsScheduleDayOfWeekEnum.Sunday | DnsScheduleDayOfWeekEnum.Monday
    )]
    [InlineData("\"Friday\"", DnsScheduleDayOfWeekEnum.Friday)]
    public void Read_ForAFlagsCommaListOfNames_CombinesThem(
        string json,
        DnsScheduleDayOfWeekEnum expected
    ) => Assert.Equal(expected, Read<DnsScheduleDayOfWeekEnum>(json));

    [Theory]
    [InlineData("\"Sunday, 3\"")]
    [InlineData("\"Sunday,,Monday\"")]
    [InlineData("\"Sunday, Nope\"")]
    [InlineData("\"\"")]
    public void Read_ForAFlagsListWithABadPart_ThrowsJsonException(string json) =>
        Assert.Throws<JsonException>(() => Read<DnsScheduleDayOfWeekEnum>(json));

    [Fact]
    public void Read_ForAFlagsListWithADigitPart_ThrowsJsonException() =>
        Assert.Throws<JsonException>(() => Read<DnsScheduleDayOfWeekEnum>("\"Monday,4\""));

    [Fact]
    public void Read_ForACommaListOnANonFlagsEnum_ThrowsJsonException() =>
        Assert.Throws<JsonException>(() => Read<SecondEnum>("\"A,B\""));

    [Fact]
    public void Read_ForAnUnknownName_ThrowsJsonExceptionListingTheValidNames()
    {
        JsonException exception = Assert.Throws<JsonException>(() =>
            Read<ZoneTypeEnum>("\"Nope\"")
        );

        Assert.Contains("'Nope'", exception.Message);
        Assert.Contains(RequestEnumNameJsonConverter<ZoneTypeEnum>.ValidNames, exception.Message);
        Assert.Contains("Primary", RequestEnumNameJsonConverter<ZoneTypeEnum>.ValidNames);
    }

    [Fact]
    public void Read_ForANullableEnumWithNull_ReturnsNull() =>
        Assert.Null(Read<DnssecAlgorithmEnum?>("null"));

    [Fact]
    public void ValidNames_ForAnEnumWithWireNames_ListsTheWrittenNames()
    {
        string[] names = RequestEnumNameJsonConverter<DnssecAlgorithmEnum>.ValidNames.Split(", ");

        Assert.Contains("RSASHA256", names);
        Assert.Contains("ECDSAP256SHA256", names);
    }

    [Fact]
    public void Write_ForEveryValueIncludingAnUnnamedCode_MatchesTheAttributeConverter()
    {
        JsonSerializerOptions defaults = new();

        foreach (
            DnsResourceRecordTypeEnum value in Enum.GetValues<DnsResourceRecordTypeEnum>()
                .Append((DnsResourceRecordTypeEnum)65280)
        )
            Assert.Equal(
                JsonSerializer.Serialize(value, defaults),
                JsonSerializer.Serialize(value, requestOptions)
            );

        Assert.Equal(
            "\"65280\"",
            JsonSerializer.Serialize((DnsResourceRecordTypeEnum)65280, requestOptions)
        );

        foreach (
            DnsScheduleDayOfWeekEnum value in new[]
            {
                DnsScheduleDayOfWeekEnum.Sunday | DnsScheduleDayOfWeekEnum.Monday,
                DnsScheduleDayOfWeekEnum.None,
                (DnsScheduleDayOfWeekEnum)1000,
            }
        )
            Assert.Equal(
                JsonSerializer.Serialize(value, defaults),
                JsonSerializer.Serialize(value, requestOptions)
            );
    }

    [Fact]
    public void CanConvert_ForAnEnumWithoutAStringConverterAttribute_ReturnsFalse()
    {
        RequestEnumNameJsonConverterFactory factory = new();

        Assert.False(factory.CanConvert(typeof(PlainEnum)));
        Assert.False(factory.CanConvert(typeof(string)));
        Assert.True(factory.CanConvert(typeof(ZoneTypeEnum)));
        Assert.True(factory.CanConvert(typeof(DnsScheduleDayOfWeekEnum)));
    }

    [Fact]
    public void Read_ForAnEnumWithoutAStringConverterAttribute_KeepsDefaultHandling() =>
        Assert.Equal(PlainEnum.One, Read<PlainEnum>("1"));

    [Fact]
    public void Read_ThroughTheOwnConverterOfAType_StillAcceptsADigitString()
    {
        // The shared converter is untouched: only the request options refuse digits.
        Assert.Equal(
            ZoneTypeEnum.Primary,
            JsonSerializer.Deserialize<ZoneTypeEnum>(
                $"\"{(int)ZoneTypeEnum.Primary}\"",
                new JsonSerializerOptions()
            )
        );
    }
}
