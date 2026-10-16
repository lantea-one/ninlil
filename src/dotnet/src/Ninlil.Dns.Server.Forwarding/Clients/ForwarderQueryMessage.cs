using System.Security.Cryptography;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Clients;

/// <summary>
///     This class builds the copy of a query that a forwarder client sends and checks that a
///     reply answers it. Every <see cref="Ninlil.Dns.Server.Core.Forwarding.IUpstreamForwarderClient" />
///     in this project uses it, so the ID rules live in one place: the caller's message is never
///     changed (the same message can be sent by more than one client, and the caller restores its
///     own ID on the reply), and a reply is accepted only when it repeats the question that was
///     sent (RFC 5452 §9.1 lists the question section and the message ID among the attributes a
///     resolver must match).
/// </summary>
internal static class ForwarderQueryMessage
{
    /// <summary>
    ///     This method returns a message ID drawn from the operating system's cryptographic random
    ///     source. An unpredictable ID is part of the defence against forged replies (RFC 5452
    ///     §9.2), so <see cref="Random" /> is not used.
    /// </summary>
    public static ushort NewRandomId() =>
        (ushort)RandomNumberGenerator.GetInt32(ushort.MaxValue + 1);

    /// <summary>
    ///     This method returns a copy of <paramref name="query" /> that carries
    ///     <paramref name="id" />. The input is never changed. The record lists are copied; the
    ///     records, question and option objects are shared, because nothing mutates them.
    /// </summary>
    /// <param name="query">The message to copy.</param>
    /// <param name="id">The message ID the copy carries.</param>
    public static DnsMessage WithId(DnsMessage query, ushort id) =>
        new()
        {
            Id = id,
            IsResponse = query.IsResponse,
            Opcode = query.Opcode,
            IsAuthoritativeAnswer = query.IsAuthoritativeAnswer,
            IsTruncated = query.IsTruncated,
            RecursionDesired = query.RecursionDesired,
            RecursionAvailable = query.RecursionAvailable,
            AuthenticData = query.AuthenticData,
            CheckingDisabled = query.CheckingDisabled,
            ResponseCode = query.ResponseCode,
            Questions = [.. query.Questions],
            Answers = [.. query.Answers],
            AuthorityRecords = [.. query.AuthorityRecords],
            AdditionalRecords = [.. query.AdditionalRecords],
            Edns = query.Edns,
            Tsig = query.Tsig,
        };

    /// <summary>
    ///     This method returns <see langword="true" /> when the question section of
    ///     <paramref name="reply" /> is the one in <paramref name="query" />: the same number of
    ///     questions, and for each the same name (compared case-insensitively, RFC 1035 §3.1),
    ///     type and class. It does not look at the message ID, because DNS-over-HTTPS sends ID 0
    ///     and the ID rule differs per transport; the callers compare it themselves.
    /// </summary>
    /// <param name="query">The message that was sent.</param>
    /// <param name="reply">The message that came back.</param>
    public static bool IsReplyTo(DnsMessage query, DnsMessage reply)
    {
        if (query.Questions.Count != reply.Questions.Count)
            return false;

        for (int i = 0; i < query.Questions.Count; i++)
            // DnsName.Equals ignores ASCII case; DnsQuestion is a record, so == also compares
            // the type and the class.
            if (query.Questions[i] != reply.Questions[i])
                return false;

        return true;
    }
}
