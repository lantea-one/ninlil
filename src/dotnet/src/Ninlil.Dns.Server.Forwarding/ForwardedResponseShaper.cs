using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class turns an upstream answer into the reply one client gets.
///     <see cref="ForwarderQueryResolver" /> uses it for a fresh answer and
///     <see cref="Caching.ForwardedResponseCacheEntry" /> for a cached one, so both shapes agree.
///     The upstream message is never changed; the result is a new message.
///     <para>
///         The reply is built from facts about the client, because the forwarder asks the
///         upstream for more than the client did (DO and CD are always set when validation is on,
///         see <see cref="ForwarderQueryResolver" />):
///     </para>
///     <list type="bullet">
///         <item>
///             <b>AA and RA.</b> AA is always clear: this server is not an authority for forwarded
///             data (RFC 1035 §4.1.1). RA is always set: this server offered recursion for the
///             query. The upstream's bits for either are never passed on.
///         </item>
///         <item>
///             <b>AD.</b> It is set only when this server validated the answer as Secure and the
///             client set DO or AD (RFC 6840 §5.8). The upstream's own AD bit is never passed on:
///             this server does not consider an answer authentic unless it checked it itself
///             (RFC 4035 §3.2.3).
///         </item>
///         <item>
///             <b>DNSSEC records.</b> A client without DO gets no RRSIG, NSEC or NSEC3 record,
///             unless it asked for that very type (RFC 4035 §3.2.1).
///         </item>
///         <item>
///             <b>EDNS.</b> A client that sent no OPT record gets none back (RFC 6891 §7). One
///             that did gets a fresh OPT record with this server's payload size, its own DO bit and
///             no options: the upstream's cookie, padding, ECS scope and other options belong to
///             the hop between this server and the upstream. The query pipeline adds the client's
///             server cookie afterwards.
///         </item>
///     </list>
/// </summary>
internal static class ForwardedResponseShaper
{
    /// <summary>
    ///     This method builds the reply to <paramref name="request" /> from
    ///     <paramref name="source" />.
    /// </summary>
    /// <param name="source">The upstream answer, as received.</param>
    /// <param name="request">The client's request being answered.</param>
    /// <param name="isSecure">Whether this server validated the answer as Secure.</param>
    /// <param name="newTtl">Returns a record's new TTL, or <see langword="null" /> to keep it.</param>
    /// <returns>A new message, safe for the caller to change.</returns>
    public static DnsMessage Shape(
        DnsMessage source,
        DnsMessage request,
        bool isSecure,
        Func<DnsResourceRecord, uint?> newTtl
    )
    {
        bool clientWantsDnssec = request.Edns?.DnssecOk == true;
        DnsResourceRecordTypeEnum? queriedType =
            request.Questions.Count > 0 ? request.Questions[0].Type : null;

        DnsMessage reply = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = source.Opcode,
            IsAuthoritativeAnswer = false,
            IsTruncated = source.IsTruncated,
            RecursionDesired = request.RecursionDesired,
            RecursionAvailable = true,
            AuthenticData = isSecure && (clientWantsDnssec || request.AuthenticData),
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = source.ResponseCode,
            Questions = [.. request.Questions],
            Edns = request.Edns is null
                ? null
                : new DnsEdnsOptions(
                    0,
                    DnsQueryDispatcher.DefaultUdpPayloadSizeOctets,
                    clientWantsDnssec,
                    []
                ),
        };

        reply.Answers.AddRange(Shape(source.Answers, clientWantsDnssec, queriedType, newTtl));
        reply.AuthorityRecords.AddRange(
            Shape(source.AuthorityRecords, clientWantsDnssec, queriedType, newTtl)
        );
        reply.AdditionalRecords.AddRange(
            Shape(source.AdditionalRecords, clientWantsDnssec, queriedType, newTtl)
        );

        return reply;
    }

    private static IEnumerable<DnsResourceRecord> Shape(
        IEnumerable<DnsResourceRecord> records,
        bool clientWantsDnssec,
        DnsResourceRecordTypeEnum? queriedType,
        Func<DnsResourceRecord, uint?> newTtl
    ) =>
        records
            .Where(record =>
                record.Type != DnsResourceRecordTypeEnum.Opt
                && (clientWantsDnssec || !IsDnssecRecord(record) || record.Type == queriedType)
            )
            .Select(record => WithTtl(record, newTtl(record)));

    private static bool IsDnssecRecord(DnsResourceRecord record) =>
        record.Type
            is DnsResourceRecordTypeEnum.RrSig
                or DnsResourceRecordTypeEnum.Nsec
                or DnsResourceRecordTypeEnum.Nsec3;

    private static DnsResourceRecord WithTtl(DnsResourceRecord record, uint? ttl) =>
        ttl is { } value && value != record.TimeToLive
            ? new DnsResourceRecord(record.Name, record.Type, record.Class, value, record.Data)
            : record;
}
