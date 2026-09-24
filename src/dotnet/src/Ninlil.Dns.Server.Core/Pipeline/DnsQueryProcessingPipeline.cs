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
///     This class decouples <em>receiving</em> a query from <em>answering</em> it. Every
///     transport's own receive loop does nothing more than read raw bytes and call
///     <see cref="EnqueueAsync" />; a small pool of background workers, sized by
///     <see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />, drains the resulting queue,
///     decoding each query, handing it to <see cref="IDnsQueryDispatcher" />, encoding the
///     answer, and sending it back. Splitting the work this way means a burst of queries, or one
///     query that happens to take longer to resolve than most, never blocks a transport's socket
///     from accepting the next datagram or connection off the wire.
///     <para>
///         <b>Bound via <see cref="IOptionsMonitor{TOptions}" />, but only half its own fields
///         genuinely hot-reload.</b> <see cref="DnsQueryProcessingPipelineOptions.MaxUdpPayloadSizeOctets" />,
///         <see cref="DnsQueryProcessingPipelineOptions.MaxUdpAmplificationFactor" />, and
///         <see cref="DnsQueryProcessingPipelineOptions.MinUdpAmplificationFloorSizeOctets" /> are
///         read fresh, via <c>options.CurrentValue</c>, on every single query this pipeline
///         answers -- a configuration write reaches them with no restart of any kind, not even a
///         restart of this one service. <see cref="DnsQueryProcessingPipelineOptions.WorkerCount" />
///         and <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" /> are structurally
///         different: they size a fixed pool of worker <see cref="Task" />s and construct a
///         brand-new <see cref="Channel{T}" />, both of which only ever happen inside
///         <see cref="StartAsync" />. Re-reading <c>options.CurrentValue</c> there means a write
///         to either field is picked up the next time <see cref="StartAsync" /> actually runs --
///         genuinely restartable in isolation, since this class is registered in
///         <c>Ninlil.Web.Services.ManagedServiceRegistry</c> under the name
///         <c>"query-processing-pipeline"</c>, so a targeted
///         <c>POST /api/services/query-processing-pipeline/restart</c> (not necessarily a full
///         process restart) is what actually applies a change to either of those two fields.
///     </para>
/// </summary>
/// <param name="dispatcher">The dispatcher every decoded query is resolved through.</param>
/// <param name="options">
///     The worker count, queue capacity, and UDP safety limits to run with -- see this class's
///     own doc comment for exactly which of these fields hot-reload and which need this
///     pipeline's own service restarted.
/// </param>
/// <param name="logger">The logger this pipeline records per-query failures to.</param>
/// <param name="filterEngine">
///     The block/allow-list engine, consulted before dispatch (client-source and domain
///     checks) and after (response-IP checks) -- applies uniformly to both
///     <c>Server.Authoritative</c> and <c>Server.Recursive</c>, since this is the one place
///     every query passes through regardless of which engine would answer it.
///     <see langword="null" /> (the default, so every pre-existing caller that constructs this
///     class without the filtering project referenced keeps compiling unchanged) means
///     filtering is entirely skipped, identical to <c>Ninlil.Dns.Server.Filtering</c>'s own
///     <c>Enable = false</c> behavior.
/// </param>
/// <param name="rateLimiter">
///     The per-client-IP rate limiter, consulted before <paramref name="filterEngine" />
///     (cheaper still, since it needs only the client's own source address).
///     <see langword="null" /> (the default) means rate limiting is entirely skipped, identical
///     to <c>Ninlil.Dns.Server.RateLimiting</c>'s own <c>Enable = false</c> behavior.
/// </param>
/// <param name="cookieService">
///     The RFC 7873 DNS Cookie service. Unlike <paramref name="filterEngine" /> and
///     <paramref name="rateLimiter" />, this lives in <c>Server.Core</c> itself and is registered
///     unconditionally by its own DI extension; <see langword="null" /> here only ever happens
///     for a caller that constructs this class directly, bypassing dependency injection (every
///     pre-existing such caller/test keeps compiling and behaving unchanged).
/// </param>
/// <param name="rewriteEngine">
///     The DNS rewrite engine, consulted after every filter/cookie check has already
///     passed and before dispatch -- a matched rewrite is answered directly, bypassing the
///     authoritative/forwarder/recursive resolvers entirely.
///     <see langword="null" /> (the default, so every pre-existing caller that constructs this
///     class without the rewrites project referenced keeps compiling unchanged) means
///     rewrites are entirely skipped, identical to <c>Ninlil.Dns.Server.Rewrites</c>'s own
///     <c>Enable = false</c> behavior.
/// </param>
/// <param name="scheduleEngine">
///     The time-of-day parental-control schedule engine -- which also evaluates a client's or
///     client group's own instant pause, checked by that engine itself ahead of every schedule
///     window it evaluates, since a pause is meant as this whole system's most decisive,
///     unconditional kill switch (see <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c>'s
///     own doc comment for why the two concerns share one engine rather than each getting its
///     own pipeline hook) -- consulted right after the cookie policy check and strictly before
///     <paramref name="filterEngine" />'s own (comparatively expensive, group/list-resolving)
///     domain check -- a scheduled or paused block is a cheap, client/time-only decision that
///     never needs to touch the filtering engine's own list machinery at all, so it always
///     short-circuits ahead of it. A blocked query is answered using the exact same wire-level
///     denial <paramref name="filterEngine" /> already builds for a domain-list block
///     (<see cref="IDnsFilterEngine.BuildDeniedResponse" />) when a filter engine happens to be
///     available, so a schedule-based block, an instant pause, and a filter-list-based block are
///     all indistinguishable on the wire -- one shared response shape, not several independently
///     invented ones. When no filter engine is registered at all (filtering itself entirely
///     absent from this deployment), a scheduled or paused block still answers, falling back to a
///     plain NXDOMAIN built the same minimal way this pipeline's own cookie-policy short-circuits
///     already do. <see langword="null" /> (the default, so every pre-existing caller that
///     constructs this class without the client-schedule project referenced keeps compiling
///     unchanged) means both scheduling and instant pause are entirely skipped.
/// </param>
/// <param name="eventPublisher">
///     The publisher this pipeline reports a query's own final, truly-final answer through, via
///     <see cref="DnsQueryAnsweredEvent" /> -- the one event <c>Server.QueryLog</c>'s own
///     persistent query log (and, in turn, the per-client activity view) is built entirely on top
///     of. Published exactly once per query, here, right before the answer is actually written to
///     the wire, regardless of which of this pipeline's own many short-circuit paths (a
///     cookie-policy refusal, an instant pause or time-of-day schedule block, a filter-list domain
///     or response-IP block, or a DNS rewrite) or <paramref name="dispatcher" /> itself ultimately
///     produced it -- <see cref="Resolution.DnsQueryDispatcher" /> itself no longer publishes this
///     event at all, precisely because it cannot see a later short-circuit (the response-IP filter
///     check below runs strictly after <paramref name="dispatcher" /> returns, and can still
///     replace its answer with a denial) that would otherwise leave a stale, already-published
///     event on record for a response that was never actually sent. <see langword="null" /> (the
///     default, so every pre-existing caller that constructs this class directly, bypassing
///     dependency injection, keeps compiling and behaving unchanged) means no event is ever
///     published, identical to how every other optional dependency here is skipped when absent.
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
    IDnsServerEventPublisher eventPublisher = null
) : IHostedService, IAsyncDisposable
{
    /// <summary>
    ///     This property contains the queue of not-yet-processed queries every transport's
    ///     receive loop writes into and every worker task reads from -- recreated fresh by every
    ///     <see cref="StartAsync" /> call rather than initialized once and reused. A
    ///     <see cref="Channel{T}" /> writer marked complete (as <see cref="StopAsync" /> marks
    ///     this one) can never be reopened -- reusing the same instance across a restart would
    ///     leave every subsequent <see cref="EnqueueAsync" /> call permanently throwing
    ///     <see cref="ChannelClosedException" /> and every restarted worker exiting immediately
    ///     with nothing to read, silently dropping all DNS traffic while every transport still
    ///     reports itself running. Read/written via <see cref="Volatile" /> since
    ///     <see cref="EnqueueAsync" /> can race a concurrent restart reassigning this field, the
    ///     same reasoning <c>ReloadableTlsCertificateProvider.Current</c> already documents for
    ///     its own hot-swapped reference.
    /// </summary>
    private Channel<DnsInboundMessage> _channel = CreateChannel(options);

    /// <summary>
    ///     This property contains every worker task started by the current <see cref="StartAsync" />
    ///     call, kept around so <see cref="StopAsync" /> can wait for them to finish -- cleared at
    ///     the start of every <see cref="StartAsync" /> call so a restart never accumulates a
    ///     stale, already-completed task from a previous run alongside the current ones.
    /// </summary>
    private readonly List<Task> _workers = [];

    /// <summary>
    ///     This property contains the source used to signal every worker to stop -- recreated
    ///     fresh by every <see cref="StartAsync" /> call, since a <see cref="CancellationTokenSource" />
    ///     can only ever be cancelled once; reusing an already-cancelled one across a restart
    ///     would leave every subsequently started worker with no way to ever observe shutdown.
    /// </summary>
    private CancellationTokenSource _stoppingCts;

    /// <summary>
    ///     This method builds a fresh, empty queue, sized per <see cref="DnsQueryProcessingPipelineOptions.ChannelCapacity" />.
    /// </summary>
    private static Channel<DnsInboundMessage> CreateChannel(
        IOptionsMonitor<DnsQueryProcessingPipelineOptions> pipelineOptions
    ) =>
        Channel.CreateBounded<DnsInboundMessage>(
            new BoundedChannelOptions(pipelineOptions.CurrentValue.ChannelCapacity)
            {
                SingleReader = false,
                SingleWriter = false,
                // Waiting for room, rather than dropping the newest or oldest query outright, is the
                // safer default: a query that ends up waiting slightly longer than usual still has a
                // chance of being answered before its requestor gives up, where a dropped query has
                // none.
                FullMode = BoundedChannelFullMode.Wait,
            }
        );

    /// <summary>
    ///     This method stages a single raw query for processing. It returns as soon as the query
    ///     has been queued, not once it has been answered.
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

        for (int i = 0; i < options.CurrentValue.WorkerCount; i++)
            _workers.Add(
                Task.Run(
                    () => ProcessQueueAsync(channel, _stoppingCts.Token),
                    CancellationToken.None
                )
            );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new work, signal every worker to stop, and give them a chance to finish
        // whatever they were already doing before the host's own shutdown timeout elapses.
        Volatile.Read(ref _channel).Writer.TryComplete();

        // A duplicate stop is a real possibility, not a defensive-programming nicety: under
        // ASP.NET Core's WebApplicationFactory (a minimal-hosting Program.cs's own background
        // "Run()" thread completing its own StopAsync/DisposeAsync cycle independently races the
        // factory's own explicit teardown, which stops the very same underlying Host a second
        // time), a second StopAsync call can genuinely arrive after DisposeAsync has already
        // disposed this same instance's own CancellationTokenSource. Treat that as "already
        // stopped," not a bug to propagate.
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
            await Task.WhenAll(_workers).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The host's shutdown timeout elapsed before every worker finished; there is nothing
            // more graceful left to do than let the process exit.
        }
    }

    /// <summary>
    ///     This method is the body of a single worker task: pull queries off the queue, one at a
    ///     time, until told to stop. Bound to the specific <paramref name="channel" /> this
    ///     worker's own <see cref="StartAsync" /> call created, rather than re-reading the
    ///     (potentially since-swapped, on a fast enough restart) <c>_channel</c> field on every
    ///     iteration, so this worker's whole lifetime is unambiguously tied to the one queue it
    ///     was actually started to drain.
    /// </summary>
    private async Task ProcessQueueAsync(
        Channel<DnsInboundMessage> channel,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await foreach (DnsInboundMessage message in channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessSingleMessageAsync(message, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // One malformed or unlucky query must never take down the worker processing
                    // it, since that worker is shared by every other query still waiting in line.
                    logger.LogError(
                        exception,
                        "Unhandled exception while processing a DNS query from {RemoteAddress}.",
                        message.RemoteAddress
                    );
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
    }

    /// <summary>
    ///     This method decodes, resolves, encodes, and replies to a single query.
    /// </summary>
    private async Task ProcessSingleMessageAsync(
        DnsInboundMessage message,
        CancellationToken stoppingToken
    )
    {
        // Checked before anything else, and before the message is even decoded: a rate-limit
        // decision, like the client-source filter check just below, needs only the client's own
        // source address. A throttled client is dropped silently for the same reason a
        // filter-denied one is -- no response at all, so an attacker gains no signal about
        // whether it was throttled or simply unlucky.
        if (
            rateLimiter is not null
            && !await rateLimiter.TryAcquireAsync(message.RemoteAddress, stoppingToken)
        )
        {
            logger.LogDebug(
                "Rate-limiting a query from {RemoteAddress}: token budget exhausted.",
                message.RemoteAddress
            );
            return;
        }

        // A second, independent rate-limit layer -- bucketed by this client's own subnet rather
        // than its exact address, catching a burst spread across many source addresses within
        // the same network that the per-IP check above cannot (each individual spoofed source
        // may only ever be seen once there). Checked here, at the same not-yet-decoded stage, for
        // the same reason. A RespondTruncated verdict is the one case that still needs a real
        // reply out of this otherwise decode-free stage -- built directly from the raw message's
        // own transaction ID (the first two octets of any well-formed DNS message, RFC 1035
        // §4.1.1), without a full decode, since that ID is all a minimal, content-free truncated
        // response needs.
        if (rateLimiter is not null && message.RequestBytes.Length >= 2)
        {
            DnsSubnetRateLimitDecisionEnum subnetDecision = await rateLimiter.CheckSubnetLimitAsync(
                message.RemoteAddress,
                message.Protocol == DnsTransportProtocolEnum.Udp,
                stoppingToken
            );

            if (subnetDecision == DnsSubnetRateLimitDecisionEnum.Drop)
            {
                logger.LogDebug(
                    "Rate-limiting a query from {RemoteAddress}: its own subnet's budget is exhausted.",
                    message.RemoteAddress
                );
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

        // Checked before the message is even decoded: an access-control decision needs only the
        // client's own source address, and a denied client is dropped exactly like this
        // project's own DNSCrypt transport drops an unauthenticated packet -- no response at
        // all, so a blocked source learns nothing about whether this server exists.
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
            // A malformed message has no reliable identifier to answer with, and RFC 1035 does
            // not require -- or even meaningfully allow -- a response to one; the safest,
            // best-practice behavior is simply not to answer it at all.
            logger.LogWarning(
                exception,
                "Discarding a malformed DNS message from {RemoteAddress}.",
                message.RemoteAddress
            );
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
        DnsCookieOutcome cookieOutcome = cookieService?.Evaluate(request, message.RemoteAddress);

        // RFC 7873 §5.2: a policy of requiring a valid DNS Cookie is enforced before dispatch,
        // exactly like a filter-denied domain -- neither engine ever sees a query this server's
        // own cookie policy already rejected.
        if (cookieOutcome is { ShouldRefuse: true })
        {
            logger.LogDebug(
                "Refusing a query from {RemoteAddress}: no usable DNS Cookie, and cookies are required.",
                message.RemoteAddress
            );
            response = WithResponseCode(request, DnsResponseCodeEnum.Refused);
            alreadyDenied = true;
        }
        else if (cookieOutcome is { ShouldRespondBadCookie: true })
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

        // An instant pause, and a time-of-day parental-control schedule, are both checked next
        // (the former always ahead of the latter, inside scheduleEngine itself), strictly before
        // the domain filter check below -- neither one ever needs anything but the client's own
        // resolved identity and the current instant, never a domain lookup, so both are always
        // cheaper than resolving which filter lists apply and are given priority accordingly. A
        // pause or scheduled block answers using the exact same wire-level denial a filter-list
        // block already uses (see this constructor's own scheduleEngine parameter doc comment for
        // why), so all three look identical to a querying client.
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

        // A denied domain is blocked whether or not this server is itself authoritative for
        // it -- checked before dispatch, so neither engine ever sees the query at all.
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

        // A DNS rewrite is consulted only once every filter/cookie denial has already been
        // ruled out (a rewrite never overrides a denial) and only for a query that has not
        // already been answered -- it is the lightweight, entity-driven "domain -> target"
        // override this project deliberately keeps out of
        // Server.Authoritative's own zone model entirely, so it is checked here, before
        // dispatch, rather than as another resolver registered with the dispatcher.
        // Deliberately not treated the same as alreadyDenied: the response-IP filter check
        // below still runs against a rewrite's own answer, so an operator-configured rewrite
        // that happens to point at an address a response-IP block list already covers is still
        // caught, the same as any other answer would be.
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

        // Technitium's "response IP blocking": an otherwise-allowed answer whose own A/AAAA
        // content names a blocked address is denied too, exactly like a domain-list match --
        // checked only once a real answer exists, never for a response the domain check above
        // already replaced. The same check blocks CNAME cloaking (an answer whose CNAME chain
        // leads to a blocked domain). It runs here, on whatever the dispatcher returned, so
        // authoritative, forwarded, recursive and cached answers are all covered by one check.
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

        // Every answer this server sends, denied or not, carries a fresh, currently valid
        // server cookie back to a requestor that presented cookie information worth responding
        // to -- the BADCOOKIE short-circuit above already attached its own, so this only ever
        // applies to a response that reached here some other way.
        if (
            cookieOutcome?.ResponseOption is not null
            && response.ResponseCode != DnsResponseCodeEnum.BadCookie
        )
        {
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

        // Report this query's own truly-final answer -- the one and only publish of
        // DnsQueryAnsweredEvent for this query, using the response actually about to be written
        // to the wire below, regardless of which path above produced it. Placed here, rather than
        // inside dispatcher.DispatchAsync itself, specifically because the response-IP filter
        // check above can still replace an already-dispatched answer with a denial; publishing
        // any earlier would risk recording a response that was never actually sent (see this
        // constructor's own eventPublisher parameter doc comment for the full reasoning).
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

        // RFC 1035 §4.2.1: a UDP response too large for the channel it travels over is
        // truncated (records dropped, TC bit set) rather than silently sent oversized and risking
        // IP fragmentation -- a real requestor sees TC=1 and retries over TCP, per §4.2.2. Every
        // other transport this pipeline serves (TCP, and, in later phases, DoH/DoT/DoQ) carries
        // an arbitrarily large response natively and never needs this at all.
        if (message.Protocol == DnsTransportProtocolEnum.Udp)
        {
            int effectiveMaxSize = Math.Min(
                request.Edns is { RequestorPayloadSize: > 0 } edns
                    ? edns.RequestorPayloadSize
                    : 512,
                options.CurrentValue.MaxUdpPayloadSizeOctets
            );

            // A minimal, no-EDNS spoofed query is the classic reflection/amplification attack
            // shape -- bounding the response/query size *ratio*, not just an absolute ceiling,
            // limits how much bigger than the query a response can be sent to a claimed
            // (possibly forged) source address.
            int amplificationCeiling = Math.Max(
                options.CurrentValue.MinUdpAmplificationFloorSizeOctets,
                (int)(message.RequestBytes.Length * options.CurrentValue.MaxUdpAmplificationFactor)
            );
            effectiveMaxSize = Math.Min(effectiveMaxSize, amplificationCeiling);

            if (responseBytes.Length > effectiveMaxSize)
            {
                response.Answers.Clear();
                response.AuthorityRecords.Clear();
                response.AdditionalRecords.Clear();
                response.IsTruncated = true;
                responseBytes = DnsMessageWriter.Write(response);
            }
        }

        await message.RespondAsync(responseBytes, stoppingToken);
    }

    /// <summary>
    ///     This method builds the minimal response header this pipeline itself is responsible
    ///     for (as opposed to a resolver's own, fuller answer) -- the query's identifier and
    ///     recursion-desired flag copied across, marked as a response, carrying the given
    ///     response code. Mirrors <see cref="Resolution.DnsQueryDispatcher" />'s own identically
    ///     shaped private helper, kept separate rather than shared since this pipeline needs it
    ///     for a cookie-policy short-circuit the dispatcher itself is never involved in. The
    ///     question section is always echoed back too -- see that helper's own doc comment for
    ///     why.
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
