using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Core.Resolution;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests that a stored record whose data cannot be written as RDATA (an A record
///     with no address) is left out of the cached zone with one Warning, instead of making every
///     query for the zone and every transfer of it throw. It runs a <see cref="TsigTransferHost" />
///     (SQLite, the real zone cache, resolver and transfer service, no sockets) and plants the bad
///     row through the repository, because the REST API refuses it.
/// </summary>
public sealed class ZoneTransferServiceUnwritableRecordTests
{
    private const string BadName = "bad." + TsigTransferHost.ZoneName;

    /// <summary>
    ///     This class is a logger provider that keeps the Warning messages written under the
    ///     categories of the zone cache and the transfer service.
    /// </summary>
    private sealed class WarningCollector : ILoggerProvider, ILogger
    {
        /// <summary>
        ///     This field contains the Warning messages seen so far.
        /// </summary>
        public readonly List<string> Warnings = [];

        public ILogger CreateLogger(string categoryName) =>
            categoryName.Contains("Authoritative", StringComparison.Ordinal)
                ? this
                : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                lock (Warnings)
                    Warnings.Add(formatter(state, exception));
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task Query_ForAnotherNameInAZoneWithAnUnwritableRecord_IsAnswered()
    {
        WarningCollector warnings = new();
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
            loggerProvider: warnings
        );
        await host.AddRecordAsync(BadName, DnsResourceRecordTypeEnum.A, """{"Address":null}""");

        AuthoritativeQueryResolver resolver = host
            .Services.GetServices<IDnsQueryResolver>()
            .OfType<AuthoritativeQueryResolver>()
            .Single();

        DnsQueryResolution good = await resolver.TryResolveAsync(
            Query($"host1.{TsigTransferHost.ZoneName}")
        );
        DnsQueryResolution bad = await resolver.TryResolveAsync(Query(BadName));

        Assert.Equal(DnsResponseCodeEnum.NoError, good.Response.ResponseCode);
        Assert.Single(good.Response.Answers);

        // The bad row is absent, not an error: the name does not exist.
        Assert.Equal(DnsResponseCodeEnum.NxDomain, bad.Response.ResponseCode);
        Assert.Empty(bad.Response.Answers);

        // One Warning for the load, none per query.
        Assert.Single(warnings.Warnings);
    }

    [Fact]
    public async Task CreateAxfrResponseAsync_ForAZoneWithAnUnwritableRecord_CompletesWithoutThatRecord()
    {
        WarningCollector warnings = new();
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
            loggerProvider: warnings
        );

        // The key is added first: adding it reloads the zone, and each load logs its own Warning.
        string key = await host.AddKeyAsync();
        await host.AddRecordAsync(BadName, DnsResourceRecordTypeEnum.A, """{"Address":null}""");

        (DnsMessage request, byte[] raw, int offset) = await host.SignedQueryAsync(
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Axfr,
            key
        );
        List<DnsMessage> messages = await host.TransferAsync(request, raw, offset);

        Assert.All(
            messages,
            message => Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode)
        );
        List<DnsResourceRecord> records = [.. messages.SelectMany(message => message.Answers)];

        // SOA, the five A records, SOA; the bad row is not among them.
        Assert.Equal(7, records.Count);
        Assert.DoesNotContain(records, record => record.Name == DnsName.Parse(BadName));
        Assert.Single(warnings.Warnings);
        Assert.Contains(BadName, warnings.Warnings[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_ForAZoneWithAnUnwritableRecord_FullAnswerLeavesItOut()
    {
        WarningCollector warnings = new();
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
            loggerProvider: warnings
        );

        // The key is added first: adding it reloads the zone, and each load logs its own Warning.
        string key = await host.AddKeyAsync();
        await host.AddRecordAsync(BadName, DnsResourceRecordTypeEnum.A, """{"Address":null}""");

        (DnsMessage request, byte[] raw, int offset) = await host.SignedIxfrQueryAsync(50, key);
        List<DnsMessage> messages = await host.TransferIxfrAsync(request, raw, offset);

        List<DnsResourceRecord> records = [.. messages.SelectMany(message => message.Answers)];
        Assert.Equal(7, records.Count);
        Assert.DoesNotContain(records, record => record.Name == DnsName.Parse(BadName));
        Assert.Single(warnings.Warnings);
    }

    [Fact]
    public async Task CreateAxfrResponseAsync_ForAZoneWhoseSoaCannotBeWritten_AnswersServerFailure()
    {
        WarningCollector warnings = new();
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
            loggerProvider: warnings
        );
        string key = await host.AddKeyAsync();
        await host.SetSoaDataAsync("""{"MasterName":null}""");

        (DnsMessage request, byte[] raw, int offset) = await host.SignedQueryAsync(
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Axfr,
            key
        );
        List<DnsMessage> messages = await host.TransferAsync(request, raw, offset);

        // The SOA is kept (the zone cannot be served without it), so the transfer is refused.
        DnsMessage message = Assert.Single(messages);
        Assert.Equal(DnsResponseCodeEnum.ServerFailure, message.ResponseCode);
        Assert.Single(warnings.Warnings);
    }

    [Fact]
    public async Task CreateIxfrResponseAsync_WithAJournalHoldingAnUnwritableRecord_FallsBackToAFullTransfer()
    {
        WarningCollector warnings = new();
        await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
            loggerProvider: warnings
        );
        string key = await host.AddKeyAsync();

        // The step would be incremental, but its added-records snapshot holds a record with no
        // address.
        await host.AddJournalEntryAsync(
            90,
            100,
            addedRecordsJson: Ninlil.Common.Serializer.JsonSerializer.Serialize(
                new List<RecordEntity>
                {
                    new()
                    {
                        Name = BadName,
                        Type = DnsResourceRecordTypeEnum.A,
                        Class = DnsClassEnum.Internet,
                        TimeToLiveSeconds = 60,
                        DataJson = """{"Address":null}""",
                    },
                }
            )
        );

        (DnsMessage request, byte[] raw, int offset) = await host.SignedIxfrQueryAsync(90, key);
        List<DnsMessage> messages = await host.TransferIxfrAsync(request, raw, offset);

        List<DnsResourceRecord> records = [.. messages.SelectMany(message => message.Answers)];
        Assert.Equal(7, records.Count);
        Assert.DoesNotContain(records, record => record.Name == DnsName.Parse(BadName));
        Assert.Single(warnings.Warnings);
    }

    /// <summary>
    ///     This method builds an Internet-class A query for a name.
    /// </summary>
    private static DnsQueryContext Query(string name) =>
        new(
            new DnsMessage
            {
                Id = 1,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse(name),
                        DnsResourceRecordTypeEnum.A,
                        DnsClassEnum.Internet
                    ),
                },
            },
            DnsTransportProtocolEnum.Udp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow
        );
}
