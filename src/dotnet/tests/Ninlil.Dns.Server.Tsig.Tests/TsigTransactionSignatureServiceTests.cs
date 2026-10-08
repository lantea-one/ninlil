using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig.Tests;

/// <summary>
///     This class tests <see cref="TsigTransactionSignatureService" />, the adapter the query
///     pipeline calls, on the shared SQLite host (<see cref="TsigServiceTestFixture" />). It
///     checks that signing never changes the reply it is given and that
///     <see cref="IDnsTransactionSignatureService.GetSignatureLength" /> matches the octets the
///     TSIG record really takes on the wire for each kind of outcome.
/// </summary>
public class TsigTransactionSignatureServiceTests(TsigServiceTestFixture fixture)
    : IClassFixture<TsigServiceTestFixture>
{
    private static readonly IReadOnlyDictionary<string, DnsName> Algorithms = new Dictionary<
        string,
        DnsName
    >
    {
        ["hmac-sha1"] = DnsTsigOptions.HmacSha1AlgorithmName,
        ["hmac-sha256"] = DnsTsigOptions.HmacSha256AlgorithmName,
        ["hmac-sha384"] = DnsTsigOptions.HmacSha384AlgorithmName,
        ["hmac-sha512"] = DnsTsigOptions.HmacSha512AlgorithmName,
    };

    private async Task<TsigKeyEntity> CreateKeyAsync(string algorithmName)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDataProtector protector = scope
            .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");

        byte[] rawSecret = new byte[64];
        Random.Shared.NextBytes(rawSecret);

        TsigKeyEntity key = new()
        {
            Id = Guid.NewGuid(),
            Name = $"adapter-key-{Guid.NewGuid()}",
            AlgorithmName = algorithmName,
            SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await keyRepository.AddAsync(key);
        await unitOfWork.SaveChangesAsync();

        return key;
    }

    private static DnsMessage BuildResponse() =>
        new()
        {
            Id = 1234,
            IsResponse = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com"),
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet
                ),
            },
        };

    /// <summary>
    ///     This method builds the request the adapter would see: signed, then written and read
    ///     back so it carries the raw bytes and the TSIG offset.
    /// </summary>
    private async Task<DnsQueryContext> BuildSignedContextAsync(TsigKeyEntity key)
    {
        await using AsyncServiceScope scope = fixture.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        DnsMessage request = BuildResponse();
        request.IsResponse = false;
        DnsMessage signed = await tsigService.SignAsync(request, key.Name, request.Id);

        byte[] raw = DnsMessageWriter.Write(signed);
        DnsMessage reparsed = DnsMessageReader.Read(raw, out int tsigOffset);

        return new DnsQueryContext(
            reparsed,
            DnsTransportProtocolEnum.Udp,
            System.Net.IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            raw,
            tsigOffset
        );
    }

    private static int TsigWireLength(DnsMessage signed)
    {
        DnsTsigOptions tsig = signed.Tsig;
        signed.Tsig = null;
        int without = DnsMessageWriter.Write(signed).Length;
        signed.Tsig = tsig;

        return DnsMessageWriter.Write(signed).Length - without;
    }

    [Theory]
    [InlineData("hmac-sha1")]
    [InlineData("hmac-sha256")]
    [InlineData("hmac-sha384")]
    [InlineData("hmac-sha512")]
    public async Task SignAsync_ForAVerifiedRequest_AddsExactlyTheAnnouncedNumberOfOctets(
        string algorithmName
    )
    {
        TsigKeyEntity key = await CreateKeyAsync(algorithmName);
        DnsQueryContext context = await BuildSignedContextAsync(key);
        IDnsTransactionSignatureService service =
            fixture.Services.GetRequiredService<IDnsTransactionSignatureService>();

        DnsTransactionSignature signature = await service.VerifyAsync(context);
        Assert.True(signature.IsVerified);

        DnsMessage response = BuildResponse();
        DnsMessage signed = await service.SignAsync(response, signature);

        Assert.Null(response.Tsig);
        Assert.NotNull(signed.Tsig);
        Assert.Equal(Algorithms[algorithmName], signed.Tsig.AlgorithmName);
        Assert.Equal(TsigWireLength(signed), service.GetSignatureLength(signature));
    }

    [Fact]
    public async Task SignAsync_ForABadSignature_AnnouncesTheLengthOfTheUnsignedErrorRecord()
    {
        TsigKeyEntity key = await CreateKeyAsync("hmac-sha256");
        DnsQueryContext context = await BuildSignedContextAsync(key);
        DnsTsigOptions original = context.Request.Tsig;
        byte[] wrongMac = (byte[])original.Mac.Clone();
        wrongMac[0] ^= 0xFF;
        DnsMessage tampered = context.Request;
        tampered.Tsig = new DnsTsigOptions(
            original.KeyName,
            original.AlgorithmName,
            original.TimeSigned,
            original.Fudge,
            wrongMac,
            original.OriginalId,
            original.Error,
            original.OtherData
        );
        IDnsTransactionSignatureService service =
            fixture.Services.GetRequiredService<IDnsTransactionSignatureService>();

        DnsTransactionSignature signature = await service.VerifyAsync(
            context with
            {
                Request = tampered,
            }
        );
        Assert.False(signature.IsVerified);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, signature.Error);

        DnsMessage signed = await service.SignAsync(BuildResponse(), signature);

        Assert.Equal(DnsResponseCodeEnum.NotAuthoritative, signed.ResponseCode);
        Assert.Equal(DnsResponseCodeEnum.BadVersionOrBadSignature, signed.Tsig.Error);
        Assert.Empty(signed.Tsig.Mac);
        Assert.Equal(TsigWireLength(signed), service.GetSignatureLength(signature));
    }

    [Fact]
    public async Task SignAsync_ForAStaleRequest_AnnouncesTheLengthOfTheSignedBadTimeRecord()
    {
        TsigKeyEntity key = await CreateKeyAsync("hmac-sha256");
        DnsQueryContext context = await BuildSignedContextAsync(key);
        IDnsTransactionSignatureService service =
            fixture.Services.GetRequiredService<IDnsTransactionSignatureService>();

        // A MAC over the current time stays valid only while the clock stays put, so move the
        // shared clock forward past the fudge instead of editing the signed request.
        fixture.Clock.Advance(TimeSpan.FromMinutes(30));

        DnsTransactionSignature signature = await service.VerifyAsync(context);
        Assert.False(signature.IsVerified);
        Assert.Equal(DnsResponseCodeEnum.BadTime, signature.Error);

        DnsMessage signed = await service.SignAsync(BuildResponse(), signature);

        Assert.Equal(DnsResponseCodeEnum.BadTime, signed.Tsig.Error);
        Assert.Equal(6, signed.Tsig.OtherData.Length);
        Assert.NotEmpty(signed.Tsig.Mac);
        Assert.Equal(TsigWireLength(signed), service.GetSignatureLength(signature));
    }

    [Fact]
    public void GetSignatureLength_ForAFormatError_IsZero()
    {
        IDnsTransactionSignatureService service =
            fixture.Services.GetRequiredService<IDnsTransactionSignatureService>();
        DnsTsigOptions tsig = new(
            DnsName.Parse("k.example."),
            DnsTsigOptions.HmacSha256AlgorithmName,
            1,
            300,
            [1],
            1,
            DnsResponseCodeEnum.NoError,
            []
        );

        Assert.Equal(
            0,
            service.GetSignatureLength(
                new DnsTransactionSignature(
                    "k.example.",
                    false,
                    DnsResponseCodeEnum.FormatError,
                    tsig
                )
            )
        );
    }
}
