using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Rewrites.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites.Tests;

/// <summary>
///     This class covers <see cref="IDnsRewriteEngine.TryBuildNoDataForNxDomainAsync" /> on
///     <see cref="DnsRewriteEngine" />: a name that exists only through a rewrite of another type
///     answers NODATA, not NXDOMAIN (RFC 2308 §2.1, §2.2). The "resolved" response is built in
///     the test, in the shape a resolver returns. The fixture is the one of
///     <see cref="DnsRewriteEngineTests" />: in-memory repositories, no sockets.
/// </summary>
public class DnsRewriteEngineNoDataTests
{
    private const string ZoneSoaOwner = "test";

    /// <summary>
    ///     This method builds an NXDOMAIN response as a resolver returns it: the question, and the
    ///     zone's SOA in the authority section when <paramref name="soa" /> is given.
    /// </summary>
    private static DnsMessage Nxdomain(
        DnsMessage request,
        DnsResourceRecord soa = null,
        params DnsResourceRecord[] extraAuthority
    )
    {
        DnsMessage resolved = new()
        {
            Id = request.Id,
            IsResponse = true,
            RecursionDesired = true,
            RecursionAvailable = true,
            ResponseCode = DnsResponseCodeEnum.NxDomain,
            Questions = [.. request.Questions],
        };

        if (soa is not null)
            resolved.AuthorityRecords.Add(soa);

        resolved.AuthorityRecords.AddRange(extraAuthority);
        return resolved;
    }

    private static DnsResourceRecord Soa(uint ttl, uint minimum) =>
        new(
            DnsName.Parse(ZoneSoaOwner),
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(
                    DnsName.Parse("ns.test"),
                    DnsName.Parse("hostmaster.test"),
                    1,
                    3600,
                    600,
                    86400,
                    minimum
                )
            )
        );

    private static DnsResourceRecord Opaque(DnsResourceRecordTypeEnum type) =>
        new(DnsName.Parse("x.test"), type, DnsClassEnum.Internet, 3600, [0, 0, 0]);

    /// <summary>
    ///     This method builds an RRSIG record whose "type covered" field (RFC 4034 §3.1) is
    ///     <paramref name="covered" />; the rest of the data is filler the engine never reads.
    /// </summary>
    private static DnsResourceRecord Rrsig(DnsResourceRecordTypeEnum covered) =>
        new(
            DnsName.Parse("x.test"),
            DnsResourceRecordTypeEnum.RrSig,
            DnsClassEnum.Internet,
            3600,
            [(byte)((ushort)covered >> 8), (byte)(ushort)covered, 8, 2, 0, 0, 0, 0]
        );

    private static Task<DnsMessage> ConvertAsync(
        DnsRewriteEngineTests.Fixture fixture,
        DnsMessage request,
        DnsMessage resolved
    )
    {
        IDnsRewriteEngine engine = fixture.Engine;
        return engine.TryBuildNoDataForNxDomainAsync(request, resolved).AsTask();
    }

    [Fact]
    public async Task ARewrite_ForAnAaaaQueryThatResolvedNxdomain_AnswersNoErrorWithNoAnswers()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(fixture, request, Nxdomain(request));

        Assert.NotNull(response);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
        Assert.Equal(request.Id, response.Id);
        Assert.True(response.IsResponse);
        Assert.Single(response.Questions);
    }

    [Fact]
    public async Task AaaaRewrite_ForAnAQueryThatResolvedNxdomain_AnswersNoError()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "2001:db8::1");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.A
        );

        DnsMessage response = await ConvertAsync(fixture, request, Nxdomain(request));

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    [Fact]
    public async Task ARewrite_ForATxtQueryThatResolvedNxdomain_AnswersNoError()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Txt
        );

        DnsMessage response = await ConvertAsync(fixture, request, Nxdomain(request));

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
    }

    [Fact]
    public async Task ARewrite_ForAnAaaaQueryThatResolvedWithData_IsNotConverted()
    {
        // A positive answer is not NXDOMAIN, and the pipeline asks only about NXDOMAIN; the
        // engine still refuses a response with another code.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );
        DnsMessage resolved = Nxdomain(request);
        resolved.ResponseCode = DnsResponseCodeEnum.NoError;
        resolved.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("x.test"),
                DnsResourceRecordTypeEnum.Aaaa,
                DnsClassEnum.Internet,
                60,
                IPAddress.Parse("2001:db8::9").GetAddressBytes()
            )
        );

        Assert.Null(await ConvertAsync(fixture, request, resolved));
    }

    [Fact]
    public async Task NxdomainWithAnAnswer_ACnameChainToAMissingName_IsNotConverted()
    {
        // The real name is a CNAME, so it exists on its own; the NXDOMAIN is for the target.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );
        DnsMessage resolved = Nxdomain(request);
        resolved.Answers.Add(Opaque(DnsResourceRecordTypeEnum.Cname));

        Assert.Null(await ConvertAsync(fixture, request, resolved));
    }

    [Fact]
    public async Task NoRewriteForTheName_LeavesTheNxdomain()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("other.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task ExactRewrite_ForASubdomainQuery_LeavesTheNxdomain()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "sub.x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task WildcardARewrite_ForAnAaaaQueryInsideIt_AnswersNoError()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("*.x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "a.x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(fixture, request, Nxdomain(request));

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
    }

    [Fact]
    public async Task WildcardARewrite_ForAnAaaaQueryOutsideIt_LeavesTheNxdomain()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("*.x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task CnameRewrite_ForATxtQuery_LeavesTheNxdomain()
    {
        // A CNAME rewrite answers A, AAAA and CNAME itself; it is not the "other type" case.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "target.test");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Txt
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task ARewrite_ForAnAQuery_IsNotConverted()
    {
        // The rewrite answers this type itself, so no NODATA applies.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.A
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task DisabledRewrite_DoesNotMakeTheNameExist()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5", isDisabled: true);
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task EngineSwitchedOff_LeavesTheNxdomain()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        fixture.Options.Enable = false;
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        Assert.Null(await ConvertAsync(fixture, request, Nxdomain(request)));
    }

    [Fact]
    public async Task ClientScopedARewrite_ForAnotherClient_LeavesTheNxdomain()
    {
        FakeClientIdentityResolver resolver = new FakeClientIdentityResolver()
            .WithResolution(
                IPAddress.Parse("192.0.2.10"),
                new ClientIdentityResolution("aa:bb:cc:dd:ee:01", InternetProtocolVersionEnum.IPv4)
            )
            .WithResolution(
                IPAddress.Parse("192.0.2.11"),
                new ClientIdentityResolution("aa:bb:cc:dd:ee:02", InternetProtocolVersionEnum.IPv4)
            );
        DnsRewriteEngineTests.Fixture fixture = new(resolver);
        Guid owner = fixture.AddClient("aa:bb:cc:dd:ee:01").Id;
        fixture.AddClient("aa:bb:cc:dd:ee:02");
        fixture.AddClientScoped("x.test", "203.0.113.5", owner);
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );
        IDnsRewriteEngine engine = fixture.Engine;

        DnsMessage other = await engine.TryBuildNoDataForNxDomainAsync(
            request,
            Nxdomain(request),
            IPAddress.Parse("192.0.2.11")
        );
        DnsMessage same = await engine.TryBuildNoDataForNxDomainAsync(
            request,
            Nxdomain(request),
            IPAddress.Parse("192.0.2.10")
        );

        Assert.Null(other);
        Assert.NotNull(same);
    }

    [Fact]
    public async Task Soa_FromTheNxdomainAuthority_IsKeptWithItsTtlCappedToTheSoaMinimum()
    {
        // RFC 2308 §3: the NODATA answer carries the zone's SOA, with a TTL that is the lesser
        // of the SOA's TTL and its MINIMUM field.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Options.TimeToLiveSeconds = 3600;
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(
            fixture,
            request,
            Nxdomain(request, Soa(ttl: 3600, minimum: 120))
        );

        DnsResourceRecord soa = Assert.Single(response.AuthorityRecords);
        Assert.Equal(DnsResourceRecordTypeEnum.Soa, soa.Type);
        Assert.Equal(120u, soa.TimeToLive);
    }

    [Fact]
    public async Task Soa_WithATtlBelowItsMinimum_KeepsTheLowerTtl()
    {
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Options.TimeToLiveSeconds = 3600;
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(
            fixture,
            request,
            Nxdomain(request, Soa(ttl: 30, minimum: 900))
        );

        Assert.Equal(30u, Assert.Single(response.AuthorityRecords).TimeToLive);
    }

    [Fact]
    public async Task Soa_AboveTheRewriteTtl_IsCappedToTheRewriteTtl()
    {
        // The rewrite's answers live for TimeToLiveSeconds; a negative answer must not outlive a
        // rewrite for the other type that the operator adds next.
        DnsRewriteEngineTests.Fixture fixture = new(); // 60 seconds
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(
            fixture,
            request,
            Nxdomain(request, Soa(ttl: 3600, minimum: 3600))
        );

        Assert.Equal(60u, Assert.Single(response.AuthorityRecords).TimeToLive);
    }

    [Fact]
    public async Task NxdomainWithoutSoa_AnswersNoDataWithoutSoa()
    {
        // RFC 2308 §3 says a NODATA answer should carry the SOA when the server has one. With no
        // SOA to hand, none is invented; such an answer is simply not cacheable (§5).
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );

        DnsMessage response = await ConvertAsync(fixture, request, Nxdomain(request));

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.AuthorityRecords);
    }

    [Fact]
    public async Task ValidatedNxdomain_BecomesNoDataWithAdClearedAndNoDenialProof()
    {
        // RFC 4035 §3.2.3: AD says every RRset in the answer and authority sections is
        // authentic. An NSEC or NSEC3 proof (RFC 4034 §4, RFC 5155 §7.2.2) proves the name does
        // not exist, which the rewrite contradicts, so it must not reach the client.
        DnsRewriteEngineTests.Fixture fixture = new();
        fixture.Options.TimeToLiveSeconds = 3600;
        fixture.Add("x.test", "203.0.113.5");
        DnsMessage request = DnsRewriteEngineTests.BuildRequest(
            "x.test",
            DnsResourceRecordTypeEnum.Aaaa
        );
        request.Edns = new DnsEdnsOptions(0, 1232, true, []);
        DnsMessage resolved = Nxdomain(
            request,
            Soa(3600, 300),
            Rrsig(DnsResourceRecordTypeEnum.Soa),
            Opaque(DnsResourceRecordTypeEnum.Nsec),
            Rrsig(DnsResourceRecordTypeEnum.Nsec),
            Opaque(DnsResourceRecordTypeEnum.Nsec3),
            Rrsig(DnsResourceRecordTypeEnum.Nsec3)
        );
        resolved.AuthenticData = true;
        resolved.IsAuthoritativeAnswer = true;
        resolved.Edns = request.Edns;

        DnsMessage response = await ConvertAsync(fixture, request, resolved);

        Assert.False(response.AuthenticData);
        Assert.False(response.IsAuthoritativeAnswer);
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal(
            [DnsResourceRecordTypeEnum.Soa, DnsResourceRecordTypeEnum.RrSig],
            response.AuthorityRecords.Select(record => record.Type).ToArray()
        );
        // RFC 2308 §3: the RRSIG over the SOA is trimmed to the SOA's TTL.
        Assert.Equal(300u, response.AuthorityRecords[1].TimeToLive);
        Assert.True(response.Edns.DnssecOk);

        // The input is untouched: it can be a shared cache entry.
        Assert.Equal(DnsResponseCodeEnum.NxDomain, resolved.ResponseCode);
        Assert.True(resolved.AuthenticData);
        Assert.Equal(6, resolved.AuthorityRecords.Count);
    }
}
