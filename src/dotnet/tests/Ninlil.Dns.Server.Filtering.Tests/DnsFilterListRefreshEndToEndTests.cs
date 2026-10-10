using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Filtering.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class tests adblock-syntax subscriptions end to end:
///     <see cref="DnsFilterListExternalRefreshService" /> fetches a list over HTTP from a
///     Kestrel server on a loopback port, stores the parsed entries in in-memory repositories,
///     and a query sent through <see cref="DnsQueryProcessingPipeline" /> and
///     <see cref="DnsFilterEngine" /> is then blocked or let through. It covers exceptions,
///     skipped rules and their log line, allow lists in adblock syntax, and wholesale
///     replacement on refresh.
/// </summary>
public sealed class DnsFilterListRefreshEndToEndTests : IAsyncLifetime
{
    /// <summary>
    ///     This constant is a small list in the shape the AdGuard DNS filter is published in,
    ///     with a couple of hosts-file lines mixed in and CRLF line endings.
    /// </summary>
    private const string AdblockList =
        "[Adblock Plus 2.0]\r\n"
        + "! Title: Ninlil end-to-end fixture\r\n"
        + "! Homepage: https://example.invalid\r\n"
        + "||ads.example.com^\r\n"
        + "@@||cdn.ads.example.com^\r\n"
        + "||important-only.example^$important\r\n"
        + "||client-only.example^$client=10.0.0.5\r\n"
        + "example.com##.banner\r\n"
        + "0.0.0.0 hosts-style.example.net\r\n"
        + "bare-style.example.org\r\n";

    private WebApplication _fakeServer;
    private string _served = AdblockList;

    /// <summary>
    ///     This field is run by the <c>/filter-then-delete</c> route before it answers, so a test
    ///     can delete the list while the refresh is fetching it.
    /// </summary>
    private Action _onFetch;

    public async Task InitializeAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _fakeServer = builder.Build();
        _fakeServer.MapGet("/filter.txt", () => Results.Text(_served, "text/plain"));
        _fakeServer.MapGet(
            "/filter-then-delete",
            () =>
            {
                _onFetch?.Invoke();
                return Results.Text(_served, "text/plain");
            }
        );
        await _fakeServer.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _fakeServer.StopAsync();
        await _fakeServer.DisposeAsync();
    }

    private string ListUrl => $"{_fakeServer.Urls.First()}/filter.txt";

    /// <summary>
    ///     This class holds the in-memory storage and services one test shares between the
    ///     refresh service, the filter engine and the pipeline.
    /// </summary>
    private sealed class Harness
    {
        public readonly InMemoryDnsFilterListRepository Lists = new();
        public readonly InMemoryDnsFilterListEntryRepository Entries = new();
        public readonly InMemoryDnsClientGroupRepository Groups = new();
        public readonly InMemoryDnsClientGroupListAssignmentRepository Assignments = new();
        public readonly DnsFilterOptions Options = new() { Enable = true };
        public readonly CapturingLogger<DnsFilterListExternalRefreshService> RefreshLog = new();
        public readonly ServiceProvider Provider;
        public readonly EntityChangeNotifier Notifier = new();
        public readonly DnsFilterConfigurationCache Cache;

        public Harness()
        {
            ServiceCollection services = new();
            services.AddSingleton<IDnsFilterListRepository>(Lists);
            services.AddSingleton<IDnsFilterListEntryRepository>(Entries);
            services.AddSingleton<IDnsClientGroupRepository>(Groups);
            services.AddSingleton<IDnsClientGroupNetworkRepository>(
                new InMemoryDnsClientGroupNetworkRepository()
            );
            services.AddSingleton<IDnsClientGroupListAssignmentRepository>(Assignments);
            services.AddSingleton<IUnitOfWork>(new NoOpUnitOfWork());
            services.AddHttpClient();
            Provider = services.BuildServiceProvider();
            Entries.Notifier = Notifier;
            Cache = new DnsFilterConfigurationCache(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                Notifier,
                NullLogger<DnsFilterConfigurationCache>.Instance
            );
        }

        public DnsFilterListEntity Subscribe(string url, DnsFilterListActionEnum action)
        {
            DnsClientGroupEntity group = new()
            {
                Id = Guid.NewGuid(),
                Name = "default",
                IsDefault = true,
            };
            Groups.Items.Add(group);

            DnsFilterListEntity list = new()
            {
                Id = Guid.NewGuid(),
                Name = "AdGuard-style subscription",
                Kind = DnsFilterListKindEnum.Domain,
                Action = action,
                Source = DnsFilterListSourceEnum.Subscribed,
                SubscriptionUrl = url,
                RefreshInterval = TimeSpan.FromDays(1),
            };
            Lists.Items.Add(list);

            Assignments.Items.Add(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = group.Id,
                    ListId = list.Id,
                }
            );

            return list;
        }

        /// <summary>
        ///     This method runs the background service until its first tick, which runs
        ///     immediately on start, has refreshed the list, then stops it.
        /// </summary>
        public async Task RefreshAsync(DnsFilterListEntity list)
        {
            DnsFilterListExternalRefreshService service = new(
                Provider.GetRequiredService<IServiceScopeFactory>(),
                Provider.GetRequiredService<IHttpClientFactory>(),
                new StaticOptionsMonitor<DnsFilterOptions>(Options),
                RefreshLog,
                Cache
            );

            await service.StartAsync(CancellationToken.None);

            DateTime deadline = DateTime.UtcNow.AddSeconds(10);

            while (list.LastFetchedAtUtc is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            await service.StopAsync(CancellationToken.None);

            Assert.NotNull(list.LastFetchedAtUtc);
        }

        /// <summary>
        ///     This method sends one query through a pipeline wired to the filter engine, with a
        ///     resolver that answers every query that reaches it, and returns the decoded
        ///     response.
        /// </summary>
        public async Task<DnsMessage> QueryAsync(string name)
        {
            DnsFilterEngine engine = new(
                new DnsFilterConfigurationCache(
                    Provider.GetRequiredService<IServiceScopeFactory>(),
                    new EntityChangeNotifier(),
                    NullLogger<DnsFilterConfigurationCache>.Instance
                ),
                new StaticOptionsMonitor<DnsFilterOptions>(Options),
                NullLogger<DnsFilterEngine>.Instance
            );

            DnsQueryProcessingPipeline pipeline = new(
                new DnsQueryDispatcher([new AnsweringResolver()], new NoOpEventPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance,
                filterEngine: engine
            );
            await pipeline.StartAsync(CancellationToken.None);

            try
            {
                TaskCompletionSource<byte[]> received = new();
                DnsMessage query = new()
                {
                    Id = 7,
                    Opcode = DnsOpcodeEnum.Query,
                    RecursionDesired = true,
                    Questions =
                    {
                        new DnsQuestion(
                            DnsName.Parse(name),
                            DnsResourceRecordTypeEnum.A,
                            DnsClassEnum.Internet
                        ),
                    },
                };

                await pipeline.EnqueueAsync(
                    new DnsInboundMessage(
                        DnsMessageWriter.Write(query),
                        DnsTransportProtocolEnum.Udp,
                        IPAddress.Parse("10.0.0.5"),
                        (bytes, _) =>
                        {
                            received.TrySetResult(bytes.ToArray());
                            return ValueTask.CompletedTask;
                        }
                    )
                );

                return DnsMessageReader.Read(
                    await received.Task.WaitAsync(TimeSpan.FromSeconds(5))
                );
            }
            finally
            {
                await pipeline.StopAsync(CancellationToken.None);
                await pipeline.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task SubscribedAdblockList_BlocksSubdomainsOfARuleAndHonoursItsException()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(ListUrl, DnsFilterListActionEnum.Block);

        await harness.RefreshAsync(list);

        // What was stored: plain domains (never the literal "||ads.example.com^"), with the
        // "@@" line flagged as an exception and every unsupported rule left out.
        Assert.Equal(
            [
                ("ads.example.com", false),
                ("cdn.ads.example.com", true),
                ("hosts-style.example.net", false),
                ("bare-style.example.org", false),
            ],
            harness.Entries.Items.Select(entry => (entry.Value, entry.IsException))
        );

        // A subdomain of a "||domain^" rule is blocked, and so is the domain itself.
        Assert.Equal(
            DnsResponseCodeEnum.NxDomain,
            (await harness.QueryAsync("tracker.ads.example.com.")).ResponseCode
        );
        Assert.Equal(
            DnsResponseCodeEnum.NxDomain,
            (await harness.QueryAsync("ads.example.com.")).ResponseCode
        );

        // The "@@" exception, and names below it, are let through to the resolver.
        DnsMessage exception = await harness.QueryAsync("cdn.ads.example.com.");
        Assert.Equal(DnsResponseCodeEnum.NoError, exception.ResponseCode);
        Assert.Single(exception.Answers);
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            (await harness.QueryAsync("img.cdn.ads.example.com.")).ResponseCode
        );

        // Hosts-file and bare-domain lines in the same list still block.
        Assert.Equal(
            DnsResponseCodeEnum.NxDomain,
            (await harness.QueryAsync("hosts-style.example.net.")).ResponseCode
        );
        Assert.Equal(
            DnsResponseCodeEnum.NxDomain,
            (await harness.QueryAsync("bare-style.example.org.")).ResponseCode
        );

        // Rules with modifiers Ninlil cannot honour were skipped, not applied more broadly.
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            (await harness.QueryAsync("important-only.example.")).ResponseCode
        );
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            (await harness.QueryAsync("client-only.example.")).ResponseCode
        );

        // ...and the refresh log line says how many were skipped.
        string logLine = Assert.Single(
            harness.RefreshLog.Messages,
            message => message.StartsWith("Refreshed block/allow list", StringComparison.Ordinal)
        );
        Assert.Contains("4 entries", logLine);
        Assert.Contains("1 of them exceptions", logLine);
        Assert.Contains("3 unsupported rules skipped", logLine);
    }

    [Fact]
    public async Task SubscribedAdblockAllowList_TreatsItsExceptionLinesAsAllowEntries()
    {
        _served = "! An allowlist in adblock syntax\n@@||partner.example^\n";

        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(ListUrl, DnsFilterListActionEnum.Allow);

        await harness.RefreshAsync(list);

        DnsFilterListEntryEntity entry = Assert.Single(harness.Entries.Items);
        Assert.Equal("partner.example", entry.Value);
        Assert.False(entry.IsException);
    }

    [Fact]
    public async Task Refresh_ReplacesThePreviousEntriesWholesale()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(ListUrl, DnsFilterListActionEnum.Block);
        await harness.RefreshAsync(list);

        _served = "||only-this-now.example^\n";
        list.LastFetchedAtUtc = null;
        await harness.RefreshAsync(list);

        DnsFilterListEntryEntity entry = Assert.Single(harness.Entries.Items);
        Assert.Equal("only-this-now.example", entry.Value);
    }

    /// <summary>
    ///     This test checks that a refresh reloads only the refreshed list's index: the entry
    ///     signal the replace raises is silenced and the list is expired by id, so a second list
    ///     of the same group stays loaded.
    /// </summary>
    [Fact]
    public async Task Refresh_WithTheCache_ReloadsOnlyTheRefreshedList()
    {
        Harness harness = new();
        DnsFilterListEntity subscribed = harness.Subscribe(ListUrl, DnsFilterListActionEnum.Block);
        DnsFilterListEntity other = new()
        {
            Id = Guid.NewGuid(),
            Name = "static",
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Local,
        };
        harness.Lists.Items.Add(other);
        harness.Assignments.Items.Add(
            new DnsClientGroupListAssignmentEntity
            {
                Id = Guid.NewGuid(),
                ClientGroupId = harness.Groups.Items[0].Id,
                ListId = other.Id,
            }
        );
        harness.Entries.Items.Add(
            new DnsFilterListEntryEntity
            {
                Id = Guid.NewGuid(),
                ListId = other.Id,
                Value = "static-block.example",
            }
        );
        DnsFilterEngine engine = new(
            harness.Cache,
            new StaticOptionsMonitor<DnsFilterOptions>(harness.Options),
            NullLogger<DnsFilterEngine>.Instance
        );
        IPAddress client = IPAddress.Parse("10.0.0.5");

        Assert.False(
            (await engine.EvaluateDomainAsync(DnsName.Parse("ok.example.org"), client)).IsDenied
        );
        Assert.Equal(2, harness.Entries.LoadedListIds.Count);

        await harness.RefreshAsync(subscribed);

        // The new entries apply to the next query, and only that list was read again.
        Assert.True(
            (await engine.EvaluateDomainAsync(DnsName.Parse("ads.example.com"), client)).IsDenied
        );
        Assert.True(
            (
                await engine.EvaluateDomainAsync(DnsName.Parse("static-block.example"), client)
            ).IsDenied
        );
        Assert.Equal(3, harness.Entries.LoadedListIds.Count);
        Assert.Equal(subscribed.Id, harness.Entries.LoadedListIds[2]);
    }

    /// <summary>
    ///     This test deletes the list while the refresh fetches it. The refresh must log that and
    ///     finish without an error and without recording a fetch or reporting a refresh.
    /// </summary>
    [Fact]
    public async Task Refresh_WhenTheListIsDeletedDuringTheFetch_LogsItAndReportsNoRefresh()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(
            $"{_fakeServer.Urls.First()}/filter-then-delete",
            DnsFilterListActionEnum.Block
        );
        _onFetch = () => harness.Lists.Items.Remove(list);

        DnsFilterListExternalRefreshService service = new(
            harness.Provider.GetRequiredService<IServiceScopeFactory>(),
            harness.Provider.GetRequiredService<IHttpClientFactory>(),
            new StaticOptionsMonitor<DnsFilterOptions>(harness.Options),
            harness.RefreshLog,
            harness.Cache
        );
        await service.StartAsync(CancellationToken.None);

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (
            !harness.RefreshLog.Messages.Any(message => message.Contains("was deleted"))
            && DateTime.UtcNow < deadline
        )
            await Task.Delay(20);

        await service.StopAsync(CancellationToken.None);

        Assert.Contains(harness.RefreshLog.Messages, message => message.Contains("was deleted"));
        Assert.DoesNotContain(
            harness.RefreshLog.Messages,
            message => message.StartsWith("Refreshed block/allow list", StringComparison.Ordinal)
        );
        Assert.Null(list.LastFetchedAtUtc);
    }

    private sealed class AnsweringResolver : IDnsQueryResolver
    {
        public ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) =>
            ValueTask.FromResult(
                DnsQueryResolution.Handled(
                    new DnsMessage
                    {
                        Id = context.Request.Id,
                        IsResponse = true,
                        Opcode = context.Request.Opcode,
                        RecursionDesired = context.Request.RecursionDesired,
                        ResponseCode = DnsResponseCodeEnum.NoError,
                        Questions = [.. context.Request.Questions],
                        Answers =
                        {
                            new DnsResourceRecord(
                                context.Request.Questions[0].Name,
                                DnsResourceRecordTypeEnum.A,
                                DnsClassEnum.Internet,
                                300,
                                [192, 0, 2, 1]
                            ),
                        },
                    }
                )
            );
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
