using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This part of <see cref="DnsFilterEngineTests" /> covers exception entries (<c>@@</c>
///     rules), which cancel only their own list's match, and CNAME-cloaking protection, which
///     checks every CNAME target in an answer against the client's domain lists.
/// </summary>
public partial class DnsFilterEngineTests
{
    private static readonly IPAddress DefaultClient = IPAddress.Parse("10.0.0.5");

    private static void AddException(Fixture fixture, Guid listId, string value) =>
        fixture.Entries.Items.Add(
            new DnsFilterListEntryEntity
            {
                Id = Guid.NewGuid(),
                ListId = listId,
                Value = value,
                IsException = true,
            }
        );

    private static DnsResourceRecord Cname(string owner, string target) =>
        new(
            DnsName.Parse(owner),
            DnsResourceRecordTypeEnum.Cname,
            DnsClassEnum.Internet,
            300,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Cname,
                new CnameRecordData(DnsName.Parse(target))
            )
        );

    private static DnsResourceRecord A(string owner, string address) =>
        new(
            DnsName.Parse(owner),
            DnsResourceRecordTypeEnum.A,
            DnsClassEnum.Internet,
            300,
            IPAddress.Parse(address).GetAddressBytes()
        );

    /// <summary>
    ///     This method builds the answer a resolver would give for <c>www.shop.example</c> when
    ///     the shop hides a tracker behind a two-step CNAME chain.
    /// </summary>
    private static DnsMessage CloakedAnswer(params DnsResourceRecord[] answers)
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("www.shop.example"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

        foreach (DnsResourceRecord answer in answers)
            response.Answers.Add(answer);

        return response;
    }

    private static DnsMessage TwoStepChainTo(string finalTarget) =>
        CloakedAnswer(
            Cname("www.shop.example", "edge.shop-cdn.example"),
            Cname("edge.shop-cdn.example", finalTarget),
            A(finalTarget, "192.0.2.80")
        );

    private static Fixture FixtureBlocking(params string[] blockedDomains)
    {
        Fixture fixture = new();
        DnsClientGroupEntity defaultGroup = fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity blockList = fixture.AddList(
            "trackers",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            blockedDomains
        );
        fixture.AssignListToGroup(defaultGroup.Id, blockList.Id);
        return fixture;
    }

    [Fact]
    public async Task EvaluateDomainAsync_ExceptionEntryCarvesANameOutOfItsOwnListsRule()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        AddException(fixture, fixture.Lists.Items[0].Id, "cdn.tracker.example");

        Assert.True(
            (
                await fixture.Engine.EvaluateDomainAsync(
                    DnsName.Parse("tracker.example"),
                    DefaultClient
                )
            ).IsDenied
        );
        Assert.True(
            (
                await fixture.Engine.EvaluateDomainAsync(
                    DnsName.Parse("pixel.tracker.example"),
                    DefaultClient
                )
            ).IsDenied
        );
        Assert.False(
            (
                await fixture.Engine.EvaluateDomainAsync(
                    DnsName.Parse("cdn.tracker.example"),
                    DefaultClient
                )
            ).IsDenied
        );
        Assert.False(
            (
                await fixture.Engine.EvaluateDomainAsync(
                    DnsName.Parse("img.cdn.tracker.example"),
                    DefaultClient
                )
            ).IsDenied
        );
    }

    [Fact]
    public async Task EvaluateDomainAsync_ExceptionInOneListDoesNotUnblockAnotherListsMatch()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        AddException(fixture, fixture.Lists.Items[0].Id, "cdn.tracker.example");

        DnsFilterListEntity strictList = fixture.AddList(
            "strict",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "cdn.tracker.example"
        );
        fixture.AssignListToGroup(fixture.Groups.Items[0].Id, strictList.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateDomainAsync(
            DnsName.Parse("cdn.tracker.example"),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateDomainAsync_ExceptionEntryAloneNeverBlocksOrAllowsAnything()
    {
        Fixture fixture = FixtureBlocking();
        AddException(fixture, fixture.Lists.Items[0].Id, "only-an-exception.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateDomainAsync(
            DnsName.Parse("only-an-exception.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Theory]
    [InlineData("b.tracker.example")]
    [InlineData("tracker.example")]
    public async Task EvaluateResponseAsync_BlockedFinalCnameTargetDeniesTheAnswer(string target)
    {
        Fixture fixture = FixtureBlocking("tracker.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo(target),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
        Assert.Contains(target, decision.Reason);
    }

    [Fact]
    public async Task EvaluateResponseAsync_BlockedTargetInTheMiddleOfTheChainDeniesTheAnswer()
    {
        Fixture fixture = FixtureBlocking("shop-cdn.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("origin.harmless.example"),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
        Assert.Contains("edge.shop-cdn.example", decision.Reason);
    }

    [Fact]
    public async Task EvaluateResponseAsync_LongChainIsCheckedToTheEnd()
    {
        Fixture fixture = FixtureBlocking("deep-tracker.example");

        DnsMessage response = CloakedAnswer(
            Cname("www.shop.example", "hop1.example"),
            Cname("hop1.example", "hop2.example"),
            Cname("hop2.example", "hop3.example"),
            Cname("hop3.example", "hop4.example"),
            Cname("hop4.example", "x.deep-tracker.example"),
            A("x.deep-tracker.example", "192.0.2.81")
        );

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            response,
            DefaultClient
        );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_ChainWithNoBlockedTargetIsAllowed()
    {
        Fixture fixture = FixtureBlocking("tracker.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("origin.harmless.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_AnswerWithoutAnyCnameIsUnaffected()
    {
        // The queried name is on the block list, but that is the domain check's job before
        // dispatch; this check only looks at CNAME targets.
        Fixture fixture = FixtureBlocking("www.shop.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            CloakedAnswer(A("www.shop.example", "192.0.2.80")),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_TargetOnAnAllowListIsNotBlockedUnderAllowWins()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        DnsFilterListEntity allowList = fixture.AddList(
            "allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "b.tracker.example"
        );
        fixture.AssignListToGroup(fixture.Groups.Items[0].Id, allowList.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_TargetOnBothListsIsBlockedUnderBlockWins()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        fixture.Options.Precedence = DnsFilterListPrecedenceEnum.BlockWins;
        DnsFilterListEntity allowList = fixture.AddList(
            "allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "b.tracker.example"
        );
        fixture.AssignListToGroup(fixture.Groups.Items[0].Id, allowList.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_TargetCoveredByItsListsOwnExceptionIsNotBlocked()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        AddException(fixture, fixture.Lists.Items[0].Id, "b.tracker.example");

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_QueriedNameOnAnAllowListSkipsTheChainCheck()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        DnsFilterListEntity allowList = fixture.AddList(
            "allow",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Allow,
            "shop.example"
        );
        fixture.AssignListToGroup(fixture.Groups.Items[0].Id, allowList.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_OnlyClientsWhoseGroupHasTheListAreBlocked()
    {
        Fixture fixture = new();
        fixture.AddGroup("default", isDefault: true);
        DnsClientGroupEntity kids = fixture.AddGroup("kids", isDefault: false);
        fixture.AddNetwork(kids.Id, "192.168.1.0/24");
        DnsFilterListEntity kidsList = fixture.AddList(
            "kids-trackers",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "tracker.example"
        );
        fixture.AssignListToGroup(kids.Id, kidsList.Id);

        DnsFilterDecision kidsDecision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            IPAddress.Parse("192.168.1.10")
        );
        DnsFilterDecision otherDecision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.True(kidsDecision.IsDenied);
        Assert.False(otherDecision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_ClientsOwnDirectListAssignmentAppliesToTheChain()
    {
        Fixture fixture = new(new StaticClientIdentityResolver("aa:bb:cc:dd:ee:ff"));
        fixture.AddGroup("default", isDefault: true);
        DnsFilterListEntity list = fixture.AddList(
            "this-client-only",
            DnsFilterListKindEnum.Domain,
            DnsFilterListActionEnum.Block,
            "tracker.example"
        );
        DnsClientEntity client = fixture.AddClient("aa:bb:cc:dd:ee:ff");
        fixture.AssignListToClient(client.Id, list.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_SettingOffLetsTheCloakedAnswerThrough()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        fixture.Options.BlockCnameCloaking = false;

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public void BlockCnameCloaking_IsOnByDefault() =>
        Assert.True(new DnsFilterOptions().BlockCnameCloaking);

    [Fact]
    public async Task EvaluateResponseAsync_FilteringOffLetsTheCloakedAnswerThrough()
    {
        Fixture fixture = FixtureBlocking("tracker.example");
        fixture.Options.Enable = false;

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("b.tracker.example"),
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_MalformedCnameDataIsIgnoredNotThrown()
    {
        Fixture fixture = FixtureBlocking("tracker.example");

        DnsMessage response = CloakedAnswer(
            new DnsResourceRecord(
                DnsName.Parse("www.shop.example"),
                DnsResourceRecordTypeEnum.Cname,
                DnsClassEnum.Internet,
                300,
                // A label length byte promising far more bytes than follow.
                [40, (byte)'x']
            ),
            A("www.shop.example", "192.0.2.80")
        );

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            response,
            DefaultClient
        );

        Assert.False(decision.IsDenied);
    }

    [Fact]
    public async Task EvaluateResponseAsync_ResponseIpListsStillApplyToAnAnswerWithACname()
    {
        Fixture fixture = FixtureBlocking("unrelated.example");
        DnsFilterListEntity ipList = fixture.AddList(
            "bad-ips",
            DnsFilterListKindEnum.ResponseIp,
            DnsFilterListActionEnum.Block,
            "192.0.2.80"
        );
        fixture.AssignListToGroup(fixture.Groups.Items[0].Id, ipList.Id);

        DnsFilterDecision decision = await fixture.Engine.EvaluateResponseAsync(
            TwoStepChainTo("origin.harmless.example"),
            DefaultClient
        );

        Assert.True(decision.IsDenied);
    }

    /// <summary>
    ///     This fake resolves every address to one fixed client identifier.
    /// </summary>
    private sealed class StaticClientIdentityResolver(string identifier)
        : Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver
    {
        public Task<Ninlil.Dns.Server.Core.ClientIdentity.ClientIdentityResolution> ResolveAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) =>
            Task.FromResult(
                new Ninlil.Dns.Server.Core.ClientIdentity.ClientIdentityResolution(
                    identifier,
                    InternetProtocolVersionEnum.IPv4
                )
            );
    }
}
