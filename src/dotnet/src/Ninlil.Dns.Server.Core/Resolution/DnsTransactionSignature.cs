using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Resolution;

/// <summary>
///     This record is the outcome of checking the transaction signature (TSIG, RFC 8945) of one
///     request. <see cref="IDnsTransactionSignatureService.VerifyAsync" /> produces it, the pipeline
///     stores it on <see cref="DnsQueryContext.TransactionSignature" /> for the resolvers, and
///     <see cref="IDnsTransactionSignatureService.SignAsync" /> uses it to sign the reply.
/// </summary>
/// <param name="KeyName">The name of the key the request's TSIG record names.</param>
/// <param name="IsVerified">
///     Whether the signature is valid: the key exists, the MAC matches and the time is within the
///     fudge.
/// </param>
/// <param name="Error">
///     Why verification failed (BADKEY, BADSIG, BADTIME and so on), or
///     <see cref="DnsResponseCodeEnum.NoError" /> when <paramref name="IsVerified" /> is
///     <see langword="true" />.
/// </param>
/// <param name="RequestTsig">
///     The request's TSIG record. Its MAC is the one the reply is signed over (RFC 8945 §5.3).
/// </param>
public record DnsTransactionSignature(
    string KeyName,
    bool IsVerified,
    DnsResponseCodeEnum Error,
    DnsTsigOptions RequestTsig
);
