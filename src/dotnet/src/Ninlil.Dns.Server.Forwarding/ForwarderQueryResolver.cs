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
using Ninlil.Dns.Server.Recursive;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class implements the dispatcher-level forwarding step. Registered as an
///     <see cref="IDnsQueryResolver" /> <b>between</b> <c>Server.Authoritative</c>'s own resolver
///     and <c>Server.Recursive</c>'s own resolver (see
///     <see cref="Extension.ServiceCollectionForwardingExtension.AddNinlilDnsServerForwarding" />'s
///     own doc comment for the required registration order) -- a name this server does not host
///     itself is offered to a configured forwarder first; only once every configured forwarder
///     fails does the dispatcher fall through to this server's own iterative resolution, per the
///     "forwarder-first, iterate on failure" design decision.
///     <para>
///         Every forwarded answer is re-validated against the DNSSEC chain of trust locally
///         (when <see cref="RecursiveServerOptions.EnableDnssecValidation" /> is on), via the
///         exact same <see cref="IDnssecValidator" /> a normal iterative answer would use --
///         this server's own "full validating resolver" guarantee holds regardless of which path
///         actually answered. A <see cref="DnssecValidationStateEnum.Bogus" /> result is treated
///         identically to the forwarder itself having failed (tried next, or fallen through to
///         iteration) -- a forwarder that returns a plausible-looking but cryptographically
///         invalid answer gets no more trust than one that timed out.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     Used to open a fresh scope per query for <see cref="IZoneRepository" />/
///     <see cref="IDnsForwarderRepository" /> access -- this resolver is registered as a
///     singleton (matching <c>AuthoritativeQueryResolver</c>/<c>RecursiveQueryResolver</c>'s own
///     registration), but those two repositories are scoped, the same captive-dependency
///     concern <c>CloudDnsZoneSyncService</c> already solves the same way.
/// </param>
/// <param name="clients">Every registered forwarder client, one per protocol.</param>
/// <param name="definitionParser">Parses a forwarder's own definition string.</param>
/// <param name="healthTracker">Tracks per-forwarder health for the round-robin strategy.</param>
/// <param name="dnssecValidator">Re-validates a forwarded answer's own DNSSEC status locally.</param>
/// <param name="forwardingOptions">
///     This resolver's own configured <see cref="ForwardingOptions.Enable" /> gate, failover
///     strategy, and timeouts -- read live via <see cref="IOptionsMonitor{TOptions}" /> rather
///     than the older, snapshotted <see cref="IOptions{TOptions}" />, since this resolver is now
///     always registered as a DI singleton (<c>ServiceCollectionForwardingExtension</c>)
///     regardless of <see cref="ForwardingOptions.Enable" />'s own value at process startup -- a
///     <c>ForwardingSettingsController.Patch</c> write toggling it on or off must be observed on
///     this same, already-constructed instance's very next query, with no restart.
/// </param>
/// <param name="recursiveOptions">
///     Read only for <see cref="RecursiveServerOptions.EnableDnssecValidation" /> (should a
///     forwarded answer be re-validated at all) and to build the <see cref="QueryBudget" /> a
///     re-validation's own DS/DNSKEY lookups are bounded by -- read live via
///     <see cref="IOptionsMonitor{TOptions}" />, the same reason
///     <see cref="Ninlil.Dns.Server.Recursive.Resolution.RecursiveQueryResolver" />'s own doc
///     comment gives: this resolver, too, is registered as a singleton, so the usual snapshotted
///     <see cref="IOptions{TOptions}" /> would never see a live
///     <c>RecursiveSettingsController.Patch</c> write.
/// </param>
/// <param name="logger">The logger this resolver records a forwarder failure or Bogus result to.</param>
/// <param name="clientSubnetService">
///     The RFC 7871 EDNS Client Subnet builder this resolver consults to attach an ECS option to
///     every query it forwards upstream on a client's behalf. Defaults to <see langword="null" />
///     -- the same optional-cross-cutting-dependency idiom this project's other resolvers already
///     establish for a similarly optional engine -- in which case no ECS option is ever attached,
///     functionally identical to <see cref="EdnsClientSubnetOptions.Enable" /> being off.
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
    EdnsClientSubnetService clientSubnetService = null
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

        // Built once per query, outside the per-forwarder retry loop below: the ECS option (if
        // this feature is enabled at all -- off by default) is the same regardless of which
        // forwarder ends up answering. clientSubnetService.AttachToRequest clones the request
        // rather than mutating context.Request in place, since that instance is shared with
        // every other IDnsQueryResolver this same dispatch might still fall through to.
        DnsEdnsOption clientSubnetOption = clientSubnetService?.BuildOutboundOption(
            context.RemoteAddress,
            context.Request
        );
        DnsMessage outboundRequest = clientSubnetOption is null
            ? context.Request
            : EdnsClientSubnetService.AttachToRequest(context.Request, clientSubnetOption);

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

            if (!recursiveOptions.CurrentValue.EnableDnssecValidation)
                return DnsQueryResolution.Handled(response);

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

            return DnsQueryResolution.Handled(response);
        }

        return DnsQueryResolution.NotHandled;
    }

    /// <summary>
    ///     This method finds the forwarder set to use for a query: a matching
    ///     <see cref="ZoneTypeEnum.Forwarder" />/<see cref="ZoneTypeEnum.SecondaryForwarder" />
    ///     zone's own <see cref="ZoneEntity.ForwarderAddresses" /> takes priority over the global
    ///     <see cref="DnsForwarderEntity" /> set. A <see cref="ZoneTypeEnum.SecondaryForwarder" />
    ///     zone's own <see cref="ZoneEntity.ForwarderAddresses" /> is populated by
    ///     <c>Refresh.SecondaryZoneRefreshService</c>'s own AXFR/IXFR pull from its configured
    ///     <see cref="ZoneTypeEnum.Forwarder" /> primary, decoded back from that primary's own
    ///     apex TXT records -- from this resolver's own point of view, once populated, it behaves
    ///     identically to an operator-authored <see cref="ZoneTypeEnum.Forwarder" /> zone.
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
    ///     This method parses (and caches, per <see cref="_parsedDefinitions" />) a forwarder's
    ///     own definition string, so repeat queries never re-parse the same string -- an
    ///     unparseable definition is logged once and skipped, rather than failing the whole query.
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
    ///     This method re-validates a forwarded response's own DNSSEC status, reusing the exact
    ///     same <see cref="IDnssecValidator" /> a normal iterative answer would use. Mirrors
    ///     <c>IterativeResolutionEngine</c>'s own Answer-section/Authority-section splitting
    ///     logic when building the <see cref="IterativeResolutionResult" /> the validator expects.
    ///     A response code other than <see cref="DnsResponseCodeEnum.NoError" />/
    ///     <see cref="DnsResponseCodeEnum.NxDomain" /> (SERVFAIL, REFUSED, ...) carries no
    ///     answer or negative-proof content to validate at all, and is treated as
    ///     <see cref="DnssecValidationStateEnum.Insecure" /> -- returned to the client as-is,
    ///     exactly like this server's own iterative resolver would for the same response code.
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
    ///     This method builds the negative-answer shape <see cref="IDnssecValidator" /> expects
    ///     for an NXDOMAIN or NODATA response -- the response's own SOA and NSEC/NSEC3/RRSIG
    ///     authority-section records, needed to validate the negative-answer proof itself.
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
