using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This class completes a rewrite's CNAME answer for an A or AAAA question with the records
///     of the CNAME target, resolved through the same <see cref="IDnsQueryDispatcher" /> a normal
///     query uses (RFC 1034 §3.6.2, §4.3.2). A stub resolver does not chase a CNAME in a reply
///     whose RA bit is clear, so without this step a Safe Search mapping such as
///     <c>www.google.com</c> to <c>forcesafesearch.google.com</c> leaves the client with no
///     address. <see cref="DnsQueryProcessingPipeline" /> calls it right after the rewrite
///     engine answered.
///     <para>
///         The lookup goes through the dispatcher, not the pipeline, so the rewrite, filter and
///         schedule steps do not run a second time and a rewrite cannot chase itself in a loop:
///         the chase is exactly one hop deep. The resolvers decide as for any other query, on the
///         client's address and RD bit, so a client that may not use forwarding or recursion gets
///         the CNAME alone. The pipeline's response check then sees the merged answer, so the
///         target's addresses and the CNAME target are filtered like any other answer.
///     </para>
/// </summary>
internal static class RewriteTargetChaser
{
    /// <summary>
    ///     This method returns the reply to send for a query a rewrite answered.
    /// </summary>
    /// <param name="context">The client's query context.</param>
    /// <param name="rewriteResponse">The rewrite engine's answer.</param>
    /// <param name="dispatcher">The dispatcher the CNAME target is resolved through.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     A new message holding the rewrite's CNAME followed by the target's answer, when the
    ///     question is A or AAAA, the rewrite answered with one CNAME and the target resolved to
    ///     NOERROR or NXDOMAIN; otherwise <paramref name="rewriteResponse" /> itself, unchanged.
    /// </returns>
    public static async ValueTask<DnsMessage> CompleteAsync(
        DnsQueryContext context,
        DnsMessage rewriteResponse,
        IDnsQueryDispatcher dispatcher,
        CancellationToken stoppingToken
    )
    {
        DnsMessage request = context.Request;

        // A CNAME question keeps the CNAME alone (RFC 1034 §3.6.2). Only a single-CNAME answer is
        // a rewrite this step knows how to extend.
        if (
            request.Questions.Count != 1
            || request.Questions[0].Class != DnsClassEnum.Internet
            || request.Questions[0].Type
                is not (DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa)
            || rewriteResponse.ResponseCode != DnsResponseCodeEnum.NoError
            || rewriteResponse.Answers.Count != 1
            || rewriteResponse.Answers[0].Type != DnsResourceRecordTypeEnum.Cname
        )
            return rewriteResponse;

        DnsName target = (
            DnsRecordDataCodecRegistry.Decode(
                DnsResourceRecordTypeEnum.Cname,
                rewriteResponse.Answers[0].Data
            ) as CnameRecordData
        )?.Target;

        // A rewrite that points a name at itself would only ask the resolvers the same question.
        if (target is null || target == request.Questions[0].Name)
            return rewriteResponse;

        DnsMessage lookup = new()
        {
            Id = request.Id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = request.RecursionDesired,
            CheckingDisabled = request.CheckingDisabled,
            Questions =
            {
                new DnsQuestion(target, request.Questions[0].Type, DnsClassEnum.Internet),
            },
            Edns = request.Edns,
        };

        // The received event, the TSIG state and the wire bytes belong to the client's query, so
        // the lookup carries none of them. IsInternalLookup keeps the query log at one entry.
        DnsQueryContext lookupContext = context with
        {
            Request = lookup,
            RawMessage = null,
            TsigRecordStartOffset = -1,
            TransactionSignature = null,
            IsInternalLookup = true,
        };

        DnsMessage resolved = await dispatcher.DispatchAsync(lookupContext, stoppingToken);

        // REFUSED, SERVFAIL, NOTIMP and the rest: the target cannot be resolved here, so the
        // client gets today's answer (the CNAME alone) and follows it itself.
        if (
            resolved.ResponseCode
            is not (DnsResponseCodeEnum.NoError or DnsResponseCodeEnum.NxDomain)
        )
            return rewriteResponse;

        // The RCODE describes the last name in the chain (RFC 6604 §3), so the lookup's code
        // stands. AA and AD stay clear: this server is no authority for the rewritten name and
        // the rewrite is unsigned. RA is the lookup's, so the flag says whether the client may
        // expect this server to chase on its own.
        DnsMessage merged = new()
        {
            Id = rewriteResponse.Id,
            IsResponse = true,
            Opcode = rewriteResponse.Opcode,
            IsAuthoritativeAnswer = false,
            IsTruncated = false,
            RecursionDesired = rewriteResponse.RecursionDesired,
            RecursionAvailable = resolved.RecursionAvailable,
            AuthenticData = false,
            CheckingDisabled = rewriteResponse.CheckingDisabled,
            ResponseCode = resolved.ResponseCode,
            Questions = [.. rewriteResponse.Questions],
            Answers = [.. rewriteResponse.Answers, .. resolved.Answers],
            AuthorityRecords = [.. resolved.AuthorityRecords],
            AdditionalRecords = [.. resolved.AdditionalRecords],
            Edns = rewriteResponse.Edns,
        };
        return merged;
    }
}
