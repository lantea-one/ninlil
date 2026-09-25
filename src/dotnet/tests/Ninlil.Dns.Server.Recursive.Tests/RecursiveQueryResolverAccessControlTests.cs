using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests how access-control rules widen <see cref="RecursiveQueryResolver" />'s
///     recursion gate: a client matching an enabled
///     <see cref="DnsAccessControlCapabilityEnum.RecursionAccess" /> rule gets recursion while
///     <see cref="RecursiveServerOptions.EnableRecursion" /> is off, and other clients are still
///     declined. It uses <see cref="FakeDnsAccessControlEngine" /> and a fake engine with a canned
///     result, with validation off.
/// </summary>
public sealed class RecursiveQueryResolverAccessControlTests
{
    private static readonly IPAddress GrantedAddress = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress UngrantedAddress = IPAddress.Parse("198.51.100.10");
    private static readonly DnsName QueriedName = DnsName.Parse("example.com");

    private static RecursiveQueryResolver CreateResolver(
        RecursiveServerOptions options,
        FakeDnsAccessControlEngine accessControlEngine = null
    )
    {
        FakeIterativeResolutionEngine engine = new();
        engine.AddResult(
            QueriedName,
            DnsResourceRecordTypeEnum.A,
            new IterativeResolutionResult(RecursiveResolutionStatusEnum.Success, [], [], null, [])
        );

        return new RecursiveQueryResolver(
            engine,
            new UnreachableDnssecValidator(),
            new StaticOptionsMonitor<RecursiveServerOptions>(options),
            accessControlEngine
        );
    }

    private static DnsQueryContext CreateContext(IPAddress remoteAddress) =>
        new(
            new DnsMessage
            {
                RecursionDesired = true,
                Questions =
                [
                    new DnsQuestion(
                        QueriedName,
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                ],
            },
            DnsTransportProtocolEnum.Udp,
            remoteAddress,
            DateTimeOffset.UtcNow
        );

    [Fact]
    public async Task GlobalRecursionOffAndNoAccessControlEngineDeclinesEveryClient()
    {
        RecursiveQueryResolver resolver = CreateResolver(
            new RecursiveServerOptions { EnableRecursion = false, EnableDnssecValidation = false }
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            CreateContext(GrantedAddress)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task GlobalRecursionOffButClientMatchesAnEnabledRuleGrantsRecursionAnyway()
    {
        FakeDnsAccessControlEngine accessControlEngine = new(
            DnsAccessControlCapabilityEnum.RecursionAccess
        );
        accessControlEngine.Grant(GrantedAddress);

        RecursiveQueryResolver resolver = CreateResolver(
            new RecursiveServerOptions { EnableRecursion = false, EnableDnssecValidation = false },
            accessControlEngine
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            CreateContext(GrantedAddress)
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
    }

    [Fact]
    public async Task GlobalRecursionOffAndClientDoesNotMatchAnyRuleStillDeclines()
    {
        FakeDnsAccessControlEngine accessControlEngine = new(
            DnsAccessControlCapabilityEnum.RecursionAccess
        );
        accessControlEngine.Grant(GrantedAddress);

        RecursiveQueryResolver resolver = CreateResolver(
            new RecursiveServerOptions { EnableRecursion = false, EnableDnssecValidation = false },
            accessControlEngine
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            CreateContext(UngrantedAddress)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task GlobalRecursionOnAnswersRegardlessOfAnyAccessControlEngine()
    {
        RecursiveQueryResolver resolver = CreateResolver(
            new RecursiveServerOptions { EnableRecursion = true, EnableDnssecValidation = false }
        );

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            CreateContext(UngrantedAddress)
        );

        Assert.True(resolution.IsHandled);
    }
}
