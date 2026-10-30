using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Server.Authoritative.Journal;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Journal;

/// <summary>
///     This class tests <see cref="RecordDataValidator" /> directly: which record types and
///     <c>data</c> shapes <c>RecordsController</c> accepts, and the
///     <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" /> it throws otherwise.
/// </summary>
public sealed class RecordDataValidatorTests
{
    [Fact]
    public void EncodeDataOrThrow_WithDataMatchingItsDeclaredType_ReturnsCanonicalPascalCaseJson()
    {
        JsonElement data = JsonDocument.Parse("""{"address":"203.0.113.5"}""").RootElement;

        string dataJson = RecordDataValidator.EncodeDataOrThrow(DnsResourceRecordTypeEnum.A, data);

        Assert.Contains("\"Address\"", dataJson, StringComparison.Ordinal);
        Assert.Contains("203.0.113.5", dataJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DnsResourceRecordTypeEnum.A, """{"address":"not-an-ip"}""", "address")]
    [InlineData(DnsResourceRecordTypeEnum.A, """{"addr":"203.0.113.5"}""", "address")]
    [InlineData(DnsResourceRecordTypeEnum.Aaaa, """{"address":"203.0.113.5x"}""", "address")]
    [InlineData(DnsResourceRecordTypeEnum.Txt, """{"text":"hello"}""", "values")]
    [InlineData(DnsResourceRecordTypeEnum.Txt, """{"values":null}""", "values")]
    [InlineData(DnsResourceRecordTypeEnum.Cname, "{}", "target")]
    [InlineData(DnsResourceRecordTypeEnum.MX, """{"preference":10}""", "exchange")]
    [InlineData(DnsResourceRecordTypeEnum.Srv, """{"priority":1,"weight":1,"port":80}""", "target")]
    [InlineData(DnsResourceRecordTypeEnum.Caa, """{"isCritical":false,"tag":"issue"}""", "value")]
    public void EncodeDataOrThrow_WithARequiredFieldMissingOrUnparsable_ThrowsAndNamesTheField(
        DnsResourceRecordTypeEnum type,
        string json,
        string field
    )
    {
        JsonElement data = JsonDocument.Parse(json).RootElement;

        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataOrThrow(type, data)
        );

        Assert.Contains($"'{field}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeDataOrThrow_WithNoDataAtAll_ThrowsValidationException()
    {
        JsonElement undefined = default;

        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataOrThrow(DnsResourceRecordTypeEnum.A, undefined)
        );

        Assert.Equal("'data' is required.", exception.Message);
    }

    [Fact]
    public void EncodeDataOrThrow_ForARecordTypeWithNoRegisteredCodec_ThrowsValidationException()
    {
        JsonElement data = JsonDocument.Parse("{}").RootElement;

        // DnsResourceRecordTypeEnum.Opt has no registered codec at all -- confirmed directly
        // against DnsRecordDataCodecRegistry rather than assumed -- so this exercises the
        // "not supported for storage" branch a real, currently-unimplemented record type
        // triggers.
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataOrThrow(DnsResourceRecordTypeEnum.Opt, data)
        );

        Assert.Contains(
            "is not supported for storage",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithMalformedJson_ThrowsValidationExceptionNamingTheType()
    {
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(
                DnsResourceRecordTypeEnum.A,
                "{ this is not valid json"
            )
        );

        Assert.Contains(
            "does not match the shape a 'A' record expects",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithTheJsonNullLiteral_ThrowsValidationException()
    {
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(DnsResourceRecordTypeEnum.A, "null")
        );

        Assert.Contains(
            "does not match the shape a 'A' record expects",
            exception.Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAlreadyCanonicalPascalCaseJson_RoundTripsCorrectly()
    {
        string dataJson = RecordDataValidator.EncodeDataFromJsonOrThrow(
            DnsResourceRecordTypeEnum.Cname,
            """{"Target":"target.example.com"}"""
        );

        Assert.Contains("target.example.com", dataJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"target":"a..b."}""")]
    [InlineData(
        """{"target":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example."}"""
    )]
    public void EncodeDataFromJsonOrThrow_WithAMalformedDnsName_ThrowsValidationException(
        string json
    )
    {
        Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(DnsResourceRecordTypeEnum.Cname, json)
        );
    }

    /// <summary>
    ///     This method builds the JSON of an APL record from (family, prefix length, base64 part)
    ///     triples, as the REST API receives it (a byte array is base64 text).
    /// </summary>
    private static string AplJson(params (int Family, int Prefix, string PartBase64)[] items) =>
        "{\"items\":["
        + string.Join(
            ",",
            items.Select(item =>
                $"{{\"addressFamily\":{item.Family},\"prefixLength\":{item.Prefix},\"negated\":false,\"addressFamilyDependentPart\":\"{item.PartBase64}\"}}"
            )
        )
        + "]}";

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAnAplPartThatEndsInAZeroOctet_NamesTheItem()
    {
        // 0A 00 00 is 10.0.0.0/24 padded: RFC 3123 Section 4 forbids the trailing zero octets.
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(
                DnsResourceRecordTypeEnum.Apl,
                AplJson((1, 24, "CgAA"))
            )
        );

        Assert.Contains("APL item 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithACanonicalAplPart_IsAccepted()
    {
        // 0A is 10.0.0.0/24 with its trailing zero octets dropped.
        string dataJson = RecordDataValidator.EncodeDataFromJsonOrThrow(
            DnsResourceRecordTypeEnum.Apl,
            AplJson((1, 24, "Cg=="))
        );

        Assert.Contains("\"Items\"", dataJson, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAnEmptyAplPart_IsAccepted()
    {
        // 0.0.0.0/0 has no significant octet at all.
        RecordDataValidator.EncodeDataFromJsonOrThrow(
            DnsResourceRecordTypeEnum.Apl,
            AplJson((1, 0, ""))
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAnIpv6AplPartLongerThanSixteenOctets_IsRefused()
    {
        // 17 octets of 0x01.
        string part = Convert.ToBase64String(Enumerable.Repeat((byte)1, 17).ToArray());

        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(
                DnsResourceRecordTypeEnum.Apl,
                AplJson((2, 128, part))
            )
        );

        Assert.Contains("APL item 1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAnIpv4AplPrefixLongerThanThirtyTwoBits_IsRefused()
    {
        Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(
                DnsResourceRecordTypeEnum.Apl,
                AplJson((1, 33, "Cg=="))
            )
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithAnAplItemOfAnotherFamily_PassesUnchecked()
    {
        // Family 3 is unknown to this rule, so its bytes are not judged.
        RecordDataValidator.EncodeDataFromJsonOrThrow(
            DnsResourceRecordTypeEnum.Apl,
            AplJson((3, 8, "AA=="))
        );
    }

    [Fact]
    public void EncodeDataFromJsonOrThrow_WithABadSecondAplItem_NamesItemTwo()
    {
        ValidationException exception = Assert.Throws<ValidationException>(() =>
            RecordDataValidator.EncodeDataFromJsonOrThrow(
                DnsResourceRecordTypeEnum.Apl,
                AplJson((1, 24, "Cg=="), (1, 24, "CgAA"))
            )
        );

        Assert.Contains("APL item 2", exception.Message, StringComparison.Ordinal);
    }
}
