using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Resolution;

/// <summary>
///     This class tests how <c>AuthoritativeQueryResolver</c> applies wildcards (RFC 4592): only
///     the wildcard at the closest encloser answers, a wildcard CNAME answers every type, and a
///     wildcard NODATA answer carries its DNSSEC proof (RFC 4035 §3.1.3.4, RFC 5155 §7.2.5). It
///     seeds the example zone of RFC 4592 §2.2.1 (plus <c>*.w.example</c> and a second zone
///     <c>wild.test</c> with a wildcard CNAME) into three real in-process hosts built by
///     <see cref="AuthoritativeTestZoneHost" />, one unsigned, one signed with NSEC and one with
///     NSEC3, and queries each of them directly. Proofs are checked with
///     <see cref="DenialOfExistenceValidator" />.
/// </summary>
public sealed class AuthoritativeWildcardTests : IAsyncLifetime
{
    private const string Zone = "example";
    private const string CnameZone = "wild.test";

    /// <summary>
    ///     This field holds one host per signing kind, filled by <see cref="InitializeAsync" />.
    /// </summary>
    private readonly Dictionary<TestZoneSigningKind, AuthoritativeTestZoneHost> _hosts = [];

    public async Task InitializeAsync()
    {
        foreach (
            (TestZoneSigningKind kind, TestZoneSigning signing) in new[]
            {
                (TestZoneSigningKind.None, TestZoneSigning.None),
                (TestZoneSigningKind.Nsec, TestZoneSigning.Nsec),
                (TestZoneSigningKind.Nsec3, TestZoneSigning.Nsec3(2, "AB")),
            }
        )
        {
            AuthoritativeTestZoneHost host = await AuthoritativeTestZoneHost.CreateAsync();
            await host.AddZoneAsync(
                Zone,
                [
                    TestRecord.Ns(Zone, "ns.example.com"),
                    TestRecord.Ns(Zone, "ns.example.net"),
                    Txt($"*.{Zone}", "this is a wildcard"),
                    Mx($"*.{Zone}", $"host1.{Zone}"),
                    Txt($"sub.*.{Zone}", "this is not a wildcard"),
                    TestRecord.A($"host1.{Zone}", "192.0.2.1"),
                    Srv($"_ssh._tcp.host1.{Zone}"),
                    Srv($"_ssh._tcp.host2.{Zone}"),
                    TestRecord.Ns($"subdel.{Zone}", "ns.example.com"),
                    TestRecord.Ns($"subdel.{Zone}", "ns.example.net"),
                    // A closer wildcard that lacks TXT: it shadows *.example for names under w.
                    TestRecord.A($"*.w.{Zone}", "192.0.2.9"),
                ],
                signing
            );
            await host.AddZoneAsync(
                CnameZone,
                [
                    TestRecord.Cname($"*.{CnameZone}", $"host1.{CnameZone}"),
                    TestRecord.A($"host1.{CnameZone}", "192.0.2.1"),
                ],
                signing
            );
            await host.StartAsync();
            _hosts[kind] = host;
        }
    }

    public async Task DisposeAsync()
    {
        foreach (AuthoritativeTestZoneHost host in _hosts.Values)
            await host.DisposeAsync();
    }

    private static TestRecord Txt(string owner, string text) =>
        new(owner, DnsResourceRecordTypeEnum.Txt, new TxtRecordData([text]));

    private static TestRecord Mx(string owner, string exchange) =>
        new(owner, DnsResourceRecordTypeEnum.MX, new MxRecordData(10, DnsName.Parse(exchange)));

    private static TestRecord Srv(string owner) =>
        new(
            owner,
            DnsResourceRecordTypeEnum.Srv,
            new SrvRecordData(0, 0, 22, DnsName.Parse($"host1.{Zone}"))
        );

    /// <summary>
    ///     This method asserts a NODATA answer: NoError, no answers, an SOA in the Authority.
    /// </summary>
    private static void AssertNoData(DnsMessage response)
    {
        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.True(response.IsAuthoritativeAnswer);
        Assert.Empty(response.Answers);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Soa
        );
    }

    /// <summary>
    ///     This method asserts an NXDOMAIN answer with an SOA in the Authority.
    /// </summary>
    private static void AssertNxDomain(DnsMessage response)
    {
        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.Empty(response.Answers);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.Soa
        );
    }

    /// <summary>
    ///     This method returns the NSEC or NSEC3 records of a response's Authority section.
    /// </summary>
    private static List<DnsResourceRecord> ProofRecords(DnsMessage response) =>
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
            ),
        ];

    [Theory]
    [InlineData(TestZoneSigningKind.None, "host3.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec, "host3.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec3, "host3.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.None, "foo.bar.example", DnsResourceRecordTypeEnum.Txt)]
    [InlineData(TestZoneSigningKind.Nsec, "foo.bar.example", DnsResourceRecordTypeEnum.Txt)]
    [InlineData(TestZoneSigningKind.Nsec3, "foo.bar.example", DnsResourceRecordTypeEnum.Txt)]
    public async Task Query_ForANameCoveredByTheWildcard_IsSynthesizedUnderTheQueryName(
        TestZoneSigningKind kind,
        string name,
        DnsResourceRecordTypeEnum type
    )
    {
        DnsMessage response = await _hosts[kind].QueryAsync(name, type);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        DnsResourceRecord answer = Assert.Single(response.Answers);
        Assert.Equal(DnsName.Parse(name), answer.Name);
        Assert.Equal(type, answer.Type);
    }

    [Theory]
    [InlineData(TestZoneSigningKind.None)]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task Query_ForAWildcardNameLackingTheType_IsNoData(TestZoneSigningKind kind)
    {
        AssertNoData(await _hosts[kind].QueryAsync("host3.example", DnsResourceRecordTypeEnum.A));
        AssertNoData(await _hosts[kind].QueryAsync("host1.example", DnsResourceRecordTypeEnum.MX));
        AssertNoData(await _hosts[kind].QueryAsync("sub.*.example", DnsResourceRecordTypeEnum.MX));
    }

    [Theory]
    [InlineData(TestZoneSigningKind.None)]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task Query_BelowADelegation_IsAReferralNotAWildcardAnswer(TestZoneSigningKind kind)
    {
        DnsMessage response = await _hosts[kind]
            .QueryAsync("host.subdel.example", DnsResourceRecordTypeEnum.A);

        Assert.False(response.IsAuthoritativeAnswer);
        Assert.Empty(response.Answers);
        Assert.Contains(
            response.AuthorityRecords,
            record => record.Type == DnsResourceRecordTypeEnum.NS
        );
    }

    [Theory]
    [InlineData(
        TestZoneSigningKind.None,
        "_telnet._tcp.host1.example",
        DnsResourceRecordTypeEnum.Srv
    )]
    [InlineData(
        TestZoneSigningKind.Nsec,
        "_telnet._tcp.host1.example",
        DnsResourceRecordTypeEnum.Srv
    )]
    [InlineData(
        TestZoneSigningKind.Nsec3,
        "_telnet._tcp.host1.example",
        DnsResourceRecordTypeEnum.Srv
    )]
    [InlineData(TestZoneSigningKind.None, "ghost.*.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec, "ghost.*.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec3, "ghost.*.example", DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.None, "x.host1.example", DnsResourceRecordTypeEnum.A)]
    [InlineData(TestZoneSigningKind.Nsec, "x.host1.example", DnsResourceRecordTypeEnum.A)]
    [InlineData(TestZoneSigningKind.Nsec3, "x.host1.example", DnsResourceRecordTypeEnum.A)]
    public async Task Query_WhoseClosestEncloserHasNoWildcard_IsNxDomain(
        TestZoneSigningKind kind,
        string name,
        DnsResourceRecordTypeEnum type
    )
    {
        AssertNxDomain(await _hosts[kind].QueryAsync(name, type));
    }

    [Theory]
    [InlineData(TestZoneSigningKind.None)]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task Query_UnderACloserWildcardWithoutTheType_IsNoDataNotAnAnswerFromAHigherWildcard(
        TestZoneSigningKind kind
    )
    {
        // *.w.example owns only A, so x.w.example TXT is NODATA even though *.example has TXT.
        AssertNoData(await _hosts[kind].QueryAsync("x.w.example", DnsResourceRecordTypeEnum.Txt));
    }

    [Theory]
    [InlineData(TestZoneSigningKind.None)]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task Any_ForANameWhoseClosestEncloserHasNoWildcard_IsNxDomain(
        TestZoneSigningKind kind
    )
    {
        AssertNxDomain(
            await _hosts[kind].QueryAsync("ghost.*.example", DnsResourceRecordTypeEnum.Any)
        );
    }

    [Theory]
    [InlineData(TestZoneSigningKind.None, DnsResourceRecordTypeEnum.A)]
    [InlineData(TestZoneSigningKind.Nsec, DnsResourceRecordTypeEnum.A)]
    [InlineData(TestZoneSigningKind.Nsec3, DnsResourceRecordTypeEnum.A)]
    [InlineData(TestZoneSigningKind.None, DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec, DnsResourceRecordTypeEnum.MX)]
    [InlineData(TestZoneSigningKind.Nsec3, DnsResourceRecordTypeEnum.MX)]
    public async Task Query_ForAnyTypeUnderAWildcardCname_AnswersTheCnameUnderTheQueryName(
        TestZoneSigningKind kind,
        DnsResourceRecordTypeEnum type
    )
    {
        DnsMessage response = await _hosts[kind].QueryAsync("x.wild.test", type);

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        DnsResourceRecord cname = Assert.Single(
            response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.Cname
        );
        Assert.Equal(DnsName.Parse("x.wild.test"), cname.Name);

        // The target is in the zone, so an A query also gets the target's address.
        bool hasTargetAddress = response.Answers.Any(record =>
            record.Type == DnsResourceRecordTypeEnum.A
            && record.Name == DnsName.Parse("host1.wild.test")
        );
        Assert.Equal(type == DnsResourceRecordTypeEnum.A, hasTargetAddress);
    }

    [Theory]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task Query_UnderASignedWildcardCname_ServesTheRrsigUnderTheQueryNameWithTheWildcardProof(
        TestZoneSigningKind kind
    )
    {
        DnsMessage response = await _hosts[kind]
            .QueryAsync("x.wild.test", DnsResourceRecordTypeEnum.MX, dnssecOk: true);

        DnsResourceRecord rrsig = Assert.Single(
            response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.RrSig
        );
        Assert.Equal(DnsName.Parse("x.wild.test"), rrsig.Name);

        // RRSIG RDATA: type covered (2), algorithm (1), labels (1). The label count excludes the
        // wildcard label, so it is 2 for *.wild.test.
        Assert.Equal(2, rrsig.Data[3]);

        List<DnsResourceRecord> proof = ProofRecords(response);
        Assert.NotEmpty(proof);
        DnsName apex = DnsName.Parse("wild.test");
        DnsName queried = DnsName.Parse("x.wild.test");
        Assert.Equal(
            DenialOfExistenceOutcomeEnum.Proven,
            kind == TestZoneSigningKind.Nsec
                ? DenialOfExistenceValidator.VerifyWildcardAnswer(queried, 2, apex, proof)
                : DenialOfExistenceValidator.VerifyWildcardAnswerNsec3(queried, 2, apex, proof)
        );
    }

    [Theory]
    [InlineData(TestZoneSigningKind.Nsec)]
    [InlineData(TestZoneSigningKind.Nsec3)]
    public async Task NoData_ForANameCoveredByAWildcardLackingTheType_CarriesTheWildcardProof(
        TestZoneSigningKind kind
    )
    {
        DnsMessage response = await _hosts[kind]
            .QueryAsync("host3.example", DnsResourceRecordTypeEnum.A, dnssecOk: true);

        AssertNoData(response);
        List<DnsResourceRecord> proof = ProofRecords(response);
        Assert.NotEmpty(proof);
        DnsName apex = DnsName.Parse(Zone);
        DnsName queried = DnsName.Parse("host3.example");
        Assert.Equal(
            DenialOfExistenceOutcomeEnum.Proven,
            kind == TestZoneSigningKind.Nsec
                ? DenialOfExistenceValidator.VerifyNoData(
                    queried,
                    DnsResourceRecordTypeEnum.A,
                    apex,
                    proof
                )
                : DenialOfExistenceValidator.VerifyNoDataNsec3(
                    queried,
                    DnsResourceRecordTypeEnum.A,
                    apex,
                    proof
                )
        );
    }

    [Fact]
    public async Task NoData_ForAWildcardNameLackingTheType_IncludesTheNsecOwnedByTheWildcard()
    {
        DnsMessage response = await _hosts[TestZoneSigningKind.Nsec]
            .QueryAsync("host3.example", DnsResourceRecordTypeEnum.A, dnssecOk: true);

        Assert.Contains(
            ProofRecords(response),
            record => record.Name == DnsName.Parse("*.example")
        );
    }
}
