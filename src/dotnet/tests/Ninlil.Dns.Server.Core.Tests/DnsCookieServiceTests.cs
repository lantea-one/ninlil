using System.Net;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Cookies;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="DnsCookieService" /> (RFC 7873) directly with fixed options:
///     what <see cref="DnsCookieService.Evaluate" /> decides with cookies disabled, optional and
///     required, and what <see cref="DnsCookieService.Verify" /> accepts across source addresses,
///     tampering, bad lengths and secret rotations.
/// </summary>
public class DnsCookieServiceTests
{
    private static readonly IPAddress ClientAddress = IPAddress.Parse("203.0.113.55");

    private static DnsCookieService CreateService(bool require = false) =>
        new(Options.Create(new DnsCookieOptions { Enable = true, Require = require }));

    private static DnsMessage BuildQuery(DnsEdnsOption cookieOption = null) =>
        new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            Edns = cookieOption is null ? null : new DnsEdnsOptions(0, 1232, false, [cookieOption]),
        };

    private static byte[] NewClientCookie() =>
        Enumerable.Range(1, 8).Select(i => (byte)i).ToArray();

    [Fact]
    public void EvaluateIsANoOpWhenDisabled()
    {
        DnsCookieService service = new(Options.Create(new DnsCookieOptions { Enable = false }));

        DnsCookieOutcome outcome = service.Evaluate(BuildQuery(), ClientAddress);

        Assert.False(outcome.ShouldRefuse);
        Assert.False(outcome.ShouldRespondBadCookie);
        Assert.Null(outcome.ResponseOption);
    }

    [Fact]
    public void EvaluateAttachesNothingWhenTheRequestHasNoCookieAtAll()
    {
        DnsCookieService service = CreateService();

        DnsCookieOutcome outcome = service.Evaluate(BuildQuery(), ClientAddress);

        Assert.False(outcome.ShouldRefuse);
        Assert.False(outcome.ShouldRespondBadCookie);
        Assert.Null(outcome.ResponseOption);
    }

    [Fact]
    public void EvaluateEchoesAFreshServerCookieForAClientOnlyCookie()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();

        DnsCookieOutcome outcome = service.Evaluate(
            BuildQuery(new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, clientCookie)),
            ClientAddress
        );

        Assert.False(outcome.ShouldRefuse);
        Assert.False(outcome.ShouldRespondBadCookie);
        Assert.NotNull(outcome.ResponseOption);
        Assert.Equal(16, outcome.ResponseOption.Data.Length);
        Assert.Equal(clientCookie, outcome.ResponseOption.Data[..8]);
    }

    [Fact]
    public void VerifyAcceptsACookieThisServiceItselfIssued()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();

        DnsEdnsOption issued = service.BuildResponseOption(clientCookie, ClientAddress);

        Assert.Equal(DnsCookieVerificationResultEnum.Valid, service.Verify(issued, ClientAddress));
    }

    [Fact]
    public void VerifyRejectsACookieFromADifferentSourceAddress()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();

        DnsEdnsOption issued = service.BuildResponseOption(clientCookie, ClientAddress);

        Assert.Equal(
            DnsCookieVerificationResultEnum.Invalid,
            service.Verify(issued, IPAddress.Parse("203.0.113.99"))
        );
    }

    [Fact]
    public void VerifyRejectsATamperedServerCookie()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();
        DnsEdnsOption issued = service.BuildResponseOption(clientCookie, ClientAddress);

        byte[] tampered = [.. issued.Data];
        tampered[^1] ^= 0xFF;

        Assert.Equal(
            DnsCookieVerificationResultEnum.Invalid,
            service.Verify(new DnsEdnsOption(issued.Code, tampered), ClientAddress)
        );
    }

    [Theory]
    [InlineData(7)]
    [InlineData(9)]
    [InlineData(15)]
    [InlineData(41)]
    public void VerifyRejectsAnImpossibleCookieLength(int length) =>
        Assert.Equal(
            DnsCookieVerificationResultEnum.Malformed,
            CreateService()
                .Verify(
                    new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, new byte[length]),
                    ClientAddress
                )
        );

    [Fact]
    public void VerifyStillAcceptsACookieIssuedUnderTheImmediatelyPrecedingSecret()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();

        DnsEdnsOption issuedBeforeRotation = service.BuildResponseOption(
            clientCookie,
            ClientAddress
        );
        service.RotateSecret();

        Assert.Equal(
            DnsCookieVerificationResultEnum.Valid,
            service.Verify(issuedBeforeRotation, ClientAddress)
        );
    }

    [Fact]
    public void VerifyRejectsACookieFromTwoRotationsAgo()
    {
        DnsCookieService service = CreateService();
        byte[] clientCookie = NewClientCookie();

        DnsEdnsOption issued = service.BuildResponseOption(clientCookie, ClientAddress);
        service.RotateSecret();
        service.RotateSecret();

        Assert.Equal(
            DnsCookieVerificationResultEnum.Invalid,
            service.Verify(issued, ClientAddress)
        );
    }

    [Fact]
    public void EvaluateRefusesWhenCookiesAreRequiredAndNoneWasPresented()
    {
        DnsCookieService service = CreateService(require: true);

        DnsCookieOutcome outcome = service.Evaluate(BuildQuery(), ClientAddress);

        Assert.True(outcome.ShouldRefuse);
        Assert.False(outcome.ShouldRespondBadCookie);
        Assert.Null(outcome.ResponseOption);
    }

    [Fact]
    public void EvaluateRespondsBadCookieWhenCookiesAreRequiredAndOnlyAClientCookieWasPresented()
    {
        DnsCookieService service = CreateService(require: true);
        byte[] clientCookie = NewClientCookie();

        DnsCookieOutcome outcome = service.Evaluate(
            BuildQuery(new DnsEdnsOption((ushort)EdnsOptionCodeEnum.Cookie, clientCookie)),
            ClientAddress
        );

        Assert.False(outcome.ShouldRefuse);
        Assert.True(outcome.ShouldRespondBadCookie);
        Assert.NotNull(outcome.ResponseOption);
    }

    [Fact]
    public void EvaluateAllowsThroughWhenCookiesAreRequiredAndAValidOneWasPresented()
    {
        DnsCookieService service = CreateService(require: true);
        byte[] clientCookie = NewClientCookie();
        DnsEdnsOption validCookie = service.BuildResponseOption(clientCookie, ClientAddress);

        DnsCookieOutcome outcome = service.Evaluate(BuildQuery(validCookie), ClientAddress);

        Assert.False(outcome.ShouldRefuse);
        Assert.False(outcome.ShouldRespondBadCookie);
        Assert.NotNull(outcome.ResponseOption);
    }
}
