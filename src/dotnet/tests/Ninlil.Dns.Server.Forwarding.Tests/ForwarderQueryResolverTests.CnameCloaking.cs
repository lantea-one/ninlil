using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> proves CNAME-cloaking protection
///     on a real forwarded answer: a query for an allowed name goes through the real
///     <see cref="DnsQueryProcessingPipeline" /> to the real <see cref="ForwarderQueryResolver" />,
///     out over a real UDP socket to a fake upstream, and the upstream's answer -- a CNAME to a
///     blocked tracker domain, name-compressed on the wire as a real server would send it -- is
///     replaced by the real <see cref="DnsFilterEngine" />'s block response before it reaches
///     the client, and recorded that way in the answered event the query log is built from.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    private static byte[] BuildCloakedResponse()
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("metrics.shop.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
        response.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("metrics.shop.example."),
                DnsResourceRecordTypeEnum.Cname,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Cname,
                    new CnameRecordData(DnsName.Parse("shop.collect.tracker.example."))
                )
            )
        );
        response.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("shop.collect.tracker.example."),
                DnsResourceRecordTypeEnum.A,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.A,
                    new ARecordData(IPAddress.Parse("192.0.2.80"))
                )
            )
        );

        return DnsMessageWriter.Write(response);
    }

    /// <summary>
    ///     This method seeds a default group with one local domain block list on it.
    /// </summary>
    private async Task SeedBlockListAsync(params string[] blockedDomains)
    {
        using IServiceScope scope = _provider.CreateScope();
        IServiceProvider services = scope.ServiceProvider;

        DnsClientGroupEntity group = new()
        {
            Id = Guid.NewGuid(),
            Name = "default",
            IsDefault = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        DnsFilterListEntity list = new()
        {
            Id = Guid.NewGuid(),
            Name = "trackers",
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Local,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await services.GetRequiredService<IDnsClientGroupRepository>().AddAsync(group);
        await services.GetRequiredService<IDnsFilterListRepository>().AddAsync(list);
        await services
            .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
            .AddAsync(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = group.Id,
                    ListId = list.Id,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );

        foreach (string domain in blockedDomains)
            await services
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .AddAsync(
                    new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        ListId = list.Id,
                        Value = domain,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method sends one query through a real pipeline whose only resolver is the real
    ///     forwarder, filtered by the real engine, and returns the wire answer plus every
    ///     answered event the pipeline published.
    /// </summary>
    private async Task<(
        DnsMessage Response,
        List<DnsQueryAnsweredEvent> Events
    )> QueryThroughPipelineAsync(DnsFilterOptions filterOptions, string name)
    {
        DnsFilterEngine engine = new(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<DnsFilterOptions>(filterOptions),
            NullLogger<DnsFilterEngine>.Instance
        );
        CapturingEventPublisher events = new();

        DnsQueryProcessingPipeline pipeline = new(
            new DnsQueryDispatcher([CreateResolver()], events),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance,
            filterEngine: engine,
            eventPublisher: events
        );
        await pipeline.StartAsync(CancellationToken.None);

        try
        {
            TaskCompletionSource<byte[]> received = new();

            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    DnsMessageWriter.Write(BuildQuery(77, name)),
                    DnsTransportProtocolEnum.Udp,
                    IPAddress.Parse("10.0.0.5"),
                    (bytes, _) =>
                    {
                        received.TrySetResult(bytes.ToArray());
                        return ValueTask.CompletedTask;
                    }
                )
            );

            DnsMessage response = DnsMessageReader.Read(
                await received.Task.WaitAsync(TimeSpan.FromSeconds(10))
            );

            return (response, events.AnsweredEvents);
        }
        finally
        {
            await pipeline.StopAsync(CancellationToken.None);
            await pipeline.DisposeAsync();
        }
    }

    [Fact]
    public async Task ForwardedAnswer_WithACnameToABlockedDomain_GetsTheBlockResponse()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        await SeedBlockListAsync("tracker.example");

        (DnsMessage response, List<DnsQueryAnsweredEvent> events) = await QueryThroughPipelineAsync(
            new DnsFilterOptions { Enable = true },
            "metrics.shop.example."
        );

        Assert.Equal(77, response.Id);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.Empty(response.Answers);

        // The query log records exactly what was sent: the block, not the forwarded answer.
        DnsQueryAnsweredEvent answered = Assert.Single(events);
        Assert.Equal("metrics.shop.example.", answered.QueriedName);
        Assert.Equal(DnsResponseCodeEnum.NxDomain, answered.ResponseCode);
        Assert.Equal(0, answered.AnswerCount);
    }

    [Fact]
    public async Task ForwardedAnswer_WithACnameToABlockedDomain_HonoursSinkholeMode()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        await SeedBlockListAsync("tracker.example");

        (DnsMessage response, _) = await QueryThroughPipelineAsync(
            new DnsFilterOptions
            {
                Enable = true,
                BlockResponseMode = DnsFilterBlockResponseModeEnum.SinkholeAddress,
            },
            "metrics.shop.example."
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        DnsResourceRecord sinkhole = Assert.Single(response.Answers);
        Assert.Equal(DnsName.Parse("metrics.shop.example."), sinkhole.Name);
        Assert.Equal(DnsResourceRecordTypeEnum.A, sinkhole.Type);
        Assert.Equal(IPAddress.Any.GetAddressBytes(), sinkhole.Data);
    }

    [Fact]
    public async Task ForwardedAnswer_WithACnameToABlockedDomain_PassesWhenTheSettingIsOff()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        await SeedBlockListAsync("tracker.example");

        (DnsMessage response, _) = await QueryThroughPipelineAsync(
            new DnsFilterOptions { Enable = true, BlockCnameCloaking = false },
            "metrics.shop.example."
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal(2, response.Answers.Count);
        Assert.Equal(DnsResourceRecordTypeEnum.Cname, response.Answers[0].Type);
    }

    [Fact]
    public async Task ForwardedAnswer_WithACnameToAnUnlistedDomain_IsDeliveredUnchanged()
    {
        using FakeUdpUpstream upstream = FakeUdpUpstream.Start(BuildCloakedResponse());
        await SeedForwarderAsync($"127.0.0.1:{upstream.Port}");
        await SeedBlockListAsync("some-other-tracker.example");

        (DnsMessage response, _) = await QueryThroughPipelineAsync(
            new DnsFilterOptions { Enable = true },
            "metrics.shop.example."
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Equal(2, response.Answers.Count);
    }

    /// <summary>
    ///     This fake records every <see cref="DnsQueryAnsweredEvent" /> -- the event
    ///     <c>DnsQueryLogSubscriber</c> writes each query log row from.
    /// </summary>
    private sealed class CapturingEventPublisher : IDnsServerEventPublisher
    {
        public List<DnsQueryAnsweredEvent> AnsweredEvents { get; } = [];

        public void Publish(IDnsServerEvent domainEvent)
        {
            if (domainEvent is DnsQueryAnsweredEvent answered)
                lock (AnsweredEvents)
                    AnsweredEvents.Add(answered);
        }
    }
}
