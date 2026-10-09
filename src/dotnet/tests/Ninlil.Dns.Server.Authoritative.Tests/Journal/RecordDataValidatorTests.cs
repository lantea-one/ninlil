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
}
