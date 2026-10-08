using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Transfer;

/// <summary>
///     This class runs one incoming AXFR or IXFR request for the TCP, DNS-over-TLS and
///     DNS-over-QUIC listeners, which answer transfers themselves because a transfer is a
///     sequence of messages the query pipeline cannot produce. It gives transfers the two
///     pipeline behaviours that matter at the edge: the per-address and per-subnet rate limits
///     run before the transfer service is touched, and one <see cref="DnsQueryAnsweredEvent" />
///     is published when the transfer ends, so it reaches the query log like any other answer.
///     The transfer ACL and TSIG checks stay in <see cref="IZoneTransferService" />.
///     <para>
///         The listener supplies how a message is written (framing and connection handling
///         differ per transport) and the token that bounds the transfer. That token must not be
///         the read-idle token, because a transfer is long by nature.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IZoneTransferService" /> per transfer.</param>
/// <param name="rateLimiter">
///     The rate limiter shared with the query pipeline. <see langword="null" /> skips rate
///     limiting.
/// </param>
/// <param name="eventPublisher">
///     The publisher for <see cref="DnsQueryAnsweredEvent" />. <see langword="null" /> publishes
///     nothing.
/// </param>
public sealed class ZoneTransferRequestRunner(
    IServiceScopeFactory scopeFactory,
    IDnsRateLimiter rateLimiter = null,
    IDnsServerEventPublisher eventPublisher = null
)
{
    /// <summary>
    ///     This method rate-limits, then serves one transfer, writing each response message in
    ///     order through <paramref name="writeMessageAsync" />, and publishes one answered event
    ///     once every message is written. A transfer that fails part-way (the client stopped
    ///     reading, or the token was cancelled) throws and publishes nothing.
    /// </summary>
    /// <param name="request">The decoded AXFR or IXFR query.</param>
    /// <param name="rawMessage">The query as received, which TSIG verification needs.</param>
    /// <param name="tsigRecordStartOffset">Where the TSIG record starts in <paramref name="rawMessage" />.</param>
    /// <param name="remoteAddress">The client's normalized source address.</param>
    /// <param name="protocol">The transport the query arrived over.</param>
    /// <param name="questionType"><see cref="DnsResourceRecordTypeEnum.Axfr" /> or <see cref="DnsResourceRecordTypeEnum.Ixfr" />.</param>
    /// <param name="writeMessageAsync">Writes one encoded response message to the client.</param>
    /// <param name="transferToken">The token bounding the whole transfer.</param>
    /// <returns>
    ///     <see langword="false" /> if the rate limit denied the request, in which case nothing
    ///     was written, the transfer service was not called and the caller should close the
    ///     connection or stream without a reply (as the pipeline does for a throttled query);
    ///     otherwise <see langword="true" />.
    /// </returns>
    public async Task<bool> RunAsync(
        DnsMessage request,
        byte[] rawMessage,
        int tsigRecordStartOffset,
        IPAddress remoteAddress,
        DnsTransportProtocolEnum protocol,
        DnsResourceRecordTypeEnum questionType,
        Func<byte[], CancellationToken, ValueTask> writeMessageAsync,
        CancellationToken transferToken
    )
    {
        if (rateLimiter is not null)
        {
            // Stream transports are not datagrams, so the subnet check can only drop, never
            // answer truncated; any outcome other than Allow denies the transfer.
            if (
                !await rateLimiter.TryAcquireAsync(remoteAddress, transferToken)
                || await rateLimiter.CheckSubnetLimitAsync(
                    remoteAddress,
                    isUdp: false,
                    transferToken
                ) != DnsSubnetRateLimitDecisionEnum.Allow
            )
                return false;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneTransferService transferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsQueryContext context = new(
            request,
            protocol,
            remoteAddress,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigRecordStartOffset
        );

        IAsyncEnumerable<DnsMessage> responses =
            questionType == DnsResourceRecordTypeEnum.Axfr
                ? transferService.CreateAxfrResponseAsync(context, transferToken)
                : transferService.CreateIxfrResponseAsync(context, transferToken);

        DnsResponseCodeEnum? firstResponseCode = null;
        int answerCount = 0;

        await foreach (DnsMessage responseMessage in responses.WithCancellation(transferToken))
        {
            // A refused transfer is a single message, so the first message's code is the
            // outcome; later messages of an accepted transfer all carry NoError.
            firstResponseCode ??= responseMessage.ResponseCode;
            answerCount += responseMessage.Answers.Count;

            await writeMessageAsync(DnsMessageWriter.Write(responseMessage), transferToken);
        }

        if (firstResponseCode is not null)
            eventPublisher?.Publish(
                new DnsQueryAnsweredEvent(
                    DateTimeOffset.UtcNow,
                    protocol,
                    remoteAddress,
                    request.Questions.Count > 0 ? request.Questions[0].Name.ToString() : null,
                    questionType,
                    firstResponseCode.Value,
                    answerCount
                )
            );

        return true;
    }
}
