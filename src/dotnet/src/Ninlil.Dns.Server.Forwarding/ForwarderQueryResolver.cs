using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
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
///         When <see cref="RecursiveServerOptions.EnableDnssecValidation" /> is on, each
///         forwarded answer is checked locally with the same <see cref="IDnssecValidator" /> as an
///         iterative answer. A <see cref="DnssecValidationStateEnum.Bogus" /> result counts as a
///         failed forwarder, so the next one is tried. Other results are returned as received;
///         the upstream's header flags, including AD, are passed through unchanged.
///     </para>
///     <para>
///         <b>Caching.</b> Answers are cached inside this resolver rather than around the
///         dispatcher, because only this resolver knows which forwarder set and ECS subnet a query
///         uses, and both are part of the cache key (see <see cref="ForwardedResponseCacheKey" />).
///         The lookup happens after the forwarder set is chosen and before anything is sent; an
///         accepted, cacheable answer is stored before it is returned. The pipeline filters
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

        // Built once per query: the ECS option is the same whichever forwarder answers.
        // AttachToRequest returns a copy, because context.Request is shared with the resolvers
        // this dispatch may still fall through to.
        DnsEdnsOption clientSubnetOption = clientSubnetService?.BuildOutboundOption(
            context.RemoteAddress,
            context.Request
        );
        DnsMessage outboundRequest = clientSubnetOption is null
            ? context.Request
            : EdnsClientSubnetService.AttachToRequest(context.Request, clientSubnetOption);

        // The client's TSIG belongs to the hop between the client and this server: its MAC covers
        // the request ID and this server's key, which the upstream cannot check, and forwarding it
        // would leak the key name. The ECS copy above never carries it.
        if (outboundRequest.Tsig is not null)
            outboundRequest = WithoutTsig(outboundRequest);

        bool validationEnabled = recursiveOptions.CurrentValue.EnableDnssecValidation;

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
                return Answer(cacheKey, response);

            DnssecValidationStateEnum state = await ValidateAsync(
                question,
                response,
                stoppingToken
            );

            if (state == DnssecValidationStateEnum.Bogus)
            {
                healthTracker.RecordResult(definition, succeeded: false);
                logger.LogWarning(
                    "Forwarder '{Definition}' returned a Bogus (DNSSEC-invalid) answer for {Name}; treating it as a failed attempt.",
                    definition.OriginalDefinitionString,
                    question.Name
                );
                continue;
            }

            return Answer(cacheKey, response);
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
    ///     This method caches an accepted upstream answer (when it is cacheable) and returns it as
    ///     this resolver's result. The cache takes a copy, so the pipeline may change
    ///     <paramref name="response" /> afterwards.
    /// </summary>
    private DnsQueryResolution Answer(ForwardedResponseCacheKey? cacheKey, DnsMessage response)
    {
        if (cacheKey is { } key)
            responseCache.TryStore(key, response);

        return DnsQueryResolution.Handled(response);
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
    ///     (answer and RRSIG records for a positive answer, authority SOA and proofs for NXDOMAIN or
    ///     NODATA). A response code other than <see cref="DnsResponseCodeEnum.NoError" /> or
    ///     <see cref="DnsResponseCodeEnum.NxDomain" /> has nothing to validate and is treated as
    ///     <see cref="DnssecValidationStateEnum.Insecure" />, so it is returned as received.
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
        )
            return DnssecValidationStateEnum.Insecure;

        IterativeResolutionResult result;

        if (response.ResponseCode == DnsResponseCodeEnum.NxDomain)
        {
            result = BuildNegativeResult(RecursiveResolutionStatusEnum.NxDomain, response);
        }
        else
        {
            List<DnsResourceRecord> exactAnswers =
            [
                .. response.Answers.Where(record => record.Type == question.Type),
            ];

            if (exactAnswers.Count == 0)
            {
                result = BuildNegativeResult(RecursiveResolutionStatusEnum.NoData, response);
            }
            else
            {
                List<DnsResourceRecord> answer =
                [
                    .. response.Answers.Where(record =>
                        record.Type == question.Type
                        || record.Type == DnsResourceRecordTypeEnum.Cname
                    ),
                ];
                List<DnsResourceRecord> dnssecRecords =
                [
                    .. response.Answers.Where(record =>
                        record.Type == DnsResourceRecordTypeEnum.RrSig
                    ),
                ];

                result = new IterativeResolutionResult(
                    RecursiveResolutionStatusEnum.Success,
                    answer,
                    dnssecRecords,
                    null,
                    []
                );
            }
        }

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
    ///     This method builds the negative-answer shape <see cref="IDnssecValidator" /> expects
    ///     for an NXDOMAIN or NODATA response: the authority section's SOA and its NSEC, NSEC3 and
    ///     RRSIG records, which make up the denial proof.
    /// </summary>
    private static IterativeResolutionResult BuildNegativeResult(
        RecursiveResolutionStatusEnum status,
        DnsMessage response
    )
    {
        DnsResourceRecord soa = response.AuthorityRecords.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.Soa
        );
        List<DnsResourceRecord> dnssecRecords =
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type
                    is DnsResourceRecordTypeEnum.RrSig
                        or DnsResourceRecordTypeEnum.Nsec
                        or DnsResourceRecordTypeEnum.Nsec3
            ),
        ];

        return new IterativeResolutionResult(status, [], dnssecRecords, soa, []);
    }
}
