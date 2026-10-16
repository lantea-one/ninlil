using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Filtering.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering.Tests;

/// <summary>
///     This class tests when <see cref="DnsFilterListExternalRefreshService" /> fetches a
///     subscribed list: the 24-hour default, the one-hour floor, the exponential backoff of a
///     failing URL and the 128 MiB download cap. It calls the internal
///     <c>RefreshDueListsAsync</c> pass directly with a <see cref="ManualTimeProvider" /> and an
///     <see cref="IHttpClientFactory" /> whose handler counts requests, so no loop runs, no socket
///     opens and no test waits for real time.
/// </summary>
public sealed class DnsFilterListRefreshScheduleTests
{
    /// <summary>
    ///     This constant is a small hosts-file list the handler serves unless a test says
    ///     otherwise.
    /// </summary>
    private const string SmallList = "0.0.0.0 fresh.example.com\n";

    /// <summary>
    ///     This class owns the in-memory storage, the manual clock, the fake HTTP handler and the
    ///     service one test drives.
    /// </summary>
    private sealed class Harness
    {
        public readonly InMemoryDnsFilterListRepository Lists = new();
        public readonly InMemoryDnsFilterListEntryRepository Entries = new();
        public readonly ManualTimeProvider Clock = new();
        public readonly FakeListHandler Handler;
        public readonly LevelLogger Log = new();
        public readonly DnsFilterListExternalRefreshService Service;

        public Harness()
        {
            Handler = new FakeListHandler(Clock);

            ServiceCollection services = new();
            services.AddSingleton<IDnsFilterListRepository>(Lists);
            services.AddSingleton<IDnsFilterListEntryRepository>(Entries);
            services.AddSingleton<IUnitOfWork>(new NoOpUnitOfWork());
            ServiceProvider provider = services.BuildServiceProvider();

            Service = new DnsFilterListExternalRefreshService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new FakeHttpClientFactory(Handler),
                new StaticOptionsMonitor<DnsFilterOptions>(new DnsFilterOptions { Enable = true }),
                Log,
                cache: null,
                timeProvider: Clock
            );
        }

        /// <summary>This method adds a subscribed list and returns it.</summary>
        public DnsFilterListEntity Subscribe(TimeSpan? interval, DateTimeOffset? lastFetched)
        {
            DnsFilterListEntity list = new()
            {
                Id = Guid.NewGuid(),
                Name = "schedule list",
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Subscribed,
                SubscriptionUrl = "http://lists.invalid/list.txt",
                RefreshInterval = interval,
                LastFetchedAtUtc = lastFetched,
            };
            Lists.Items.Add(list);
            return list;
        }

        /// <summary>This method adds one entry to a list, standing for what a refresh replaces.</summary>
        public void AddEntry(DnsFilterListEntity list, string value) =>
            Entries.Items.Add(
                new DnsFilterListEntryEntity
                {
                    Id = Guid.NewGuid(),
                    ListId = list.Id,
                    Value = value,
                }
            );

        /// <summary>This method runs one pass of the service.</summary>
        public Task PassAsync() => Service.RefreshDueListsAsync(CancellationToken.None);

        /// <summary>
        ///     This method runs a pass at the current minute, then repeats it once a minute for
        ///     <paramref name="minutes" /> more minutes, as the one-minute loop would.
        /// </summary>
        public async Task PassEveryMinuteAsync(int minutes)
        {
            await PassAsync();

            for (int minute = 0; minute < minutes; minute++)
            {
                Clock.Advance(TimeSpan.FromMinutes(1));
                await PassAsync();
            }
        }
    }

    [Fact]
    public async Task Pass_ForAListWithNoInterval_WaitsTwentyFourHours()
    {
        Harness harness = new();
        harness.Subscribe(interval: null, lastFetched: harness.Clock.GetUtcNow());
        harness.Handler.Respond = _ => FakeListHandler.Text(SmallList);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.PassAsync();
        Assert.Equal(0, harness.Handler.RequestCount);

        harness.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await harness.PassAsync();
        Assert.Equal(1, harness.Handler.RequestCount);
    }

    [Fact]
    public async Task Pass_ForAFailingUrl_AttemptsAtGrowingGapsAndWarnsOnce()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(interval: null, lastFetched: null);
        harness.AddEntry(list, "old.example.com");
        harness.Handler.Respond = _ => new HttpResponseMessage(
            System.Net.HttpStatusCode.InternalServerError
        );

        await harness.PassEveryMinuteAsync(60);

        // 1, 2, 4, 8 and 16 minutes between attempts: minutes 0, 1, 3, 7, 15 and 31.
        Assert.Equal([0, 1, 3, 7, 15, 31], harness.Handler.AttemptMinutes);
        Assert.Equal(1, harness.Log.Count(LogLevel.Warning));
        Assert.Equal("old.example.com", Assert.Single(harness.Entries.Items).Value);
        Assert.Null(list.LastFetchedAtUtc);
    }

    [Fact]
    public async Task Pass_AfterFailures_ARecoveryIsLoggedOnceAndTheNextFetchWaitsTheInterval()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(interval: null, lastFetched: null);
        harness.AddEntry(list, "old.example.com");
        harness.Handler.Respond = _ => new HttpResponseMessage(
            System.Net.HttpStatusCode.BadGateway
        );

        // Failures at minutes 0, 1 and 3; the next attempt is due at minute 7.
        await harness.PassEveryMinuteAsync(3);
        Assert.Equal(3, harness.Handler.RequestCount);

        harness.Handler.Respond = _ => FakeListHandler.Text(SmallList);
        harness.Clock.Advance(TimeSpan.FromMinutes(4));
        await harness.PassAsync();

        Assert.Equal(4, harness.Handler.RequestCount);
        Assert.Equal(1, harness.Log.Count(LogLevel.Information, "refreshed again"));
        Assert.Equal("fresh.example.com", Assert.Single(harness.Entries.Items).Value);
        Assert.Equal(harness.Clock.GetUtcNow(), list.LastFetchedAtUtc);

        harness.Clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        await harness.PassAsync();
        Assert.Equal(4, harness.Handler.RequestCount);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.PassAsync();
        Assert.Equal(5, harness.Handler.RequestCount);
    }

    [Fact]
    public async Task Pass_ForAStoredIntervalUnderAnHour_UsesOneHourAndLogsItOnce()
    {
        Harness harness = new();
        harness.Subscribe(
            interval: TimeSpan.FromSeconds(10),
            lastFetched: harness.Clock.GetUtcNow()
        );
        harness.Handler.Respond = _ => FakeListHandler.Text(SmallList);

        harness.Clock.Advance(TimeSpan.FromMinutes(59));
        await harness.PassAsync();
        Assert.Equal(0, harness.Handler.RequestCount);

        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await harness.PassAsync();
        Assert.Equal(1, harness.Handler.RequestCount);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await harness.PassAsync();
        Assert.Equal(2, harness.Handler.RequestCount);

        Assert.Equal(1, harness.Log.Count(LogLevel.Information, "below the minimum"));
    }

    [Fact]
    public async Task Pass_ForADeclaredLengthOverTheCap_FailsWithoutReadingTheBody()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(interval: null, lastFetched: null);
        harness.AddEntry(list, "old.example.com");
        GeneratedStream body = GeneratedStream.Declared(129L * 1024 * 1024);
        harness.Handler.Respond = _ => FakeListHandler.Stream(body);

        await harness.PassAsync();

        Assert.Equal(1, harness.Handler.RequestCount);
        Assert.Equal(0, body.OctetsServed);
        Assert.Equal("old.example.com", Assert.Single(harness.Entries.Items).Value);
        Assert.Null(list.LastFetchedAtUtc);
        Assert.Equal(1, harness.Log.Count(LogLevel.Warning));
    }

    [Fact]
    public async Task Pass_ForAChunkedBodyPastTheCap_StopsReadingAndKeepsTheEntries()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(interval: null, lastFetched: null);
        harness.AddEntry(list, "old.example.com");
        GeneratedStream body = GeneratedStream.Chunked(
            DnsFilterListExternalRefreshService.MaxDownloadOctets + 10L * 1024 * 1024
        );
        harness.Handler.Respond = _ => FakeListHandler.Stream(body);

        await harness.PassAsync();

        Assert.Equal("old.example.com", Assert.Single(harness.Entries.Items).Value);
        Assert.Null(list.LastFetchedAtUtc);
        // The service gives up within one buffer of the cap, long before the 10 MiB beyond it.
        Assert.True(
            body.OctetsServed <= DnsFilterListExternalRefreshService.MaxDownloadOctets + 1024 * 1024
        );
    }

    [Fact]
    public async Task Pass_ForAOneMebibyteList_ReplacesTheEntries()
    {
        Harness harness = new();
        DnsFilterListEntity list = harness.Subscribe(interval: null, lastFetched: null);
        harness.AddEntry(list, "old.example.com");
        string text = string.Concat(
            Enumerable.Range(0, 40_000).Select(index => $"0.0.0.0 host{index}.example.net\n")
        );
        Assert.True(text.Length > 1024 * 1024);
        harness.Handler.Respond = _ => FakeListHandler.Text(text);

        await harness.PassAsync();

        Assert.Equal(40_000, harness.Entries.Items.Count);
        Assert.DoesNotContain(harness.Entries.Items, entry => entry.Value == "old.example.com");
        Assert.Equal(harness.Clock.GetUtcNow(), list.LastFetchedAtUtc);
    }

    /// <summary>
    ///     This class is an <see cref="IHttpClientFactory" /> that hands out clients over one
    ///     shared <see cref="FakeListHandler" />, so no connection is ever made.
    /// </summary>
    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        /// <inheritdoc />
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    ///     This class is the fake HTTP server: it counts requests, records the minute of the
    ///     manual clock each arrived at, and answers with whatever a test set in
    ///     <see cref="Respond" />.
    /// </summary>
    private sealed class FakeListHandler(ManualTimeProvider clock) : HttpMessageHandler
    {
        private readonly DateTimeOffset _start = clock.GetUtcNow();
        private int _requestCount;

        /// <summary>This property builds the response to the next request.</summary>
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }

        /// <summary>This property is how many requests arrived.</summary>
        public int RequestCount => _requestCount;

        /// <summary>
        ///     This property is the whole minutes after the harness started at which each request
        ///     arrived.
        /// </summary>
        public List<int> AttemptMinutes { get; } = [];

        /// <summary>This method builds a 200 response holding <paramref name="text" />.</summary>
        public static HttpResponseMessage Text(string text) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StringContent(text) };

        /// <summary>This method builds a 200 response streaming <paramref name="body" />.</summary>
        public static HttpResponseMessage Stream(GeneratedStream body) =>
            new(System.Net.HttpStatusCode.OK) { Content = new StreamContent(body) };

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken stoppingToken
        )
        {
            Interlocked.Increment(ref _requestCount);
            AttemptMinutes.Add((int)(clock.GetUtcNow() - _start).TotalMinutes);
            return Task.FromResult(Respond(request));
        }
    }

    /// <summary>
    ///     This class is a response body that makes up its octets. It reports a length (so the
    ///     response carries a <c>Content-Length</c>) or none (so it is chunked), and counts the
    ///     octets handed out, so a test can see how far the service read.
    /// </summary>
    private sealed class GeneratedStream : Stream
    {
        private readonly long _length;
        private readonly bool _seekable;
        private long _position;

        private GeneratedStream(long length, bool seekable)
        {
            _length = length;
            _seekable = seekable;
        }

        /// <summary>
        ///     This method creates a body whose length is known to the HTTP layer. A test
        ///     asserts <see cref="OctetsServed" /> stays 0, because a declared length over the cap
        ///     must stop the download before any octet is read.
        /// </summary>
        public static GeneratedStream Declared(long length) => new(length, seekable: true);

        /// <summary>This method creates a body of unknown length, as a chunked response has.</summary>
        public static GeneratedStream Chunked(long length) => new(length, seekable: false);

        /// <summary>This property is how many octets readers have taken so far.</summary>
        public long OctetsServed => _position;

        public override bool CanRead => true;
        public override bool CanSeek => _seekable;
        public override bool CanWrite => false;
        public override long Length =>
            _seekable ? _length : throw new NotSupportedException("No length.");
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, _length - _position);
            // 'a' keeps the text parseable and cheap to produce; the content does not matter here.
            buffer[..count].Fill((byte)'a');
            _position += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(Read(buffer.Span));

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     This class is an <see cref="ILogger{TCategoryName}" /> that keeps the level and
    ///     formatted text of every entry, so a test can count warnings and recoveries.
    /// </summary>
    private sealed class LevelLogger : ILogger<DnsFilterListExternalRefreshService>
    {
        private readonly List<(LogLevel Level, string Message)> _entries = [];

        /// <summary>
        ///     This method counts the entries at <paramref name="level" /> whose text contains
        ///     <paramref name="text" /> (any text when it is <see langword="null" />).
        /// </summary>
        public int Count(LogLevel level, string text = null)
        {
            lock (_entries)
                return _entries.Count(entry =>
                    entry.Level == level
                    && (text is null || entry.Message.Contains(text, StringComparison.Ordinal))
                );
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (_entries)
                _entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
