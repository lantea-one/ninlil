using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
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
///     <see cref="ForwardingOptions.PerForwarderTimeout" />; when all fail, the dispatcher falls
///     through to recursive iteration.
///     <para>
///         <b>DNSSEC.</b> When <see cref="RecursiveServerOptions.EnableDnssecValidation" /> is
///         on, the query sent upstream has EDNS with DO and CD set, whatever the client sent
///         (RFC 6840 §5.9: a validating resolver sets CD upstream, so the upstream returns the
///         data and its signatures even when its own validation fails). Only the client's ECS
///         option is kept; its cookie and other options belong to the client-to-server hop.
///         Each answer is then checked locally with the same <see cref="IDnssecValidator" /> as
///         an iterative answer: the CNAME chain is rebuilt from the answer section, a negative
///         answer or a wildcard expansion carries the NSEC or NSEC3 proof the validator needs,
///         and the DS and DNSKEY records of the chain of trust come from the validator's
///         default source, the iterative engine. A
///         <see cref="DnssecValidationStateEnum.Bogus" /> answer counts as a failed forwarder, so
///         the next one is tried, unless the client set CD (RFC 4035 §3.2.2): it then gets the
///         data without AD, and the answer is not cached. Every other result is accepted.
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
///         before the reply is shaped. The pipeline filters
///         whatever the dispatcher returns, so a cache hit is filtered per client like a fresh
///         answer. When every forwarder fails, an expired answer is served stale if the
///         server-wide RFC 8767 serve-stale setting allows it, before falling through to
///         recursion.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to open a scope per query for the scoped <see cref="IZoneRepository" /> and
///     <see cref="IDnsForwarderRepository" />, since this resolver is a singleton.
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
public sealed class ForwarderQueryResolver(
    IServiceScopeFactory scopeFactory,
    IEnumerable<IUpstreamForwarderClient> clients,
    IUpstreamForwarderDefinitionParser definitionParser,
    ForwarderHealthTracker healthTracker,
    IDnssecValidator dnssecValidator,
    IOptionsMonitor<ForwardingOptions> forwardingOptions,
    IOptionsMonitor<RecursiveServerOptions> recursiveOptions,
    ILogger<ForwarderQueryResolver> logger,
    EdnsClientSubnetService clientSubnetService = null,
    IForwardedResponseCache responseCache = null
) : IDnsQueryResolver
{
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

        DnsQuestion question = context.Request.Questions[0];

        IReadOnlyList<string> definitionStrings = await ResolveForwarderSetAsync(
            question.Name,
            stoppingToken
        );

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

        bool validationEnabled = recursiveOptions.CurrentValue.EnableDnssecValidation;

        // Built once per query: the ECS option is the same whichever forwarder answers.
        // AttachToRequest returns a copy, because context.Request is shared with the resolvers
        // this dispatch may still fall through to.
        DnsEdnsOption clientSubnetOption = clientSubnetService?.BuildOutboundOption(
            context.RemoteAddress,
            context.Request
        );
        DnsMessage outboundRequest =
            validationEnabled ? BuildValidatingRequest(context.Request, clientSubnetOption)
            : clientSubnetOption is null ? context.Request
            : EdnsClientSubnetService.AttachToRequest(context.Request, clientSubnetOption);

        // The client's TSIG belongs to the hop between the client and this server: its MAC covers
        // the request ID and this server's key, which the upstream cannot check, and forwarding it
        // would leak the key name. The ECS copy and the validating copy never carry it.
        if (outboundRequest.Tsig is not null)
            outboundRequest = WithoutTsig(outboundRequest);

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
                healthTracker.RecordResult(definition, succeeded: true);
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

            response.Id = context.Request.Id;

            if (!validationEnabled)
                return Answer(cacheKey, response, context.Request, isSecure: false);

            DnssecValidationStateEnum state = await ValidateAsync(
                question,
                response,
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
    ///     This method finds the forwarder set for a query. It walks from the queried name up to
    ///     the root, and the first enabled <see cref="ZoneTypeEnum.Forwarder" /> or
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone with addresses wins; otherwise the
    ///     enabled global <see cref="DnsForwarderEntity" /> set is used, in order. A
    ///     <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone gets its
    ///     <see cref="ZoneEntity.ForwarderAddresses" /> from <c>SecondaryZoneRefreshService</c>,
    ///     which transfers them from a forwarder-zone primary's apex TXT records; here it behaves
    ///     like any forwarder zone. This costs one zone lookup per label on every query.
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveForwarderSetAsync(
        DnsName queriedName,
        CancellationToken stoppingToken
    )
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        for (int start = 0; start <= queriedName.Labels.Count; start++)
        {
            DnsName candidateZoneName = new([.. queriedName.Labels.Skip(start)]);
            string zoneNameText = candidateZoneName.ToString().TrimEnd('.');

            ZoneEntity zone = await zoneRepository.FindByNameAsync(zoneNameText, stoppingToken);

            if (
                zone
                    is {
                        Type: ZoneTypeEnum.Forwarder or ZoneTypeEnum.SecondaryForwarder,
                        IsDisabled: false,
                    }
                && zone.ForwarderAddresses.Count > 0
            )
                return zone.ForwarderAddresses;
        }

        IDnsForwarderRepository forwarderRepository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();
        IReadOnlyList<DnsForwarderEntity> globalForwarders =
            await forwarderRepository.ListEnabledOrderedAsync(stoppingToken);

        return [.. globalForwarders.Select(forwarder => forwarder.DefinitionString)];
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
    private async Task<DnssecValidationStateEnum> ValidateAsync(
        DnsQuestion question,
        DnsMessage response,
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
            stoppingToken
        );

        return validation.State;
    }

    /// <summary>
    ///     This method builds the outbound query for a validating forwarder: the client's
    ///     question, ID and RD bit, with CD set and EDNS with DO set (RFC 6840 §5.9), so the
    ///     upstream answers with its RRSIGs and NSEC proofs even for a client that did not ask
    ///     for them and even when the upstream's own validation fails. The client's payload size
    ///     and ECS option are kept; every other EDNS option, and the TSIG, are left behind
    ///     because they belong to the client-to-server hop.
    /// </summary>
    /// <param name="clientRequest">The client's request, which is not changed.</param>
    /// <param name="clientSubnetOption">
    ///     The ECS option built for this client, or <see langword="null" /> to relay the client's
    ///     own ECS option, if it sent one.
    /// </param>
    private static DnsMessage BuildValidatingRequest(
        DnsMessage clientRequest,
        DnsEdnsOption clientSubnetOption
    )
    {
        DnsEdnsOption subnet =
            clientSubnetOption
            ?? clientRequest.Edns?.Options.FirstOrDefault(option =>
                option.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
            );

        DnsMessage outbound = new()
        {
            Id = clientRequest.Id,
            Opcode = clientRequest.Opcode,
            RecursionDesired = clientRequest.RecursionDesired,
            CheckingDisabled = true,
            Edns = new DnsEdnsOptions(
                0,
                clientRequest.Edns is { RequestorPayloadSize: > 0 } edns
                    ? edns.RequestorPayloadSize
                    : DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                true,
                subnet is null ? [] : [subnet]
            ),
        };
        outbound.Questions.AddRange(clientRequest.Questions);

        return outbound;
    }

    /// <summary>
    ///     This method copies a request without its TSIG record. The shared
    ///     <c>DnsQueryContext.Request</c> must keep it, because the pipeline signs the reply from it.
    /// </summary>
    private static DnsMessage WithoutTsig(DnsMessage original) =>
        new()
        {
            Id = original.Id,
            IsResponse = original.IsResponse,
            Opcode = original.Opcode,
            IsAuthoritativeAnswer = original.IsAuthoritativeAnswer,
            IsTruncated = original.IsTruncated,
            RecursionDesired = original.RecursionDesired,
            RecursionAvailable = original.RecursionAvailable,
            AuthenticData = original.AuthenticData,
            CheckingDisabled = original.CheckingDisabled,
            ResponseCode = original.ResponseCode,
            Questions = [.. original.Questions],
            Answers = [.. original.Answers],
            AuthorityRecords = [.. original.AuthorityRecords],
            AdditionalRecords = [.. original.AdditionalRecords],
            Edns = original.Edns,
            Tsig = null,
        };

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
    private static IterativeResolutionResult BuildResult(DnsQuestion question, DnsMessage response)
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
