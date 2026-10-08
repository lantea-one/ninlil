using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class adapts <see cref="ITsigService" /> (RFC 8945) to the pipeline's
///     <see cref="IDnsTransactionSignatureService" />. It is what lets
///     <c>DnsQueryProcessingPipeline</c> verify signed QUERY, NOTIFY and UPDATE requests and sign
///     their replies without Server.Core depending on the key store.
///     <para>
///         It is a singleton, but <see cref="ITsigService" /> is scoped (it reads the key
///         repository), so every call opens its own scope.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to open a scope for <see cref="ITsigService" />.</param>
/// <param name="keyCache">The shared key lookup cache, used to find the key of a BADTIME reply.</param>
public class TsigTransactionSignatureService(
    IServiceScopeFactory scopeFactory,
    TsigKeyLookupCache keyCache
) : IDnsTransactionSignatureService
{
    /// <summary>
    ///     This constant contains the octets of a TSIG record outside its names and its MAC and
    ///     Other Data: the owner's type, class, TTL and RDLENGTH (10), Time Signed (6), Fudge (2),
    ///     MAC Size (2), Original ID (2), Error (2) and Other Len (2).
    /// </summary>
    private const int FixedRecordOctets = 26;

    /// <inheritdoc />
    public async ValueTask<DnsTransactionSignature> VerifyAsync(
        DnsQueryContext context,
        CancellationToken stoppingToken = default
    )
    {
        DnsTsigOptions tsig = context.Request.Tsig;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        TsigVerificationResult result = await tsigService.VerifyAsync(
            context.RawMessage,
            context.TsigRecordStartOffset,
            context.Request,
            stoppingToken
        );

        return new DnsTransactionSignature(
            tsig.KeyName.ToString(),
            result.IsValid,
            result.Error,
            tsig
        );
    }

    /// <inheritdoc />
    public async ValueTask<DnsMessage> SignAsync(
        DnsMessage response,
        DnsTransactionSignature signature,
        CancellationToken stoppingToken = default
    )
    {
        // The reply may be a message the caches share between clients, so it is never changed.
        DnsMessage copy = Copy(response);

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();

        if (signature.IsVerified)
            return await tsigService.SignResponseAsync(
                copy,
                signature.KeyName,
                copy.Id,
                signature.RequestTsig.Mac,
                stoppingToken
            );

        // Only a BADTIME reply is signed, and it needs the key the sender proved it holds.
        TsigKeyEntity key =
            signature.Error == DnsResponseCodeEnum.BadTime
                ? await keyCache.FindAsync(
                    signature.KeyName,
                    scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>(),
                    stoppingToken
                )
                : null;

        return await tsigService.SignErrorResponseAsync(
            copy,
            signature.RequestTsig,
            TsigVerificationResult.Failure(signature.Error, key),
            stoppingToken
        );
    }

    /// <inheritdoc />
    public int GetSignatureLength(DnsTransactionSignature signature)
    {
        // FORMERR replies carry no TSIG record.
        if (!signature.IsVerified && signature.Error == DnsResponseCodeEnum.FormatError)
            return 0;

        DnsTsigOptions tsig = signature.RequestTsig;
        int macOctets = 0;
        int otherOctets = 0;

        // A verified reply and a BADTIME reply are signed (full MAC). BADTIME also carries the
        // server time (6 octets). Other errors have an empty MAC.
        if (signature.IsVerified || signature.Error == DnsResponseCodeEnum.BadTime)
        {
            macOctets = TsigAlgorithm.Find(tsig.AlgorithmName)?.DigestLength ?? 0;
            otherOctets = signature.IsVerified ? 0 : 6;
        }

        return FixedRecordOctets
            + tsig.KeyName.WireLength
            + tsig.AlgorithmName.WireLength
            + macOctets
            + otherOctets;
    }

    /// <summary>
    ///     This method copies a message's header and sections, without any TSIG record. The
    ///     records themselves are shared, which is safe because nothing here changes them.
    /// </summary>
    private static DnsMessage Copy(DnsMessage original) =>
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
}
