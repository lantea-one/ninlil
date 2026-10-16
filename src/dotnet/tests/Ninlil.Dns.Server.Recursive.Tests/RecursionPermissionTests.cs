using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="RecursionPermission" />, the rule that decides whether a client
///     may use recursive service, directly with <see cref="FakeDnsAccessControlEngine" />:
///     EnableRecursion allows everyone, an access rule can allow single clients while it is off,
///     and only the recursion capability counts.
/// </summary>
public sealed class RecursionPermissionTests
{
    private static readonly IPAddress GrantedAddress = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress OtherAddress = IPAddress.Parse("198.51.100.10");

    [Fact]
    public async Task IsPermittedAsync_WithEnableRecursionOn_AllowsAnyClientWithoutAnEngine()
    {
        bool permitted = await RecursionPermission.IsPermittedAsync(
            new RecursiveServerOptions { EnableRecursion = true },
            null,
            OtherAddress
        );

        Assert.True(permitted);
    }

    [Fact]
    public async Task IsPermittedAsync_WithEnableRecursionOffAndNoEngine_DeniesEveryClient()
    {
        bool permitted = await RecursionPermission.IsPermittedAsync(
            new RecursiveServerOptions { EnableRecursion = false },
            null,
            GrantedAddress
        );

        Assert.False(permitted);
    }

    [Fact]
    public async Task IsPermittedAsync_WithEnableRecursionOffAndARuleForTheClient_AllowsOnlyThatClient()
    {
        FakeDnsAccessControlEngine engine = new(DnsAccessControlCapabilityEnum.RecursionAccess);
        engine.Grant(GrantedAddress);
        RecursiveServerOptions options = new() { EnableRecursion = false };

        Assert.True(await RecursionPermission.IsPermittedAsync(options, engine, GrantedAddress));
        Assert.False(await RecursionPermission.IsPermittedAsync(options, engine, OtherAddress));
    }

    [Fact]
    public async Task IsPermittedAsync_WithAnotherCapabilityGranted_DeniesTheClient()
    {
        FakeDnsAccessControlEngine engine = new(DnsAccessControlCapabilityEnum.RateLimitBypass);
        engine.Grant(GrantedAddress);

        bool permitted = await RecursionPermission.IsPermittedAsync(
            new RecursiveServerOptions { EnableRecursion = false },
            engine,
            GrantedAddress
        );

        Assert.False(permitted);
    }
}
