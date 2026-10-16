using System.Net;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Acme.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="AcmeChallengePropagationWaiter" /> against a real zone cache on
///     SQLite (<see cref="AcmeTestZoneHost" />), a fake probe, a fake name server resolver and a
///     clock the test moves by hand, so the two-minute bound and the five-second polls run without
///     waiting.
/// </summary>
public sealed class AcmeChallengePropagationWaiterTests
{
    private static readonly DnsName ChallengeName = DnsName.Parse("_acme-challenge.example.com");

    private const string Value = "challenge-value";

    /// <summary>The records every test zone holds: the challenge TXT record and two NS records.</summary>
    private static (string, DnsResourceRecordTypeEnum, IDnsRecordData)[] ZoneRecords(
        params (string, DnsResourceRecordTypeEnum, IDnsRecordData)[] extra
    ) =>
        [
            (
                "_acme-challenge.example.com",
                DnsResourceRecordTypeEnum.Txt,
                new TxtRecordData([Value])
            ),
            .. extra,
        ];

    /// <summary>
    ///     This fake probe confirms a server from its Nth call on (1-based), counts calls per
    ///     server and can be told to throw for a server.
    /// </summary>
    private sealed class FakeProbe : IAcmeChallengeProbe
    {
        /// <summary>For each server, the call number from which it serves the value; absent means never.</summary>
        public Dictionary<IPAddress, int> ServesFromCall { get; } = [];

        /// <summary>The servers whose probe throws.</summary>
        public HashSet<IPAddress> Throwing { get; } = [];

        /// <summary>How many times each server was probed.</summary>
        public Dictionary<IPAddress, int> Calls { get; } = [];

        /// <inheritdoc />
        public Task<bool> ServesValueAsync(
            IPAddress server,
            DnsName name,
            string dnsTxtValue,
            CancellationToken stoppingToken
        )
        {
            Calls[server] = Calls.GetValueOrDefault(server) + 1;

            if (Throwing.Contains(server) && Calls[server] < 3)
                throw new InvalidOperationException("probe failed");

            return Task.FromResult(
                ServesFromCall.TryGetValue(server, out int from) && Calls[server] >= from
            );
        }
    }

    /// <summary>
    ///     This fake resolver answers from a table and throws for any other host, like a
    ///     name that does not exist.
    /// </summary>
    private sealed class FakeResolver : IAcmeNameServerAddressResolver
    {
        /// <summary>The addresses per host name.</summary>
        public Dictionary<string, IPAddress[]> Hosts { get; } = [];

        /// <inheritdoc />
        public Task<IPAddress[]> ResolveAsync(DnsName host, CancellationToken stoppingToken) =>
            Hosts.TryGetValue(host.ToString().TrimEnd('.'), out IPAddress[] addresses)
                ? Task.FromResult(addresses)
                : throw new InvalidOperationException($"{host} does not resolve");
    }

    private static readonly IPAddress Notify = IPAddress.Parse("192.0.2.1");
    private static readonly IPAddress InZoneNs = IPAddress.Parse("192.0.2.2");

    /// <summary>
    ///     This method runs the wait on a task and moves the manual clock by one poll interval each
    ///     time the waiter goes to sleep, until the task ends, counting the advances.
    /// </summary>
    private static async Task<(bool Result, int Advances)> RunAsync(
        AcmeChallengePropagationWaiter waiter,
        ManualTimeProvider time
    )
    {
        Task<bool> wait = waiter.WaitAsync(ChallengeName, Value, CancellationToken.None);
        int advances = 0;

        // The waiter creates one timer per sleep, so a new timer means it is asleep again and
        // the clock can move without losing the advance.
        int seenTimers = 0;

        while (advances < 100)
        {
            while (!wait.IsCompleted && time.TimersCreated <= seenTimers)
                await Task.Delay(1);

            if (wait.IsCompleted)
                break;

            seenTimers = time.TimersCreated;
            time.Advance(AcmeChallengePropagationWaiter.PollInterval);
            advances++;
        }

        return (await wait, advances);
    }

    [Fact]
    public async Task WaitAsync_WithASlowSecondary_ReturnsTrueAfterTheThirdPollWithoutAWarning()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(
            ["192.0.2.1"],
            ZoneRecords(
                (
                    "example.com",
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse("ns2.example.com"))
                ),
                ("ns2.example.com", DnsResourceRecordTypeEnum.A, new ARecordData(InZoneNs))
            )
        );
        FakeProbe probe = new();
        probe.ServesFromCall[Notify] = 1;
        probe.ServesFromCall[InZoneNs] = 3;
        CapturingLogger<AcmeChallengePropagationWaiter> logger = new();
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            new FakeResolver(),
            logger,
            time
        );

        (bool result, int advances) = await RunAsync(waiter, time);

        Assert.True(result);
        Assert.Equal(2, advances);
        Assert.Equal(3, probe.Calls[InZoneNs]);

        // A confirmed server is not asked again.
        Assert.Equal(1, probe.Calls[Notify]);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task WaitAsync_WithAServerThatNeverServesTheValue_ReturnsFalseAfterTheTimeoutWithOneWarning()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(
            ["192.0.2.1", "192.0.2.9"],
            ZoneRecords()
        );
        IPAddress never = IPAddress.Parse("192.0.2.9");
        FakeProbe probe = new();
        probe.ServesFromCall[Notify] = 1;
        CapturingLogger<AcmeChallengePropagationWaiter> logger = new();
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            new FakeResolver(),
            logger,
            time
        );

        (bool result, int advances) = await RunAsync(waiter, time);

        Assert.False(result);
        Assert.Equal(
            (int)(
                AcmeChallengePropagationWaiter.Timeout / AcmeChallengePropagationWaiter.PollInterval
            ),
            advances
        );
        (LogLevel level, string message) = Assert.Single(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning
        );
        Assert.Contains(never.ToString(), message);
        Assert.DoesNotContain(Notify.ToString(), message);
    }

    [Fact]
    public async Task WaitAsync_WithAnUnresolvableNsHost_SkipsItWithADebugLogAndTheOthersDecide()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(
            ["192.0.2.1"],
            ZoneRecords(
                (
                    "example.com",
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse("ns.gone.test"))
                )
            )
        );
        FakeProbe probe = new();
        probe.ServesFromCall[Notify] = 1;
        CapturingLogger<AcmeChallengePropagationWaiter> logger = new();
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            new FakeResolver(),
            logger,
            time
        );

        (bool result, _) = await RunAsync(waiter, time);

        Assert.True(result);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Debug && entry.Message.Contains("ns.gone.test")
        );
        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task WaitAsync_WithAnOutOfZoneNsHost_ProbesTheAddressesTheResolverReturns()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(
            [],
            ZoneRecords(
                (
                    "example.com",
                    DnsResourceRecordTypeEnum.NS,
                    new NsRecordData(DnsName.Parse("ns.other.test"))
                )
            )
        );
        IPAddress remote = IPAddress.Parse("198.51.100.7");
        FakeProbe probe = new();
        probe.ServesFromCall[remote] = 1;
        FakeResolver resolver = new();
        resolver.Hosts["ns.other.test"] = [remote];
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            resolver,
            new CapturingLogger<AcmeChallengePropagationWaiter>(),
            time
        );

        (bool result, _) = await RunAsync(waiter, time);

        Assert.True(result);
        Assert.Equal(1, probe.Calls[remote]);
    }

    [Fact]
    public async Task WaitAsync_WithAProbeThatThrows_TreatsItAsNotYetAndKeepsWaiting()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(
            ["192.0.2.1"],
            ZoneRecords()
        );
        FakeProbe probe = new();
        probe.ServesFromCall[Notify] = 3;
        probe.Throwing.Add(Notify);
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            new FakeResolver(),
            new CapturingLogger<AcmeChallengePropagationWaiter>(),
            time
        );

        (bool result, int advances) = await RunAsync(waiter, time);

        Assert.True(result);
        Assert.Equal(2, advances);
    }

    [Fact]
    public async Task WaitAsync_WhenTheLocalCacheNeverHoldsTheValue_ReturnsFalseWithoutProbingOthers()
    {
        await using AcmeTestZoneHost host = await AcmeTestZoneHost.StartAsync(["192.0.2.1"]);
        FakeProbe probe = new();
        probe.ServesFromCall[Notify] = 1;
        CapturingLogger<AcmeChallengePropagationWaiter> logger = new();
        ManualTimeProvider time = new();
        AcmeChallengePropagationWaiter waiter = new(
            host.Cache,
            probe,
            new FakeResolver(),
            logger,
            time
        );

        (bool result, _) = await RunAsync(waiter, time);

        Assert.False(result);
        Assert.Empty(probe.Calls);
        Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }
}
