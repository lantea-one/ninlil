using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This interface is how <see cref="Pipeline.DnsQueryProcessingPipeline" /> applies TSIG
///     (RFC 8945) to QUERY, NOTIFY and UPDATE messages without Server.Core knowing about keys. The
///     pipeline verifies a signed request right after decoding it, hands the outcome to the
///     resolvers through <see cref="DnsQueryContext.TransactionSignature" />, and signs the reply
///     as the very last step, because any later change to the reply (cookie option, UDP
///     truncation) would break the MAC. Server.Tsig implements it; without an implementation the
///     pipeline ignores TSIG.
/// </summary>
public interface IDnsTransactionSignatureService
{
    /// <summary>
    ///     This method verifies the TSIG record of <paramref name="context" />'s request.
    /// </summary>
    /// <param name="context">A context whose request carries a TSIG record.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The outcome, valid or not.</returns>
    ValueTask<DnsTransactionSignature> VerifyAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method signs a reply to a request that was checked by <see cref="VerifyAsync" />.
    ///     For a failed signature it turns the reply into the error reply RFC 8945 §5.2 and §5.3.2 require
    ///     (NOTAUTH with the TSIG error in the TSIG record, or FORMERR).
    /// </summary>
    /// <param name="response">
    ///     The finished reply. It is not changed, because it may be a cached message shared with
    ///     other clients.
    /// </param>
    /// <param name="signature">The outcome of verifying the request this reply answers.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A copy of <paramref name="response" /> with its TSIG record set.</returns>
    ValueTask<DnsMessage> SignAsync(
        DnsMessage response,
        DnsTransactionSignature signature,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method returns how many octets the TSIG record that <see cref="SignAsync" /> adds
    ///     to a reply takes, so the pipeline can leave room for it when it truncates a UDP reply.
    /// </summary>
    /// <param name="signature">The outcome of verifying the request.</param>
    int GetSignatureLength(DnsTransactionSignature signature);
}
