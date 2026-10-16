using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.ClientSchedule;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Core.Rewrites;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This class is the query pipeline every DNS transport feeds. A transport (plain UDP/TCP,
///     DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, DNSCrypt) reads a request and calls
///     <see cref="EnqueueAsync" /> with a <see cref="DnsInboundMessage" /> that carries a delegate
///     for sending the reply. A pool of worker tasks
///     (<see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />) drains the bounded queue.
///     <para>
///         <b>Queries run concurrently.</b> A worker only starts a query and then reads the next
///         one, so a slow query (a recursion waiting on a lame server) holds a slot, not a worker.
///         At most <see cref="DnsQueryProcessingPipelineOptions.MaxConcurrentQueries" /> queries
///         are in progress. A worker takes a slot before it reads from the queue, so when every
///         slot is busy the queue fills and transports wait to enqueue. That is the intended
///         backpressure, as it is for BIND's <c>recursive-clients</c>. <see cref="StopAsync" />
///         cancels the queries in progress and waits for them to finish.
///     </para>
///     <para>
///         <b>Every query is answered or reported.</b> For each message a worker takes off the
///         queue, the pipeline calls exactly one of <see cref="DnsInboundMessage.RespondAsync" /> and
///         <see cref="DnsInboundMessage.OnDropped" />. A step that ends the query without a reply
///         passes a <see cref="DnsQueryDropReasonEnum" />, and an exception or a shutdown in the
///         middle of processing reports <see cref="DnsQueryDropReasonEnum.Failed" />. A transport
///         that holds a request open (DNS-over-HTTPS) relies on this so it never waits forever.
///     </para>
///     <para>
///         Each query goes through these steps, and any step can end it:
///         <list type="number">
///             <item>Per-IP and per-subnet rate limiting (<see cref="IDnsRateLimiter" />), before
///             decoding. Over the limit, the query is dropped, or answered with an empty
///             truncated reply to push the client to TCP.</item>
///             <item>The client access check (<see cref="IDnsFilterEngine.EvaluateClientAsync" />),
///             also before decoding. A denied client is dropped without a reply.</item>
///             <item>Decoding. A malformed message is dropped.</item>
///             <item>TSIG (<see cref="IDnsTransactionSignatureService" />, RFC 8945): a signed
///             request is verified, and a failed one is answered with the TSIG error reply and
///             skips every step up to the final one.</item>
///             <item>DNS Cookies (<see cref="DnsCookieService" />, RFC 7873), which can answer
///             REFUSED or BADCOOKIE when cookies are required.</item>
///             <item>Client pause and time-of-day schedule (<see cref="IDnsClientScheduleEngine" />).</item>
///             <item>The domain block/allow lists (<see cref="IDnsFilterEngine.EvaluateDomainAsync" />).</item>
///             <item>DNS rewrites (<see cref="IDnsRewriteEngine" />), which answer directly.</item>
///             <item>Otherwise, <see cref="IDnsQueryDispatcher" />, which offers the query to each
///             registered <see cref="IDnsQueryResolver" /> in turn (NOTIFY, dynamic update,
///             authoritative, forwarding, recursive).</item>
///             <item>Response IP and CNAME cloaking checks
///             (<see cref="IDnsFilterEngine.EvaluateResponseAsync" />) on any answer that was not
///             already a denial, including rewrite answers.</item>
///             <item>Giving a reply that has no OPT record one when the request had one (RFC 6891
///             §7), attaching a fresh server cookie, publishing
///             <see cref="DnsQueryAnsweredEvent" />, truncating an oversized datagram reply
///             (<see cref="DnsInboundMessage.IsDatagram" />) with room left for the TSIG record,
///             signing the reply (the last change to it, because any later one would break the
///             MAC), and sending it. A reply still larger
///             than <see cref="DnsInboundMessage.MaxResponseOctets" /> is dropped instead.</item>
///         </list>
///     </para>
///     <para>
///         <b>The pipeline replies on its own copy.</b> A resolver, the rewrite engine or the
///         filter engine may return a message that is shared (a cache entry). The OPT, cookie and
///         truncation steps never change that message: the first change takes one shallow copy
///         (<see cref="CopyForReply" />) and the pipeline changes the copy.
///     </para>
///     <para>
///         Options are read through <see cref="IOptionsMonitor{TOptions}" />. The UDP limits
///         (<see cref="DnsQueryProcessingPipelineOptions.MaxUdpPayloadSizeOctets" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" /> and
///         <see cref="DnsQueryProcessingPipelineOptions.MinUdpAmplificationFloorSizeOctets" />) are
///         read on every query. <see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxConcurrentQueries" /> and
///         <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> are read only in
///         <see cref="StartAsync" />, so a change to them needs this service restarted. It is
///         registered in <c>Ninlil.Web.Services.ManagedServiceRegistry</c> as
///         <c>"query-processing-pipeline"</c>, so
///         <c>POST /api/services/query-processing-pipeline/restart</c> is enough; the process
///         does not need to restart.
///     </para>
/// </summary>
/// <param name="dispatcher">The dispatcher every query that passes the checks is resolved through.</param>
/// <param name="options">The worker count, concurrency cap, queue capacity and UDP size limits.</param>
/// <param name="logger">The logger this pipeline records per-query failures to.</param>
/// <param name="filterEngine">
///     The block/allow list engine, used for the client check, the domain check and the response
///     check. Running it here covers every resolver. <see langword="null" /> skips filtering.
/// </param>
/// <param name="rateLimiter">
///     The per-IP and per-subnet rate limiter. <see langword="null" /> skips rate limiting.
/// </param>
/// <param name="cookieService">
///     The RFC 7873 DNS Cookie service. <c>AddNinlilDnsServerCore</c> always registers it;
///     <see langword="null" /> only happens when a test builds the pipeline directly, and skips
///     cookie handling.
/// </param>
/// <param name="rewriteEngine">
///     The DNS rewrite engine. A matching rewrite answers the query without the resolvers.
///     <see langword="null" /> skips rewrites.
/// </param>
/// <param name="scheduleEngine">
///     The client schedule engine, which checks a client's instant pause and then its time-of-day
///     schedule. It runs before the domain check because it needs only the client address and
///     the time. A blocked query gets the same denial response as a filter-list block
///     (<see cref="IDnsFilterEngine.BuildDeniedResponse" />), or NXDOMAIN when there is no filter
///     engine. <see langword="null" /> skips pauses and schedules.
/// </param>
/// <param name="eventPublisher">
///     The publisher for <see cref="DnsQueryAnsweredEvent" />, which the query log and
///     per-client activity are built on. It is published here, once per answered query, with
///     the response actually sent, because the response check can still replace the
///     dispatcher's answer; <see cref="Resolution.DnsQueryDispatcher" /> does not publish it.
///     Dropped queries (rate limit, client denial, malformed) publish nothing.
///     <see langword="null" /> publishes nothing.
/// </param>
/// <param name="transactionSignatureService">
///     The TSIG service. <c>AddNinlilDnsServerTsig</c> registers it; <see langword="null" />
///     (a test building the pipeline directly) ignores TSIG records, and replies go out unsigned.
/// </param>
public sealed class DnsQueryProcessingPipeline(
    IDnsQueryDispatcher dispatcher,
    IOptionsMonitor<DnsQueryProcessingPipelineOptions> options,
    ILogger<DnsQueryProcessingPipeline> logger,
    IDnsFilterEngine filterEngine = null,
    IDnsRateLimiter rateLimiter = null,
    DnsCookieService cookieService = null,
    IDnsRewriteEngine rewriteEngine = null,
    IDnsClientScheduleEngine scheduleEngine = null,
    IDnsServerEventPublisher eventPublisher = null,
    IDnsTransactionSignatureService transactionSignatureService = null
) : IHostedService, IAsyncDisposable
{
    /// <summary>
    ///     This field contains the queue transports write into and workers read from. It must be
    ///     recreated by every <see cref="StartAsync" />: <see cref="StopAsync" /> completes the
    ///     writer, and a completed channel cannot reopen, so reusing it after a restart would make
    ///     every <see cref="EnqueueAsync" /> throw <see cref="ChannelClosedException" /> and drop
    ///     all traffic. Accessed through <see cref="Volatile" /> because
    ///     <see cref="EnqueueAsync" /> can run while a restart replaces it.
    /// </summary>
    private Channel<DnsInboundMessage> _channel = CreateChannel(options);

    /// <summary>
    ///     This field contains the worker tasks started by the latest <see cref="StartAsync" />, so
    ///     <see cref="StopAsync" /> can wait for them. It is cleared on each start.
    /// </summary>
    private readonly List<Task> _workers = [];

    /// <summary>
    ///     This field counts the queries in progress for the latest <see cref="StartAsync" />, so
    ///     <see cref="StopAsync" /> can wait for them. It is replaced on each start, so a
    ///     query left over from an earlier run cannot hold up a later stop.
    /// </summary>
    private InFlightTracker _inFlight = new();

    /// <summary>
    ///     This field contains the source that tells the workers to stop. It is recreated by each
    ///     <see cref="StartAsync" /> because a <see cref="CancellationTokenSource" /> cannot be
    ///     cancelled twice.
    /// </summary>
    private CancellationTokenSource _stoppingCts;

    /// <summary>
    ///     This method builds an empty queue sized by <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" />.
    /// </summary>
    private static Channel<DnsInboundMessage> CreateChannel(
        IOptionsMonitor<DnsQueryProcessingPipelineOptions> pipelineOptions
    ) =>
        Channel.CreateBounded<DnsInboundMessage>(
            new BoundedChannelOptions(pipelineOptions.CurrentValue.ChannelCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                // When full, the transport waits for room: a delayed query may still be answered
                // in time, a dropped one never is.
                FullMode = BoundedChannelFullMode.Wait,
            }
        );

    /// <summary>
    ///     This method queues a raw query for processing. It returns once the query is queued, not
    ///     when it is answered, and waits while the queue is full.
    /// </summary>
    /// <param name="message">The query to process.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public ValueTask EnqueueAsync(
        DnsInboundMessage message,
        CancellationToken stoppingToken = default
    ) => Volatile.Read(ref _channel).Writer.WriteAsync(message, stoppingToken);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Channel<DnsInboundMessage> channel = CreateChannel(options);
        Volatile.Write(ref _channel, channel);

        _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workers.Clear();

        InFlightTracker inFlight = new();
        _inFlight = inFlight;
        SemaphoreSlim slots = new(Math.Max(1, options.CurrentValue.MaxConcurrentQueries));
        CancellationToken stoppingToken = _stoppingCts.Token;

        for (int i = 0; i < options.CurrentValue.WorkerCount; i++)
            _workers.Add(
                Task.Run(
                    () => ProcessQueueAsync(channel, slots, inFlight, stoppingToken),
                    CancellationToken.None
                )
            );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting work, tell the workers and the queries in progress to stop, and wait
        // for both until the host's shutdown timeout.
        Channel<DnsInboundMessage> channel = Volatile.Read(ref _channel);
        channel.Writer.TryComplete();

        // StopAsync can run twice: under WebApplicationFactory, the Program.cs Run() thread and
        // the factory's teardown both stop the same host, and the second call can arrive after
        // DisposeAsync disposed the token source. Treat that as already stopped.
        try
        {
            await _stoppingCts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            // The workers go first: once they have all stopped, no new query can start, so
            // the in-flight count can only fall.
            await Task.WhenAll(_workers).WaitAsync(cancellationToken);
            await _inFlight.WhenIdle.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The shutdown timeout elapsed before every worker or query finished.
        }

        // A message still queued will never be read. Tell its transport, so a request held open
        // (DNS-over-HTTPS) does not wait for its backstop.
        while (channel.Reader.TryRead(out DnsInboundMessage abandoned))
            NotifyDropped(abandoned, DnsQueryDropReasonEnum.Failed);
    }

    /// <summary>
    ///     This method is the body of one worker task: take a slot, read the next query, start it
    ///     without waiting for it, and repeat until told to stop. The slot comes first so a
    ///     waiting query stays in the queue, where <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" />
    ///     bounds it, instead of sitting in a worker. It is given the channel its
    ///     <see cref="StartAsync" /> created rather than reading <c>_channel</c>, which a restart
    ///     may already have replaced.
    /// </summary>
    private async Task ProcessQueueAsync(
        Channel<DnsInboundMessage> channel,
        SemaphoreSlim slots,
        InFlightTracker inFlight,
        CancellationToken stoppingToken
    )
    {
        try
        {
            while (true)
            {
                await slots.WaitAsync(stoppingToken);

                DnsInboundMessage queued;
                try
                {
                    queued = await channel.Reader.ReadAsync(stoppingToken);
                }
                catch
                {
                    slots.Release();
                    throw;
                }

                // Counted before the task starts, so StopAsync cannot see "idle" in between.
                inFlight.Enter();
                _ = Task.Run(
                    () => HandleMessageAsync(queued, slots, inFlight, stoppingToken),
                    CancellationToken.None
                );
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
        catch (ChannelClosedException)
        {
            // The queue was completed and is empty.
        }
    }

    /// <summary>
    ///     This method runs one query as its own task and gives its slot back when done. It never
    ///     throws, so the task it runs on cannot fault unobserved.
    /// </summary>
    private async Task HandleMessageAsync(
        DnsInboundMessage queued,
        SemaphoreSlim slots,
        InFlightTracker inFlight,
        CancellationToken stoppingToken
    )
    {
        try
        {
            // Wrap the message so the pipeline knows whether a step already replied or
            // reported a drop. The catch blocks below then report Failed only when nothing
            // else has, which keeps the "exactly one" rule of the class summary.
            bool settled = false;
            DnsInboundMessage message = queued with
            {
                RespondAsync = (responseBytes, token) =>
                {
                    settled = true;
                    return queued.RespondAsync(responseBytes, token);
                },
                OnDropped = reason =>
                {
                    settled = true;
                    NotifyDropped(queued, reason);
                },
            };

            try
            {
                await ProcessSingleMessageAsync(message, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Shutdown in the middle of a query: tell the transport.
                if (!settled)
                    NotifyDropped(queued, DnsQueryDropReasonEnum.Failed);
            }
            catch (Exception exception)
            {
                // One failing query must not affect the others.
                logger.LogError(
                    exception,
                    "Unhandled exception while processing a DNS query from {RemoteAddress}.",
                    message.RemoteAddress
                );

                if (!settled)
                    NotifyDropped(queued, DnsQueryDropReasonEnum.Failed);
            }
        }
        catch (Exception exception)
        {
            // Not expected: the steps above already catch. Observed here so a fault cannot go
            // unobserved.
            logger.LogDebug(exception, "A DNS query task failed while reporting its outcome.");
        }
        finally
        {
            slots.Release();
            inFlight.Exit();
        }
    }

    /// <summary>
    ///     This class counts the queries in progress and offers a task that completes when the
    ///     count is zero. A lock guards it because queries start on a worker and end on the thread
    ///     pool.
    /// </summary>
    private sealed class InFlightTracker
    {
        private readonly Lock _lock = new();
        private int _count;
        private TaskCompletionSource _idle;

        /// <summary>
        ///     This property returns a task that is complete while no query is in progress.
        /// </summary>
        public Task WhenIdle
        {
            get
            {
                lock (_lock)
                    return _count == 0 ? Task.CompletedTask : _idle.Task;
            }
        }

        /// <summary>
        ///     This method records the start of a query.
        /// </summary>
        public void Enter()
        {
            lock (_lock)
            {
                if (_count++ == 0)
                    _idle = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously
                    );
            }
        }

        /// <summary>
        ///     This method records the end of a query.
        /// </summary>
        public void Exit()
        {
            TaskCompletionSource idle = null;

            lock (_lock)
            {
                if (--_count == 0)
                    idle = _idle;
            }

            idle?.TrySetResult();
        }
    }

    /// <summary>
    ///     This method calls the transport's drop notifier. A notifier that throws must not
    ///     fail the query task, so the exception is logged and swallowed.
    /// </summary>
    private void NotifyDropped(DnsInboundMessage message, DnsQueryDropReasonEnum reason)
    {
        try
        {
            message.OnDropped?.Invoke(reason);
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception,
                "The drop notifier for a query from {RemoteAddress} threw.",
                message.RemoteAddress
            );
        }
    }

    /// <summary>
    ///     This method runs one query through the steps listed in the class summary. Every path
    ///     out of it replies or reports a drop through <paramref name="message" />.
    /// </summary>
    private async Task ProcessSingleMessageAsync(
        DnsInboundMessage message,
        CancellationToken stoppingToken
    )
    {
        // Rate limiting needs only the source address, so it runs before decoding. A throttled
        // query gets no reply, so the sender cannot tell throttling from packet loss.
        if (
            rateLimiter is not null
            && !await rateLimiter.TryAcquireAsync(message.RemoteAddress, stoppingToken)
        )
        {
            logger.LogDebug(
                "Rate-limiting a query from {RemoteAddress}: token budget exhausted.",
                message.RemoteAddress
            );
            message.OnDropped?.Invoke(DnsQueryDropReasonEnum.RateLimited);
            return;
        }

        // The per-subnet limit catches a burst spread over many addresses in one network, which
        // the per-IP limit misses. RespondTruncated sends an empty TC=1 reply built from the
        // message ID alone (the first two octets, RFC 1035 §4.1.1), so no decode is needed.
        if (rateLimiter is not null && message.RequestBytes.Length >= 2)
        {
            DnsSubnetRateLimitDecisionEnum subnetDecision = await rateLimiter.CheckSubnetLimitAsync(
                message.RemoteAddress,
                message.IsDatagram,
                stoppingToken
            );

            if (subnetDecision == DnsSubnetRateLimitDecisionEnum.Drop)
            {
                logger.LogDebug(
                    "Rate-limiting a query from {RemoteAddress}: its own subnet's budget is exhausted.",
                    message.RemoteAddress
                );
                message.OnDropped?.Invoke(DnsQueryDropReasonEnum.SubnetRateLimited);
                return;
            }

            if (subnetDecision == DnsSubnetRateLimitDecisionEnum.RespondTruncated)
            {
                logger.LogDebug(
                    "Rate-limiting a query from {RemoteAddress}: its own subnet's budget is "
                        + "exhausted, forcing a TCP retry instead of dropping outright.",
                    message.RemoteAddress
                );

                ushort requestId = (ushort)(
                    (message.RequestBytes.Span[0] << 8) | message.RequestBytes.Span[1]
                );
                DnsMessage truncatedResponse = new()
                {
                    Id = requestId,
                    IsResponse = true,
                    IsTruncated = true,
                };
                await message.RespondAsync(
                    DnsMessageWriter.Write(truncatedResponse),
                    stoppingToken
                );
                return;
            }
        }

        // The client check also needs only the source address. A denied client gets no reply,
        // so it learns nothing about this server.
        if (filterEngine is not null)
        {
            DnsFilterDecision clientDecision = await filterEngine.EvaluateClientAsync(
                message.RemoteAddress,
                stoppingToken,
                message.ClientId
            );

            if (clientDecision.IsDenied)
            {
                logger.LogDebug(
                    "Dropping every query from {RemoteAddress}: {Reason}.",
                    message.RemoteAddress,
                    clientDecision.Reason
                );
                message.OnDropped?.Invoke(DnsQueryDropReasonEnum.ClientDenied);
                return;
            }
        }

        DnsMessage request;
        int tsigRecordStartOffset;

        try
        {
            request = DnsMessageReader.Read(message.RequestBytes.Span, out tsigRecordStartOffset);
        }
        catch (FormatException exception)
        {
            // A malformed message has no reliable ID to answer with, so it is not answered. Debug
            // level, because a spoofed sender can produce these at line rate, and a Warning with a
            // stack trace per message would flood the log.
            logger.LogDebug(
                exception,
                "Discarding a malformed DNS message from {RemoteAddress}.",
                message.RemoteAddress
            );
            message.OnDropped?.Invoke(DnsQueryDropReasonEnum.Malformed);
            return;
        }

        DnsQueryContext context = new(
            request,
            message.Protocol,
            message.RemoteAddress,
            DateTimeOffset.UtcNow,
            message.RequestBytes.ToArray(),
            tsigRecordStartOffset,
            message.ClientId
        );

        DnsMessage response = null;
        bool alreadyDenied = false;
        bool signatureFailed = false;

        // A signed request is verified before anything else looks at it. The outcome rides on the
        // context so a resolver that requires TSIG (dynamic update) can rely on it.
        if (request.Tsig is not null && transactionSignatureService is not null)
        {
            DnsTransactionSignature signature = await transactionSignatureService.VerifyAsync(
                context,
                stoppingToken
            );
            context = context with { TransactionSignature = signature };

            if (!signature.IsVerified)
            {
                logger.LogDebug(
                    "Rejecting a query from {RemoteAddress}: its TSIG signature failed with {Error}.",
                    message.RemoteAddress,
                    signature.Error
                );

                // The service turns this placeholder into the RFC 8945 §5.3.2 reply when it signs.
                response = WithResponseCode(
                    request,
                    signature.Error == DnsResponseCodeEnum.FormatError
                        ? DnsResponseCodeEnum.FormatError
                        : DnsResponseCodeEnum.NotAuthoritative
                );
                alreadyDenied = true;
                signatureFailed = true;
            }
        }

        DnsCookieOutcome cookieOutcome = cookieService?.Evaluate(request, message.RemoteAddress);

        // RFC 7873 §5.2: when cookies are required, a query without a valid one is answered
        // here and never reaches a resolver. A failed signature already has its answer.
        if (!signatureFailed && cookieOutcome is { ShouldRefuse: true })
        {
            logger.LogDebug(
                "Refusing a query from {RemoteAddress}: no usable DNS Cookie, and cookies are required.",
                message.RemoteAddress
            );
            response = WithResponseCode(request, DnsResponseCodeEnum.Refused);
            alreadyDenied = true;
        }
        else if (!signatureFailed && cookieOutcome is { ShouldRespondBadCookie: true })
        {
            logger.LogDebug(
                "Answering BADCOOKIE to a query from {RemoteAddress}: cookies are required and none it presented is currently valid.",
                message.RemoteAddress
            );
            response = WithResponseCode(request, DnsResponseCodeEnum.BadCookie);
            response.Edns = new DnsEdnsOptions(
                0,
                (ushort)options.CurrentValue.MaxUdpPayloadSizeOctets,
                false,
                [cookieOutcome.ResponseOption]
            );
            alreadyDenied = true;
        }

        // Pause and schedule need only the client and the time, so they run before the more
        // expensive domain check. The denial looks the same as a filter-list block.
        if (!alreadyDenied && scheduleEngine is not null)
        {
            DnsClientScheduleDecision scheduleDecision = await scheduleEngine.EvaluateAsync(
                message.RemoteAddress,
                DateTimeOffset.UtcNow,
                stoppingToken
            );

            if (scheduleDecision.IsBlocked)
            {
                logger.LogDebug(
                    "Blocking a query from {RemoteAddress}: {Reason}.",
                    message.RemoteAddress,
                    scheduleDecision.Reason
                );
                response =
                    filterEngine?.BuildDeniedResponse(request)
                    ?? WithResponseCode(request, DnsResponseCodeEnum.NxDomain);
                alreadyDenied = true;
            }
        }

        // A denied domain is blocked before dispatch, even if this server is authoritative for it.
        if (!alreadyDenied && filterEngine is not null && request.Questions.Count > 0)
        {
            DnsFilterDecision domainDecision = await filterEngine.EvaluateDomainAsync(
                request.Questions[0].Name,
                message.RemoteAddress,
                stoppingToken,
                message.ClientId
            );

            if (domainDecision.IsDenied)
            {
                logger.LogDebug(
                    "Blocking a query for {Name} from {RemoteAddress}: {Reason}.",
                    request.Questions[0].Name,
                    message.RemoteAddress,
                    domainDecision.Reason
                );
                response = filterEngine.BuildDeniedResponse(request);
                alreadyDenied = true;
            }
        }

        // Rewrites never override a denial. They live outside the zone model, so they are
        // checked here rather than as a resolver. A rewrite answer does not set alreadyDenied,
        // so the response check below still applies to it.
        if (!alreadyDenied && response is null && rewriteEngine is not null)
        {
            response = await rewriteEngine.TryBuildResponseAsync(
                request,
                message.RemoteAddress,
                stoppingToken
            );

            if (response is not null)
                logger.LogDebug(
                    "Answering a query for {Name} from {RemoteAddress} via a configured DNS rewrite.",
                    request.Questions.Count > 0 ? request.Questions[0].Name : null,
                    message.RemoteAddress
                );
        }

        response ??= await dispatcher.DispatchAsync(context, stoppingToken);

        // Response IP blocking: an answer whose A/AAAA records name a blocked address is
        // denied like a domain-list match. The same check catches CNAME cloaking (a CNAME chain
        // leading to a blocked domain). Running it here covers every resolver and the cache.
        if (filterEngine is not null && !alreadyDenied)
        {
            DnsFilterDecision responseDecision = await filterEngine.EvaluateResponseAsync(
                response,
                message.RemoteAddress,
                stoppingToken,
                message.ClientId
            );

            if (responseDecision.IsDenied)
            {
                logger.LogDebug(
                    "Blocking a response for {Name} to {RemoteAddress}: {Reason}.",
                    request.Questions.Count > 0 ? request.Questions[0].Name : null,
                    message.RemoteAddress,
                    responseDecision.Reason
                );
                response = filterEngine.BuildDeniedResponse(request);
            }
        }

        // The object in "response" can be shared (a resolver may return a cache entry), so the
        // steps below change a copy, taken once and only when a change is due.
        bool replyIsOwned = false;

        // RFC 6891 §7: a responder that receives an OPT record includes one. Replies this server
        // builds itself (filter denials, rewrite answers, REFUSED) carry none, so one is added.
        if (request.Edns is not null && response.Edns is null)
        {
            response = CopyForReply(response);
            replyIsOwned = true;
            response.Edns = new DnsEdnsOptions(
                0,
                DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                request.Edns.DnssecOk,
                []
            );
        }

        // Every reply to a client that sent a cookie carries a fresh server cookie, replacing
        // any cookie option already there. A BADCOOKIE reply already has one.
        if (
            cookieOutcome?.ResponseOption is not null
            && response.ResponseCode != DnsResponseCodeEnum.BadCookie
        )
        {
            if (!replyIsOwned)
            {
                response = CopyForReply(response);
                replyIsOwned = true;
            }

            response.Edns = response.Edns is null
                ? new DnsEdnsOptions(
                    0,
                    (ushort)options.CurrentValue.MaxUdpPayloadSizeOctets,
                    false,
                    [cookieOutcome.ResponseOption]
                )
                : response.Edns with
                {
                    Options =
                    [
                        .. response.Edns.Options.Where(option =>
                            option.Code != (ushort)EdnsOptionCodeEnum.Cookie
                        ),
                        cookieOutcome.ResponseOption,
                    ],
                };
        }

        // Published once, after every check that could replace the response (see the
        // eventPublisher parameter). The answer count is taken before UDP truncation.
        eventPublisher?.Publish(
            new DnsQueryAnsweredEvent(
                DateTimeOffset.UtcNow,
                message.Protocol,
                message.RemoteAddress,
                request.Questions.Count > 0 ? request.Questions[0].Name.ToString() : null,
                request.Questions.Count > 0 ? request.Questions[0].Type : null,
                response.ResponseCode,
                response.Answers.Count
            )
        );

        byte[] responseBytes = DnsMessageWriter.Write(response);

        // The TSIG record is added last, so a datagram reply must leave room for it.
        DnsTransactionSignature replySignature = transactionSignatureService is not null
            ? context.TransactionSignature
            : null;
        int signatureLength = replySignature is null
            ? 0
            : transactionSignatureService.GetSignatureLength(replySignature);

        // RFC 1035 §4.2.1: a datagram response that is too large is truncated (all records
        // dropped, TC set) so the client retries over TCP (§4.2.2). The other transports carry
        // large responses and skip this.
        if (message.IsDatagram)
        {
            int effectiveMaxSize = Math.Min(
                request.Edns is { RequestorPayloadSize: > 0 } edns
                    ? edns.RequestorPayloadSize
                    : 512,
                options.CurrentValue.MaxUdpPayloadSizeOctets
            );

            // Capping the response-to-query size ratio limits how much traffic a spoofed query
            // can reflect at a forged source address.
            int amplificationCeiling = Math.Max(
                options.CurrentValue.MinUdpAmplificationFloorSizeOctets,
                (int)(message.RequestBytes.Length * options.CurrentValue.MaxUdpAmplificationFactor)
            );
            effectiveMaxSize = Math.Min(effectiveMaxSize, amplificationCeiling);

            // The transport's own limit (for example an encrypted envelope's padding budget)
            // applies on top of the client's.
            if (message.MaxResponseOctets is { } transportLimit)
                effectiveMaxSize = Math.Min(effectiveMaxSize, transportLimit);

            effectiveMaxSize = Math.Max(0, effectiveMaxSize - signatureLength);

            if (responseBytes.Length > effectiveMaxSize)
            {
                // A new message, not a cleared one: "response" can still be a shared object.
                DnsMessage truncatedResponse = CopyForReply(response);
                truncatedResponse.Answers.Clear();
                truncatedResponse.AuthorityRecords.Clear();
                truncatedResponse.AdditionalRecords.Clear();
                truncatedResponse.IsTruncated = true;
                response = truncatedResponse;
                responseBytes = DnsMessageWriter.Write(response);
            }
        }

        // RFC 8945 §5.3: a truncated reply is signed too. The service signs a copy, so a cached
        // message never gains a TSIG record.
        if (replySignature is not null)
        {
            response = await transactionSignatureService.SignAsync(
                response,
                replySignature,
                stoppingToken
            );
            responseBytes = DnsMessageWriter.Write(response);
        }

        // Even the truncated reply may not fit the transport (or a stream transport has a cap
        // but no truncation). Sending it anyway would fail on the wire, so report the drop.
        if (
            message.MaxResponseOctets is { } maxResponseOctets
            && responseBytes.Length > maxResponseOctets
        )
        {
            logger.LogDebug(
                "Dropping a {Size}-octet response to {RemoteAddress}: the transport carries at most {Max}.",
                responseBytes.Length,
                message.RemoteAddress,
                maxResponseOctets
            );
            message.OnDropped?.Invoke(DnsQueryDropReasonEnum.ResponseTooLarge);
            return;
        }

        await message.RespondAsync(responseBytes, stoppingToken);
    }

    /// <summary>
    ///     This method takes the shallow copy the pipeline changes in place of a reply that can be
    ///     shared. The header flags, the question and record lists (new lists holding the same
    ///     record objects, which nothing here changes), <see cref="DnsMessage.Edns" /> and
    ///     <see cref="DnsMessage.Tsig" /> are copied.
    /// </summary>
    private static DnsMessage CopyForReply(DnsMessage source) =>
        new()
        {
            Id = source.Id,
            IsResponse = source.IsResponse,
            Opcode = source.Opcode,
            IsAuthoritativeAnswer = source.IsAuthoritativeAnswer,
            IsTruncated = source.IsTruncated,
            RecursionDesired = source.RecursionDesired,
            RecursionAvailable = source.RecursionAvailable,
            AuthenticData = source.AuthenticData,
            CheckingDisabled = source.CheckingDisabled,
            ResponseCode = source.ResponseCode,
            Questions = [.. source.Questions],
            Answers = [.. source.Answers],
            AuthorityRecords = [.. source.AuthorityRecords],
            AdditionalRecords = [.. source.AdditionalRecords],
            Edns = source.Edns,
            Tsig = source.Tsig,
        };

    /// <summary>
    ///     This method builds a minimal response with the given response code: the query's ID,
    ///     opcode, RD and CD flags, and its question section. It matches the helper of the same
    ///     name in <see cref="Resolution.DnsQueryDispatcher" />.
    /// </summary>
    private static DnsMessage WithResponseCode(
        DnsMessage request,
        DnsResponseCodeEnum responseCode
    ) =>
        new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = responseCode,
            Questions = [.. request.Questions],
        };

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _stoppingCts?.Dispose();
        return ValueTask.CompletedTask;
    }
}
