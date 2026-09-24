using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Server.Authoritative.Journal;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Journal;

/// <summary>
///     This class covers <see cref="RecordDataValidator" /> directly -- the record-type/
///     codec-level validation logic <c>Ninlil.Web.Controllers.RecordsController</c>
///     calls into for its own <c>data</c> field, now converted to
///     throw this project's own custom exception hierarchy directly, since neither of this
///     class's two methods has any caller outside a REST-facing write path -- unlike
///     <see cref="IZoneUpdateService" /> itself, this class shares no code with any
///     DNS-protocol-triggered write path, so there is no shared error-signaling behavior to
///     preserve here.
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
}
