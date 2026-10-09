using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Recursive.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Validation;

/// <summary>
///     This class implements <see cref="IDnssecRecordFetcher" /> over the forwarders, so the DS
///     and DNSKEY records a forwarded answer's chain of trust needs come from the same servers
///     that gave the answer, and a network that only reaches its forwarders still validates.
///     <see cref="ForwarderQueryResolver" /> builds one per query and hands it to
///     <see cref="IDnssecValidator.ValidateAsync" />.
///     <para>
///         Every lookup is a query with RD, CD and EDNS DO set (RFC 6840 §5.9: the upstream then
///         returns the data and its signatures even when its own validation fails), with no
///         other EDNS option and no TSIG. The forwarders are tried in the order given, each for
///         at most <see cref="ForwardingOptions.PerForwarderTimeout" />; every attempt consumes
///         <see cref="QueryBudget.TryConsumeQuery" />. A forwarder that fails, times out or
///         answers with an error code is skipped. The answer goes through
///         <see cref="IForwardedResponseCache" /> under a key built from the same forwarder set
///         with DO and CD set, so a repeated lookup sends nothing upstream. The cached message is
///         stored as not Secure: it is data for the validator, which checks it against the trust
///         anchor itself.
///     </para>
/// </summary>
/// <param name="forwarders">The forwarders to ask, already ordered for this query.</param>
/// <param name="upstreamSet">
///     The forwarder definition strings of the query, in configuration order: the cache key's
///     upstream set (see <see cref="ForwardedResponseCacheKey.UpstreamSet" />).
/// </param>
/// <param name="clients">The forwarder clients by protocol.</param>
/// <param name="healthTracker">Told the outcome of every attempt, like the answer query.</param>
/// <param name="responseCache">
///     The forwarded-response cache; <see langword="null" /> sends every lookup upstream.
/// </param>
/// <param name="forwardingOptions">Read live for the per-forwarder timeout.</param>
/// <param name="logger">The logger a failed attempt is recorded to.</param>
public sealed class ForwarderDnssecRecordFetcher(
    IReadOnlyList<UpstreamForwarderDefinition> forwarders,
    IReadOnlyList<string> upstreamSet,
    IReadOnlyDictionary<UpstreamForwarderProtocolEnum, IUpstreamForwarderClient> clients,
    ForwarderHealthTracker healthTracker,
    IForwardedResponseCache responseCache,
    IOptionsMonitor<ForwardingOptions> forwardingOptions,
    ILogger logger
) : IDnssecRecordFetcher
{
    /// <inheritdoc />
    public async Task<IterativeResolutionResult> FetchAsync(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        QueryBudget budget,
        CancellationToken stoppingToken
    )
    {
        DnsQuestion question = new(name, type, recordClass);
        DnsMessage request = new()
        {
            Id = (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1),
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            CheckingDisabled = true,
            Edns = new DnsEdnsOptions(0, DnsQueryDispatcher.DefaultUdpPayloadSizeOctets, true, []),
        };
        request.Questions.Add(question);

        ForwardedResponseCacheKey key = ForwardedResponseCacheKey.Create(
            request,
            request,
            upstreamSet,
            validated: true
        );

        if (responseCache?.TryGet(key, request) is { } cached)
            return ForwarderQueryResolver.BuildResult(question, cached);

        foreach (UpstreamForwarderDefinition definition in forwarders)
        {
            if (!clients.TryGetValue(definition.Protocol, out IUpstreamForwarderClient client))
                continue;

            if (!budget.TryConsumeQuery())
                return IterativeResolutionResult.ServFail();

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(forwardingOptions.CurrentValue.PerForwarderTimeout);

            DnsMessage response;

            try
            {
                response = await client.ForwardAsync(definition, request, timeoutCts.Token);
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
                    "Forwarder '{Definition}' failed to answer a {Type} lookup for {Name}.",
                    definition.OriginalDefinitionString,
                    type,
                    name
                );
                continue;
            }

            // SERVFAIL, REFUSED and the like carry no records for the chain; another forwarder
            // may do better.
            if (
                response.ResponseCode
                is not (DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain)
            )
                continue;

            responseCache?.TryStore(key, response, isSecure: false);

            return ForwarderQueryResolver.BuildResult(question, response);
        }

        return IterativeResolutionResult.ServFail();
    }
}
