using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Recursive.Caching;
using Ninlil.Dns.Server.Recursive.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Resolution;

/// <summary>
///     This class keeps <see cref="IRecursiveCache" /> supplied with the root name servers, so
///     <see cref="IterativeResolutionEngine" /> always has the root to start from.
///     <see cref="StartAsync" /> first stores the <see cref="RootHints" /> list (or its override)
///     with a six-day TTL through <see cref="RootHintsSeeder" />, which needs no network. The
///     background loop then sends an RFC 8109 priming query (<c>NS .</c>) at once and replaces the
///     list with the root's answer and glue. Each attempt tries the hint addresses of both
///     families in random order until one server answers authoritatively with an NS RRset for the
///     root. The loop primes again when 90 percent of that NS TTL has elapsed, so the entries never
///     lapse on an idle resolver. After a failed attempt it retries after one minute, doubling up
///     to one hour; the seeded list stays in the cache meanwhile. The options come from
///     <see cref="IOptions{TOptions}" />, which is not reloaded, so every attempt uses the root
///     hints override as it was at startup.
/// </summary>
/// <param name="cache">The cache this service seeds.</param>
/// <param name="upstreamClient">The client used to send the priming query.</param>
/// <param name="options">The root hints override and whether to set DO on the priming query.</param>
/// <param name="logger">The logger this service records a failed priming query to.</param>
/// <param name="timeProvider">The clock the re-prime and retry delays use; the system clock by default.</param>
public sealed class RootHintsPrimingService(
    IRecursiveCache cache,
    IUpstreamDnsClient upstreamClient,
    IOptions<RecursiveServerOptions> options,
    ILogger<RootHintsPrimingService> logger,
    TimeProvider timeProvider = null
) : BackgroundService
{
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromHours(1);

    /// <summary>
    ///     The fraction of the root NS TTL after which the next priming query is sent.
    /// </summary>
    private const double ReprimeFraction = 0.9;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    /// <summary>
    ///     This method seeds the hints list synchronously, so the cache holds the root before any
    ///     query is served, then starts the priming loop.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        RootHintsSeeder.Seed(cache, RootHints.Resolve(options.Value));

        return base.StartAsync(cancellationToken);
    }

    /// <summary>
    ///     This method runs the priming loop for the life of the host.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan retryDelay = InitialRetryDelay;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;

            try
            {
                uint? rootNsTimeToLive = await PrimeFromALiveRootServerAsync(stoppingToken);

                if (rootNsTimeToLive is null)
                {
                    logger.LogWarning(
                        "No root server answered the priming query; continuing with the cached root list and retrying in {Delay}.",
                        retryDelay
                    );
                    delay = retryDelay;
                    retryDelay = TimeSpan.FromTicks(
                        Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks)
                    );
                }
                else
                {
                    retryDelay = InitialRetryDelay;

                    // A zero TTL must not turn the loop into a tight one.
                    delay = TimeSpan.FromSeconds(
                        Math.Max(rootNsTimeToLive.Value * ReprimeFraction, 60)
                    );
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to prime the root hints from a live root server; retrying in {Delay}.",
                    retryDelay
                );
                delay = retryDelay;
                retryDelay = TimeSpan.FromTicks(
                    Math.Min(retryDelay.Ticks * 2, MaxRetryDelay.Ticks)
                );
            }

            try
            {
                await Task.Delay(delay, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>
    ///     This method sends the RFC 8109 priming query to the hint addresses in random order
    ///     until one server answers authoritatively with NS records for the root, then stores them
    ///     and every A/AAAA record in the additional section, replacing the seed. The response is
    ///     not DNSSEC-validated. A server that does not answer, or answers with anything else, is
    ///     skipped.
    /// </summary>
    /// <returns>
    ///     The smallest TTL in the stored root NS RRset, or <see langword="null" /> when no server
    ///     gave a usable answer.
    /// </returns>
    private async Task<uint?> PrimeFromALiveRootServerAsync(CancellationToken stoppingToken)
    {
        List<IPAddress> addresses =
        [
            .. RootHints
                .Resolve(options.Value)
                .SelectMany(server => server.Addresses)
                .OrderBy(_ => Random.Shared.Next()),
        ];

        foreach (IPAddress address in addresses)
        {
            DnsMessage response;

            try
            {
                response = await upstreamClient.QueryAsync(
                    address,
                    DnsName.Root,
                    DnsResourceRecordTypeEnum.NS,
                    DnsClassEnum.Internet,
                    options.Value.EnableDnssecValidation,
                    clientSubnetOption: null,
                    stoppingToken: stoppingToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // An unreachable address (for example IPv6 on an IPv4-only host) must not stop
                // the attempt: the next address may work.
                logger.LogDebug(exception, "Priming query to {Address} failed.", address);
                continue;
            }

            List<DnsResourceRecord> nsRecords =
            [
                .. response?.IsAuthoritativeAnswer == true
                    ? response.Answers.Where(record =>
                        record.Type == DnsResourceRecordTypeEnum.NS && record.Name == DnsName.Root
                    )
                    : [],
            ];

            if (nsRecords.Count == 0)
                continue;

            RootHintsSeeder.StoreFromPrimingResponse(cache, nsRecords, response.AdditionalRecords);

            return nsRecords.Min(record => record.TimeToLive);
        }

        return null;
    }
}
