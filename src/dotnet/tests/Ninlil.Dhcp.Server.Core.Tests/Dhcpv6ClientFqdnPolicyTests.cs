using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.DnsUpdate;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the S bit of the DHCPv6 Client FQDN reply (RFC 4704 Section 4.1):
///     S=1 only when <see cref="IDhcpDnsUpdatePolicy" /> says this server registers the scope's
///     host names, otherwise S=0, N=1 and O=1 for a client that asked for S=1. It calls
///     <see cref="Dhcpv6RequestHandler.HandleRequestAsync" /> directly on a
///     <see cref="Dhcpv6EngineTestHost" />, once with the default policy (no DNS integration) and
///     once with a counting fake policy that answers true, on both the Rapid Commit and the REQUEST
///     paths.
/// </summary>
public sealed class Dhcpv6ClientFqdnPolicyTests
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.IPv6Loopback, 546);

    /// <summary>
    ///     This class is a policy with a fixed answer that counts its calls.
    /// </summary>
    private sealed class CountingPolicy(bool answer) : IDhcpDnsUpdatePolicy
    {
        public int Calls;

        public Task<bool> RegistersHostNamesAsync(
            DhcpScopeEntity scope,
            CancellationToken stoppingToken = default
        )
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(answer);
        }
    }

    /// <summary>
    ///     This method builds a host, optionally with a counting policy in place of the default.
    /// </summary>
    private static async Task<Dhcpv6EngineTestHost> NewHostAsync(CountingPolicy policy)
    {
        Dhcpv6EngineTestHost host = new();
        await host.InitializeAsync(
            enableRapidCommit: true,
            configureServices: policy is null
                ? null
                : services => services.AddSingleton<IDhcpDnsUpdatePolicy>(policy)
        );
        return host;
    }

    /// <summary>
    ///     This method builds a message of the given type from a client with a fixed DUID.
    /// </summary>
    private static Dhcpv6Message NewMessage(
        Dhcpv6MessageTypeEnum type,
        byte[] duid,
        bool withClientFqdn
    )
    {
        Dhcpv6Message message = new() { MessageType = type, TransactionId = 0x7000 };
        message.SetClientIdentifier(duid);
        message.SetElapsedTime(TimeSpan.Zero);

        if (withClientFqdn)
            message.SetClientFqdn(
                serverShouldNotPerformAnyDnsUpdates: false,
                serverOverrodeClientPreference: false,
                serverPerformsAaaaUpdate: true,
                domainName: "laptop",
                isFullyQualified: false
            );

        return message;
    }

    /// <summary>
    ///     This method sends a Rapid Commit SOLICIT and returns the REPLY.
    /// </summary>
    private static async Task<Dhcpv6Message> RapidCommitAsync(
        Dhcpv6EngineTestHost host,
        byte[] duid,
        bool withClientFqdn
    )
    {
        Dhcpv6Message solicit = NewMessage(Dhcpv6MessageTypeEnum.Solicit, duid, withClientFqdn);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );
        solicit.SetRapidCommit();

        return await host.Handler.HandleRequestAsync(
            solicit,
            RemoteEndPoint,
            CancellationToken.None
        );
    }

    /// <summary>
    ///     This method asserts the S, N and O bits of the reply's Client FQDN option.
    /// </summary>
    private static void AssertFqdnBits(Dhcpv6Message reply, bool s, bool n, bool o)
    {
        Assert.NotNull(reply);
        Assert.True(
            reply.TryGetClientFqdn(
                out bool replyN,
                out bool replyO,
                out bool replyS,
                out string domainName,
                out _
            )
        );
        Assert.Equal(s, replyS);
        Assert.Equal(n, replyN);
        Assert.Equal(o, replyO);
        Assert.Equal("laptop", domainName);
    }

    [Fact]
    public async Task RapidCommit_WithoutADnsPolicy_ReplyHasS0N1O1()
    {
        await using Dhcpv6EngineTestHost host = await NewHostAsync(policy: null);
        await host.SeedAddressScopeAsync("fqdn-none", "2001:db8:7::10", "2001:db8:7::12");

        Dhcpv6Message reply = await RapidCommitAsync(
            host,
            [0x00, 0x01, 0x00, 0x01, 0x71, 0x71, 0x71, 0x71, 0x00, 0x01],
            withClientFqdn: true
        );

        AssertFqdnBits(reply, s: false, n: true, o: true);
    }

    [Fact]
    public async Task RapidCommit_WithAPolicyThatRegisters_ReplyHasS1N0O0()
    {
        CountingPolicy policy = new(answer: true);
        await using Dhcpv6EngineTestHost host = await NewHostAsync(policy);
        await host.SeedAddressScopeAsync("fqdn-yes", "2001:db8:8::10", "2001:db8:8::12");

        Dhcpv6Message reply = await RapidCommitAsync(
            host,
            [0x00, 0x01, 0x00, 0x01, 0x72, 0x72, 0x72, 0x72, 0x00, 0x01],
            withClientFqdn: true
        );

        AssertFqdnBits(reply, s: true, n: false, o: false);
        Assert.Equal(1, policy.Calls);
    }

    [Fact]
    public async Task Request_WithoutADnsPolicy_ReplyHasS0N1O1()
    {
        await using Dhcpv6EngineTestHost host = await NewHostAsync(policy: null);
        await host.SeedAddressScopeAsync("fqdn-request", "2001:db8:9::10", "2001:db8:9::12");
        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x73, 0x73, 0x73, 0x73, 0x00, 0x01];

        Dhcpv6Message advertise = await host.Handler.HandleRequestAsync(
            SolicitWithAddress(duid),
            RemoteEndPoint,
            CancellationToken.None
        );
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offered
        );

        Dhcpv6Message request = NewMessage(Dhcpv6MessageTypeEnum.Request, duid, true);
        request.SetServerIdentifier(serverDuid);
        request.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses
            {
                IdentityAssociationId = 1,
                Addresses =
                {
                    new Dhcpv6IdentityAssociationAddress
                    {
                        Address = offered[0].Addresses[0].Address,
                    },
                },
            }
        );

        Dhcpv6Message reply = await host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        AssertFqdnBits(reply, s: false, n: true, o: true);
    }

    [Fact]
    public async Task RapidCommit_WithoutAClientFqdnOption_HasNoFqdnReplyAndNeverAsksThePolicy()
    {
        CountingPolicy policy = new(answer: true);
        await using Dhcpv6EngineTestHost host = await NewHostAsync(policy);
        await host.SeedAddressScopeAsync("fqdn-absent", "2001:db8:a::10", "2001:db8:a::12");

        Dhcpv6Message reply = await RapidCommitAsync(
            host,
            [0x00, 0x01, 0x00, 0x01, 0x74, 0x74, 0x74, 0x74, 0x00, 0x01],
            withClientFqdn: false
        );

        Assert.NotNull(reply);
        Assert.False(reply.TryGetClientFqdn(out _, out _, out _, out _, out _));
        Assert.Equal(0, policy.Calls);
    }

    /// <summary>
    ///     This method builds a plain SOLICIT with an IA_NA for the REQUEST test.
    /// </summary>
    private static Dhcpv6Message SolicitWithAddress(byte[] duid)
    {
        Dhcpv6Message solicit = NewMessage(Dhcpv6MessageTypeEnum.Solicit, duid, false);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );
        return solicit;
    }
}
