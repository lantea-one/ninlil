using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Forwarding.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests that the two ways to build a <see cref="ForwardedResponseCacheKey" /> give
///     equal keys: from the outbound request and a definition list, and from the ECS option and
///     the set's pre-joined key (<see cref="ForwarderSet.UpstreamKey" />), which the resolver
///     uses on every query. A table of requests covers the flags and options the key holds. No
///     network or database is involved.
/// </summary>
public sealed class ForwardedResponseCacheKeyTests
{
    private static readonly DnsEdnsOption Subnet = new(
        (ushort)EdnsOptionCodeEnum.ClientSubnet,
        [0, 1, 24, 0, 192, 0, 2]
    );

    private static DnsMessage Request(
        bool recursionDesired = true,
        bool checkingDisabled = false,
        DnsEdnsOptions edns = null,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A,
        DnsClassEnum recordClass = DnsClassEnum.Internet
    ) =>
        new()
        {
            Id = 7,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = recursionDesired,
            CheckingDisabled = checkingDisabled,
            Questions = { new DnsQuestion(DnsName.Parse("Www.Example.com"), type, recordClass) },
            Edns = edns,
        };

    public static TheoryData<string> Cases() =>
        [
            "plain",
            "no-rd",
            "cd",
            "edns",
            "edns-do",
            "edns-with-client-ecs",
            "ecs-built",
            "ecs-built-no-client-edns",
            "type-aaaa",
            "class-chaos",
        ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Create_FromTheSetKeyAndTheEcsOption_EqualsCreateFromTheOutboundRequest(string name)
    {
        DnsEdnsOption clientEcs = new(
            (ushort)EdnsOptionCodeEnum.ClientSubnet,
            [0, 1, 16, 0, 10, 9]
        );
        (DnsMessage request, DnsEdnsOption subnet) = name switch
        {
            "no-rd" => (Request(recursionDesired: false), null),
            "cd" => (Request(checkingDisabled: true), null),
            "edns" => (Request(edns: new DnsEdnsOptions(0, 1232, false, [])), null),
            "edns-do" => (Request(edns: new DnsEdnsOptions(0, 4096, true, [])), null),
            "edns-with-client-ecs" => (
                Request(edns: new DnsEdnsOptions(0, 1232, false, [clientEcs])),
                clientEcs
            ),
            "ecs-built" => (Request(edns: new DnsEdnsOptions(0, 1232, true, [])), Subnet),
            "ecs-built-no-client-edns" => (Request(), Subnet),
            "type-aaaa" => (Request(type: DnsResourceRecordTypeEnum.Aaaa), null),
            "class-chaos" => (Request(recordClass: DnsClassEnum.Chaos), null),
            _ => (Request(), null),
        };

        // The outbound request carries the ECS option as its only option, as the resolver builds it.
        DnsMessage outbound = Request();
        outbound.Edns = new DnsEdnsOptions(0, 1232, false, subnet is null ? [] : [subnet]);

        foreach (
            IReadOnlyList<string> set in new IReadOnlyList<string>[]
            {
                ["127.0.0.1:5301"],
                ["127.0.0.1:5301", "tls://[2001:db8::1]:853", "https://dns.example/dns-query"],
                [],
            }
        )
        {
            foreach (bool validated in new[] { false, true })
            {
                ForwardedResponseCacheKey fromOutbound = ForwardedResponseCacheKey.Create(
                    request,
                    outbound,
                    set,
                    validated
                );
                ForwardedResponseCacheKey fromParts = ForwardedResponseCacheKey.Create(
                    request,
                    subnet,
                    new ForwarderSet(set, true).UpstreamKey,
                    validated
                );

                Assert.Equal(fromOutbound, fromParts);
                Assert.Equal(fromOutbound.GetHashCode(), fromParts.GetHashCode());
            }
        }
    }

    [Fact]
    public void Create_ForDifferentSets_GivesKeysThatAreNotEqual()
    {
        DnsMessage request = Request();

        ForwardedResponseCacheKey two = ForwardedResponseCacheKey.Create(
            request,
            null,
            new ForwarderSet(["a", "b"], true).UpstreamKey,
            false
        );

        Assert.NotEqual(
            two,
            ForwardedResponseCacheKey.Create(
                request,
                null,
                new ForwarderSet(["a"], true).UpstreamKey,
                false
            )
        );
        Assert.NotEqual(
            two,
            ForwardedResponseCacheKey.Create(
                request,
                null,
                new ForwarderSet(["ab"], true).UpstreamKey,
                false
            )
        );
    }
}
