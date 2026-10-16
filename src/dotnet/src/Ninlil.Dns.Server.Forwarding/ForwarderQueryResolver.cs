using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.AccessControl;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Validation;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class is the forwarding step of query resolution. It is an
///     <see cref="IDnsQueryResolver" /> registered between the authoritative and recursive
///     resolvers (see
///     <see cref="Extension.ServiceCollectionForwardingExtension.AddNinlilDnsServerForwarding" />),
///     so <see cref="DnsQueryDispatcher" /> offers it every name this server does not host (see
///     the dispatcher and <c>DnsQueryProcessingPipeline</c> for the whole query path). The
///     forwarder set is the closest enabled forwarder zone's addresses, or else the global
///     forwarders. The forwarders are tried in order, each with
///     <see cref="ForwardingOptions.PerForwarderTimeout" />.
///     <para>
///         <b>Who is forwarded for.</b> Forwarding is recursive service, so it needs a QUERY with
///         RD set (RFC 1034 §4.3.1: without RD the server answers from local data only) from a
///         client allowed recursion by <see cref="RecursionPermission" />, the rule
///         <see cref="RecursiveQueryResolver" /> uses: <see cref="RecursiveServerOptions.EnableRecursion" />
///         on, or an access rule granting the client recursion access. Any other query is
///         <see cref="DnsQueryResolution.NotHandled" />.
///     </para>
///     <para>
///         <b>Upstream failures.</b> An upstream that does not answer, or answers SERVFAIL,
///         REFUSED, NOTIMP, FORMERR or any other code except NOERROR and NXDOMAIN, has failed: it
///         counts against its health and the next forwarder is tried. NXDOMAIN and NOERROR are
///         answers and end the search. When every forwarder has failed, a stale cached answer is
///         served if the RFC 8767 setting allows it; otherwise the result is
///         <see cref="DnsQueryResolution.NotHandled" />, so the dispatcher falls through to
///         <see cref="RecursiveQueryResolver" />, which is permitted for this client by the same
///         rule.
///     </para>
///     <para>
///         <b>The outbound query.</b> Every hop gets a query built for it (see
///         <see cref="BuildOutboundRequest" />), never the client's message. The client's EDNS
///         options (cookie, padding, NSID and so on) and its TSIG are never relayed. The upstream
///         gets an OPT record only when the client sent EDNS, validation is on or an ECS option
///         was built; it keeps the client's payload size and DO bit, carries the built ECS option
///         as its only option and passes the client's CD bit through.
///     </para>
///     <para>
///         <b>DNSSEC.</b> When <see cref="RecursiveServerOptions.EnableDnssecValidation" /> is
///         on and the forwarder set allows it, the query sent upstream has EDNS with DO and CD set, whatever the client sent
///         (RFC 6840 §5.9: a validating resolver sets CD upstream, so the upstream returns the
///         data and its signatures even when its own validation fails).
///         Each answer is then checked locally with the same <see cref="IDnssecValidator" /> as
///         an iterative answer: the CNAME chain is rebuilt from the answer section, a negative
///         answer or a wildcard expansion carries the NSEC or NSEC3 proof the validator needs,
///         and the DS and DNSKEY records of the chain of trust are asked of the same
///         forwarders (see <see cref="ForwarderDnssecRecordFetcher" />), so the iterative engine
///         is not needed and a network that only reaches its forwarders still validates. A
///         <see cref="DnssecValidationStateEnum.Bogus" /> answer counts as a failed forwarder, so
///         the next one is tried, unless the client set CD (RFC 4035 §3.2.2): it then gets the
///         data without AD, and the answer is not cached. Every other result is accepted.
///         A conditional-forwarder zone validates only when its
///         <see cref="ZoneEntity.ValidateDnssec" /> switch is on (it is off by default, because a
///         private zone has no chain of trust from the public root); the global forwarders follow
///         the global setting. A query for a name on the Insecure domain list
///         (<see cref="InsecureDomainPolicy" />) is accepted as Insecure whatever the switch says.
///     </para>
///     <para>
///         <b>The reply.</b> The reply is built per client with
///         <see cref="ForwardedResponseShaper" />, never passed through. AD is set only for
///         an answer this server validated as Secure, to a client that set DO or AD; the
///         upstream's AD bit is ignored, also when validation is off. A client without DO gets
///         no RRSIG, NSEC or NSEC3 records, a client without EDNS gets no OPT record, and no
///         upstream EDNS option reaches any client.
///     </para>
///     <para>
///         <b>Caching.</b> Answers are cached inside this resolver rather than around the
///         dispatcher, because only this resolver knows which forwarder set and ECS subnet a query
///         uses, and both are part of the cache key (see <see cref="ForwardedResponseCacheKey" />).
///         The lookup happens after the forwarder set is chosen and before anything is sent; an
///         accepted, cacheable answer is stored (as received, with whether it was Secure)
///         before the reply is shaped. The cache is flushed when the Insecure domain list changes,
///         because it remembers whether an answer was Secure. The pipeline filters
///         whatever the dispatcher returns, so a cache hit is filtered per client like a fresh
///         answer. When every forwarder fails, an expired answer is served stale if the
///         server-wide RFC 8767 serve-stale setting allows it, before falling through to
///         recursion.
///     </para>
/// </summary>
/// <param name="forwarderSets">
///     The in-memory copy of the forwarder zones and global forwarders that the forwarder set of
///     each query is chosen from.
/// </param>
/// <param name="clients">Every registered forwarder client, one per protocol.</param>
/// <param name="definitionParser">Parses a forwarder's definition string.</param>
/// <param name="healthTracker">Tracks per-forwarder health for the round-robin strategy.</param>
/// <param name="dnssecValidator">Validates a forwarded answer's DNSSEC status locally.</param>
/// <param name="forwardingOptions">
///     The <see cref="ForwardingOptions.Enable" /> switch, failover strategy and timeout, read
///     live through <see cref="IOptionsMonitor{TOptions}" /> rather than
///     <see cref="IOptions{TOptions}" />, so a <c>ForwardingSettingsController.Patch</c> applies
///     to the next query without a restart.
/// </param>
/// <param name="recursiveOptions">
///     Read live for <see cref="RecursiveServerOptions.EnableDnssecValidation" /> and to build
///     the <see cref="QueryBudget" /> that bounds a validation's DS and DNSKEY lookups, so a
///     <c>RecursiveSettingsController.Patch</c> applies without a restart.
/// </param>
/// <param name="logger">The logger this resolver records a forwarder failure or Bogus result to.</param>
/// <param name="clientSubnetService">
///     The RFC 7871 EDNS Client Subnet service that builds the ECS option attached to each
///     forwarded query. <see langword="null" /> (the default) attaches none, the same as
///     <see cref="EdnsClientSubnetOptions.Enable" /> being off.
/// </param>
/// <param name="responseCache">
///     The forwarded-response cache. <see langword="null" /> (the default) sends every query
///     upstream.
/// </param>
/// <param name="insecureDomains">
///     The Insecure domain list whose changes flush <paramref name="responseCache" />.
///     <see langword="null" /> (the default) never flushes.
/// </param>
/// <param name="accessControlEngine">
///     The access-control engine that can grant recursion access to a client while
///     <see cref="RecursiveServerOptions.EnableRecursion" /> is off (see
///     <see cref="RecursionPermission" />). <see langword="null" /> (the default) grants nothing.
/// </param>
public sealed class ForwarderQueryResolver(
    ForwarderSetCache forwarderSets,
    IEnumerable<IUpstreamForwarderClient> clients,
    IUpstreamForwarderDefinitionParser definitionParser,
    ForwarderHealthTracker healthTracker,
    IDnssecValidator dnssecValidator,
    IOptionsMonitor<ForwardingOptions> forwardingOptions,
    IOptionsMonitor<RecursiveServerOptions> recursiveOptions,
    ILogger<ForwarderQueryResolver> logger,
    EdnsClientSubnetService clientSubnetService = null,
    IForwardedResponseCache responseCache = null,
    InsecureDomainPolicy insecureDomains = null,
    IDnsAccessControlEngine accessControlEngine = null
) : IDnsQueryResolver
{
    private string _seenInsecureDomains;

    private readonly IReadOnlyDictionary<
        UpstreamForwarderProtocolEnum,
        IUpstreamForwarderClient
    > _clientsByProtocol = clients.ToDictionary(client => client.Protocol);

    private readonly ConcurrentDictionary<string, UpstreamForwarderDefinition> _parsedDefinitions =
        new();

    /// <inheritdoc />
    public async ValueTask<DnsQueryResolution> TryResolveAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        if (!forwardingOptions.CurrentValue.Enable || context.Request.Questions.Count == 0)
            return DnsQueryResolution.NotHandled;

        // Only a QUERY is forwarded. The dispatcher answers NOTIMP for the opcodes it does not
        // implement and the NOTIFY and UPDATE resolvers claim theirs before this step, so this
        // check is defensive.
        if (context.Request.Opcode != DnsOpcodeEnum.Query)
            return DnsQueryResolution.NotHandled;

        // RFC 1034 §4.3.1: without RD the server answers from local data only.
        if (!context.Request.RecursionDesired)
            return DnsQueryResolution.NotHandled;

        // Forwarding is recursive service (BCP 140, RFC 5358), so it is limited to the clients the
        // recursive resolver serves. Checked before anything is sent or read from the cache.
        bool recursionPermitted = await RecursionPermission.IsPermittedAsync(
            recursiveOptions.CurrentValue,
            accessControlEngine,
            context.RemoteAddress,
            stoppingToken
        );

        if (!recursionPermitted)
            return DnsQueryResolution.NotHandled;

        DnsQuestion question = context.Request.Questions[0];

        FlushCacheWhenInsecureDomainsChanged();

        (IReadOnlyList<string> definitionStrings, bool zoneValidates) =
            await ResolveForwarderSetAsync(question.Name, stoppingToken);

        if (definitionStrings.Count == 0)
            return DnsQueryResolution.NotHandled;

        List<UpstreamForwarderDefinition> candidates = [];

        foreach (string definitionString in definitionStrings)
        {
            if (TryParse(definitionString, out UpstreamForwarderDefinition definition))
                candidates.Add(definition);
        }

        if (candidates.Count == 0)
            return DnsQueryResolution.NotHandled;

        IReadOnlyList<UpstreamForwarderDefinition> ordered =
            forwardingOptions.CurrentValue.FailoverStrategy
            == ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin
                ? healthTracker.OrderForAttempt(candidates)
                : candidates;

        // The global switch is the master: a zone's own switch can only turn validation off.
        bool validationEnabled =
            recursiveOptions.CurrentValue.EnableDnssecValidation && zoneValidates;

        // Built once per query: the ECS option is the same whichever forwarder answers.
        DnsEdnsOption clientSubnetOption = clientSubnetService?.BuildOutboundOption(
            context.RemoteAddress,
            context.Request
        );

        // A fresh message for the upstream hop. context.Request is shared with the resolvers this
        // dispatch may still fall through to, and the pipeline signs the reply from its TSIG, so
        // it is never changed or sent.
        DnsMessage outboundRequest = BuildOutboundRequest(
            context.Request,
            clientSubnetOption,
            validationEnabled
        );

        // Only a plain, single-question QUERY is cached: that is all a stub resolver ever sends,
        // and anything else (a NOTIFY or UPDATE that reached here, a multi-question message) has
        // no well-defined "same question" to reuse an answer for.
        ForwardedResponseCacheKey? cacheKey =
            responseCache is not null
            && context.Request.Opcode == DnsOpcodeEnum.Query
            && context.Request.Questions.Count == 1
                ? ForwardedResponseCacheKey.Create(
                    context.Request,
                    outboundRequest,
                    definitionStrings,
                    validationEnabled
                )
                : null;

        if (
            cacheKey is { } lookupKey
            && responseCache.TryGet(lookupKey, context.Request) is { } cached
        )
            return DnsQueryResolution.Handled(cached);

        foreach (UpstreamForwarderDefinition definition in ordered)
        {
            if (
                !_clientsByProtocol.TryGetValue(
                    definition.Protocol,
                    out IUpstreamForwarderClient client
                )
            )
                continue;

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(forwardingOptions.CurrentValue.PerForwarderTimeout);

            DnsMessage response;

            try
            {
                response = await client.ForwardAsync(definition, outboundRequest, timeoutCts.Token);
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException
                    || !stoppingToken.IsCancellationRequested
                )
            {
                healthTracker.RecordResult(definition, succeeded: false);
                logger.LogDebug(
                    exception,
                    "Forwarder '{Definition}' failed to answer a query for {Name}.",
                    definition.OriginalDefinitionString,
                    question.Name
                );
                continue;
            }

            // A lame or overloaded upstream answers SERVFAIL, REFUSED or NOTIMP (or garbles the
            // message with FORMERR). The next forwarder may know better, and the failure counts
            // against this one's health. NXDOMAIN is a real answer, so it stops the search
            // (RFC 1035 §4.1.1).
            if (
                response.ResponseCode
                is not (DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain)
            )
            {
                healthTracker.RecordResult(definition, succeeded: false);
                logger.LogDebug(
                    "Forwarder '{Definition}' answered {ResponseCode} for {Name}; trying the next one.",
                    definition.OriginalDefinitionString,
                    response.ResponseCode,
                    question.Name
                );
                continue;
            }

            healthTracker.RecordResult(definition, succeeded: true);
            response.Id = context.Request.Id;

            if (!validationEnabled)
                return Answer(cacheKey, response, context.Request, isSecure: false);

            DnssecValidationStateEnum state = await ValidateAsync(
                question,
                response,
                new ForwarderDnssecRecordFetcher(
                    ordered,
                    definitionStrings,
                    _clientsByProtocol,
                    healthTracker,
                    responseCache,
                    forwardingOptions,
                    logger
                ),
                stoppingToken
            );

            if (state == DnssecValidationStateEnum.Bogus)
            {
                // RFC 4035 §3.2.2: a client that set CD wants the data even when it does not
                // validate. It is returned without AD and not cached, so a client without CD
                // asking for the same name never sees it.
                if (context.Request.CheckingDisabled)
                    return Answer(cacheKey: null, response, context.Request, isSecure: false);

                healthTracker.RecordResult(definition, succeeded: false);
                logger.LogWarning(
                    "Forwarder '{Definition}' returned a Bogus (DNSSEC-invalid) answer for {Name}; treating it as a failed attempt.",
                    definition.OriginalDefinitionString,
                    question.Name
                );
                continue;
            }

            return Answer(
                cacheKey,
                response,
                context.Request,
                isSecure: state == DnssecValidationStateEnum.Secure
            );
        }

        // Every forwarder failed. RFC 8767: an expired answer inside the serve-stale window is
        // better than none. Without one, the dispatcher falls through to recursion.
        if (
            cacheKey is { } staleKey
            && responseCache.TryGetStale(staleKey, context.Request) is { } stale
        )
        {
            logger.LogDebug(
                "Every forwarder failed for {Name}; serving a stale cached answer.",
                question.Name
            );
            return DnsQueryResolution.Handled(stale);
        }

        // Only clients permitted recursion reach this point, so the recursive resolver, next in
        // the dispatcher, takes the query.
        return DnsQueryResolution.NotHandled;
    }

    /// <summary>
    ///     This method caches an accepted upstream answer (when it is cacheable) and returns the
    ///     reply to <paramref name="request" />, shaped for that client (see
    ///     <see cref="ForwardedResponseShaper" />). The cache takes a copy of the answer as
    ///     received, so what one client's reply drops or changes never reaches the next.
    /// </summary>
    private DnsQueryResolution Answer(
        ForwardedResponseCacheKey? cacheKey,
        DnsMessage response,
        DnsMessage request,
        bool isSecure
    )
    {
        if (cacheKey is { } key)
            responseCache.TryStore(key, response, isSecure);

        return DnsQueryResolution.Handled(
            ForwardedResponseShaper.Shape(response, request, isSecure, _ => null)
        );
    }

    /// <summary>
    ///     This method finds the forwarder set for a query: the closest enabled
    ///     <see cref="ZoneTypeEnum.Forwarder" /> or <see cref="ZoneTypeEnum.SecondaryForwarder" />
    ///     zone with addresses, otherwise the enabled global <see cref="DnsForwarderEntity" /> set,
    ///     in order (see <see cref="ForwarderSetCache" />). The result also says whether DNSSEC
    ///     validation may run for the set: a zone's <see cref="ZoneEntity.ValidateDnssec" />, always
    ///     <see langword="true" /> for the global set. A
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone gets its
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> from <c>SecondaryZoneRefreshService</c>,
    ///     which transfers them from a forwarder-zone primary's apex TXT records; here it behaves
    ///     like any forwarder zone. This reads no database rows while the cache copy is fresh.
    /// </summary>
    private async ValueTask<(
        IReadOnlyList<string> Definitions,
        bool Validates
    )> ResolveForwarderSetAsync(DnsName queriedName, CancellationToken stoppingToken)
    {
        ForwarderSet set = await forwarderSets.FindAsync(queriedName, stoppingToken);

        return (set.Definitions, set.Validates);
    }

    /// <summary>
    ///     This method empties the response cache when the Insecure domain list differs from the
    ///     one the last query saw. Entries store whether an answer was Secure (and so gets AD), and
    ///     the list decides that, so an entry made under another list must not be replayed. The
    ///     first query only records the list.
    /// </summary>
    private void FlushCacheWhenInsecureDomainsChanged()
    {
        if (insecureDomains is null || responseCache is null)
            return;

        string current = insecureDomains.Fingerprint;
        string previous = Interlocked.Exchange(ref _seenInsecureDomains, current);

        if (previous is not null && previous != current)
            responseCache.Flush();
    }

    /// <summary>
    ///     This method parses a forwarder's definition string, caching the result in
    ///     <see cref="_parsedDefinitions" />. An unparseable definition is logged and skipped on
    ///     every query that uses it, rather than failing the query.
    /// </summary>
    private bool TryParse(string definitionString, out UpstreamForwarderDefinition definition)
    {
        if (_parsedDefinitions.TryGetValue(definitionString, out definition))
            return true;

        try
        {
            definition = definitionParser.Parse(definitionString);
            _parsedDefinitions[definitionString] = definition;
            return true;
        }
        catch (FormatException exception)
        {
            logger.LogWarning(
                exception,
                "Skipping an unparseable forwarder definition: '{Definition}'.",
                definitionString
            );
            definition = null;
            return false;
        }
    }

    /// <summary>
    ///     This method validates a forwarded response with <see cref="IDnssecValidator" />, first
    ///     turning it into the <see cref="IterativeResolutionResult" /> shape the validator expects
    ///     (see <see cref="BuildResult" />). A response code other than
    ///     <see cref="DnsResponseCodeEnum.NoError" /> or <see cref="DnsResponseCodeEnum.NxDomain" />
    ///     has nothing to validate and is treated as <see cref="DnssecValidationStateEnum.Insecure" />,
    ///     so it is returned as received. So is an ANY query, whose answer is a mix of types that
    ///     the validator does not check as one RRset.
    /// </summary>
    /// <param name="question">The client's question.</param>
    /// <param name="response">The forwarder's answer.</param>
    /// <param name="fetcher">Supplies the DS and DNSKEY records of the chain of trust.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    private async Task<DnssecValidationStateEnum> ValidateAsync(
        DnsQuestion question,
        DnsMessage response,
        IDnssecRecordFetcher fetcher,
        CancellationToken stoppingToken
    )
    {
        if (
            response.ResponseCode
                is not (DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain)
            || question.Type == DnsResourceRecordTypeEnum.Any
        )
            return DnssecValidationStateEnum.Insecure;

        IterativeResolutionResult result = BuildResult(question, response);
        QueryBudget budget = new(recursiveOptions.CurrentValue);

        DnssecValidationResult validation = await dnssecValidator.ValidateAsync(
            question.Name,
            question.Type,
            question.Class,
            result,
            budget,
            stoppingToken,
            fetcher
        );

        return validation.State;
    }

    /// <summary>
    ///     This method builds the query sent to an upstream forwarder: a new message made for this
    ///     hop, not a relay of the client's. The header keeps the client's ID (the forwarder client
    ///     may replace it), opcode and RD bit, and the questions are copied. CD is set when this
    ///     server validates (RFC 6840 §5.9: a validating resolver sets CD upstream, so the upstream
    ///     returns the data and its signatures even when its own validation fails) and otherwise
    ///     follows the client's CD bit.
    ///     <para>
    ///         An OPT record is added when the client sent EDNS, when validation is on, or when an
    ///         ECS option was built (RFC 6891 §6.1.1: the OPT record is per hop, so it is rebuilt
    ///         and never copied). It keeps the client's payload size and DO bit, with DO always
    ///         set when validating, and carries ECS as its only option. The client's cookie
    ///         (RFC 7873, a client-server pair), padding, NSID and every other option belong to
    ///         the client-to-server hop and are never relayed. The client's own ECS option is
    ///         relayed only when validating without an ECS service, as the validating query has
    ///         always done. The TSIG record is never sent: its MAC covers the request ID and this
    ///         server's key, which the upstream cannot check, and forwarding it would leak the
    ///         key name.
    ///     </para>
    /// </summary>
    /// <param name="clientRequest">The client's request, which is not changed.</param>
    /// <param name="clientSubnetOption">
    ///     The ECS option built for this client, or <see langword="null" /> when there is none.
    /// </param>
    /// <param name="validating">Whether this server validates the answer.</param>
    private static DnsMessage BuildOutboundRequest(
        DnsMessage clientRequest,
        DnsEdnsOption clientSubnetOption,
        bool validating
    )
    {
        DnsMessage outbound = new()
        {
            Id = clientRequest.Id,
            Opcode = clientRequest.Opcode,
            RecursionDesired = clientRequest.RecursionDesired,
            CheckingDisabled = validating || clientRequest.CheckingDisabled,
        };
        outbound.Questions.AddRange(clientRequest.Questions);

        DnsEdnsOption subnet =
            clientSubnetOption
            ?? (
                validating
                    ? clientRequest.Edns?.Options.FirstOrDefault(option =>
                        option.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
                    )
                    : null
            );

        if (!validating && clientRequest.Edns is null && subnet is null)
            return outbound;

        outbound.Edns = new DnsEdnsOptions(
            0,
            clientRequest.Edns is { RequestorPayloadSize: > 0 } edns
                ? edns.RequestorPayloadSize
                : DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
            validating || clientRequest.Edns?.DnssecOk == true,
            subnet is null ? [] : [subnet]
        );

        return outbound;
    }

    /// <summary>
    ///     This method turns a forwarded response into the shape <see cref="IDnssecValidator" />
    ///     expects, the way the iterative engine would have built it.
    ///     <list type="bullet">
    ///         <item>
    ///             The CNAME chain is followed from the queried name through the answer section,
    ///             one <see cref="CnameChainHop" /> per link with the RRSIGs the answer holds for
    ///             it. The hop's zone is left empty: the validator takes it from the RRSIG signer
    ///             or walks the delegations itself.
    ///         </item>
    ///         <item>
    ///             A positive answer is the chain plus the records of the queried type owned by
    ///             the end of the chain. Its DNSSEC records are the RRSIGs over that RRset and the
    ///             authority section's NSEC and NSEC3 records with their RRSIGs, which prove that a
    ///             wildcard expansion had no closer match (RFC 4035 §5.3.4, RFC 5155 §8.8).
    ///         </item>
    ///         <item>
    ///             Without such records the answer is negative for the end of the chain: NXDOMAIN
    ///             for that response code, otherwise NODATA. Its SOA and proof are the authority
    ///             section's SOA, NSEC, NSEC3 and RRSIG records.
    ///         </item>
    ///     </list>
    ///     No zone cuts are known, so the validator starts any unsigned-answer walk at the root.
    /// </summary>
    internal static IterativeResolutionResult BuildResult(DnsQuestion question, DnsMessage response)
    {
        DnsName end = question.Name;
        List<DnsResourceRecord> chain = [];
        List<CnameChainHop> hops = [];

        // A name seen twice is a loop; the validator rejects the chain it can build.
        HashSet<DnsName> seen = [end];

        while (question.Type != DnsResourceRecordTypeEnum.Cname)
        {
            DnsResourceRecord cname = response.Answers.FirstOrDefault(record =>
                record.Type == DnsResourceRecordTypeEnum.Cname && record.Name == end
            );

            if (
                cname is null
                || DnsRecordDataCodecRegistry.Decode(cname.Type, cname.Data)
                    is not CnameRecordData cnameData
                || !seen.Add(cnameData.Target)
            )
                break;

            chain.Add(cname);
            hops.Add(
                new CnameChainHop(
                    cname,
                    SignaturesOver(response.Answers, end, DnsResourceRecordTypeEnum.Cname),
                    null
                )
            );
            end = cnameData.Target;
        }

        List<DnsResourceRecord> terminal =
        [
            .. response.Answers.Where(record => record.Type == question.Type && record.Name == end),
        ];

        if (response.ResponseCode == DnsResponseCodeEnum.NoError && terminal.Count > 0)
            return new IterativeResolutionResult(
                RecursiveResolutionStatusEnum.Success,
                [.. chain, .. terminal],
                [
                    .. SignaturesOver(response.Answers, end, question.Type),
                    .. response.AuthorityRecords.Where(IsProof),
                ],
                null,
                [],
                hops
            );

        return new IterativeResolutionResult(
            response.ResponseCode == DnsResponseCodeEnum.NxDomain
                ? RecursiveResolutionStatusEnum.NxDomain
                : RecursiveResolutionStatusEnum.NoData,
            [],
            [
                .. response.AuthorityRecords.Where(record =>
                    record.Type
                        is DnsResourceRecordTypeEnum.RrSig
                            or DnsResourceRecordTypeEnum.Nsec
                            or DnsResourceRecordTypeEnum.Nsec3
                ),
            ],
            response.AuthorityRecords.FirstOrDefault(record =>
                record.Type == DnsResourceRecordTypeEnum.Soa
            ),
            [],
            hops
        );
    }

    /// <summary>
    ///     This method picks the RRSIG records in <paramref name="records" /> that are owned by
    ///     <paramref name="owner" /> and cover <paramref name="coveredType" />.
    /// </summary>
    private static List<DnsResourceRecord> SignaturesOver(
        IEnumerable<DnsResourceRecord> records,
        DnsName owner,
        DnsResourceRecordTypeEnum coveredType
    ) =>
        [
            .. records.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.RrSig
                && record.Name == owner
                && DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                    is RrSigRecordData signature
                && signature.TypeCovered == coveredType
            ),
        ];

    /// <summary>
    ///     This method tells whether a record is an NSEC or NSEC3 record, or an RRSIG covering
    ///     one: the records of a denial or wildcard proof.
    /// </summary>
    private static bool IsProof(DnsResourceRecord record) =>
        record.Type is DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
        || (
            record.Type == DnsResourceRecordTypeEnum.RrSig
            && DnsRecordDataCodecRegistry.Decode(record.Type, record.Data)
                is RrSigRecordData
                {
                    TypeCovered: DnsResourceRecordTypeEnum.Nsec or DnsResourceRecordTypeEnum.Nsec3
                }
        );
}
