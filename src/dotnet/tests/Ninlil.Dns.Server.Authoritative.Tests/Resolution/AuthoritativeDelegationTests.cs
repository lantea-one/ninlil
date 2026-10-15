using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Resolution;

/// <summary>
///     This class tests how <c>AuthoritativeQueryResolver</c> treats nested zone cuts (RFC 1034
///     §4.2.1, §4.3.2): data at or below the topmost cut belongs to the child, so a query there is
///     referred to the topmost cut's servers, and a CNAME is never followed into that data. It
///     queries a real in-process host built by <see cref="AuthoritativeTestZoneHost" /> over one
///     shared unsigned zone.
/// </summary>
public sealed class AuthoritativeDelegationTests : IAsyncLifetime
{
    private const string Zone = "nested.test";

    private AuthoritativeTestZoneHost _host;

    public async Task InitializeAsync()
    {
        _host = await AuthoritativeTestZoneHost.CreateAsync();
        await _host.AddZoneAsync(
            Zone,
            [
                TestRecord.Ns(Zone, $"ns1.{Zone}"),
                TestRecord.A($"ns1.{Zone}", "192.0.2.1"),
                // The outer cut, with name servers outside the zone.
                TestRecord.Ns($"b.{Zone}", "ns.other.example"),
                // A nested cut inside the outer one, with glue: occluded data.
                TestRecord.Ns($"a.b.{Zone}", $"ns.a.b.{Zone}"),
                TestRecord.A($"ns.a.b.{Zone}", "192.0.2.53"),
                TestRecord.Cname($"cn.{Zone}", $"ns.a.b.{Zone}"),
                // An empty non-terminal: _tcp exists only because an SRV owner lies below it.
                new TestRecord(
                    $"_sip._tcp.{Zone}",
                    DnsResourceRecordTypeEnum.Srv,
                    new SrvRecordData(0, 0, 5060, DnsName.Parse($"ns1.{Zone}"))
                ),
            ]
        );
        await _host.StartAsync();
    }

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    /// <summary>
    ///     This method asserts that a response is a referral to the given cut: not authoritative,
    ///     no answers, and NS records at that owner in the Authority section.
    /// </summary>
    private static void AssertReferralTo(DnsMessage response, string cut)
    {
        Assert.False(response.IsAuthoritativeAnswer);
        Assert.Empty(response.Answers);
        Assert.NotEmpty(response.AuthorityRecords);
        Assert.All(
            response.AuthorityRecords,
            record =>
            {
                Assert.Equal(DnsResourceRecordTypeEnum.NS, record.Type);
                Assert.Equal(DnsName.Parse(cut), record.Name);
            }
        );
    }

    [Fact]
    public async Task Query_BelowNestedCuts_IsReferredToTheTopmostCut()
    {
        AssertReferralTo(
            await _host.QueryAsync($"host.a.b.{Zone}", DnsResourceRecordTypeEnum.A),
            $"b.{Zone}"
        );
    }

    [Fact]
    public async Task NsQuery_AtANestedCut_IsReferredToTheTopmostCut()
    {
        AssertReferralTo(
            await _host.QueryAsync($"a.b.{Zone}", DnsResourceRecordTypeEnum.NS),
            $"b.{Zone}"
        );
    }

    [Fact]
    public async Task DsQuery_AtANestedCut_IsReferredToTheTopmostCut()
    {
        AssertReferralTo(
            await _host.QueryAsync($"a.b.{Zone}", DnsResourceRecordTypeEnum.DS),
            $"b.{Zone}"
        );
    }

    [Fact]
    public async Task Query_ForTheCnameTargetBelowACut_ReturnsTheCnameOnly()
    {
        DnsMessage response = await _host.QueryAsync($"cn.{Zone}", DnsResourceRecordTypeEnum.A);

        Assert.True(response.IsAuthoritativeAnswer);
        DnsResourceRecord cname = Assert.Single(response.Answers);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, cname.Type);
    }

    [Fact]
    public async Task Query_BelowASingleCut_IsReferredToThatCut()
    {
        AssertReferralTo(
            await _host.QueryAsync($"x.b.{Zone}", DnsResourceRecordTypeEnum.A),
            $"b.{Zone}"
        );
    }

    [Fact]
    public async Task NsQuery_AtTheTopmostCut_IsAnsweredAuthoritatively()
    {
        DnsMessage response = await _host.QueryAsync($"b.{Zone}", DnsResourceRecordTypeEnum.NS);

        Assert.True(response.IsAuthoritativeAnswer);
        Assert.Single(response.Answers);
    }

    [Fact]
    public async Task Query_ForAnEmptyNonTerminal_IsNoDataNotNxDomain()
    {
        DnsMessage response = await _host.QueryAsync($"_tcp.{Zone}", DnsResourceRecordTypeEnum.A);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
        Assert.True(response.IsAuthoritativeAnswer);
    }

    [Fact]
    public async Task Query_ForAnUnknownName_IsNxDomain()
    {
        DnsMessage response = await _host.QueryAsync($"nope.{Zone}", DnsResourceRecordTypeEnum.A);

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
    }
}
