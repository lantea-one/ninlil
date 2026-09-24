using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Caching;

/// <summary>
///     This class holds one cached upstream answer: a private copy of the whole response exactly
///     as the forwarder sent it, and when it was stored. The copy is never handed out itself;
///     every hit builds a fresh message from it, because the query pipeline changes the message
///     it is given (it attaches this server's DNS cookie, and truncates an oversized UDP answer
///     in place), and one client's reply must never leak into the next client's.
/// </summary>
public sealed class ForwardedResponseCacheEntry : IDnsCacheEntry
{
    private readonly DnsMessage _response;

    /// <summary>
    ///     This constructor snapshots <paramref name="response" />. The resource records
    ///     themselves are immutable, so copying the section lists is enough to make the snapshot
    ///     independent of the original message.
    /// </summary>
    /// <param name="response">The upstream answer to cache.</param>
    /// <param name="storedAtUtc">When it was stored.</param>
    /// <param name="expiresAtUtc">When it stops being fresh.</param>
    public ForwardedResponseCacheEntry(
        DnsMessage response,
        DateTimeOffset storedAtUtc,
        DateTimeOffset expiresAtUtc
    )
    {
        _response = Copy(response, response, _ => null);
        StoredAtUtc = storedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>
    ///     This property contains when this answer was received from upstream.
    /// </summary>
    public DateTimeOffset StoredAtUtc { get; }

    /// <inheritdoc />
    public DateTimeOffset ExpiresAtUtc { get; }

    /// <summary>
    ///     This property contains the cached answer's response code, for tests and diagnostics.
    /// </summary>
    public Common.Enum.DnsResponseCodeEnum ResponseCode => _response.ResponseCode;

    /// <summary>
    ///     This method builds the answer to <paramref name="request" /> from this entry, with
    ///     every TTL reduced by how long the answer has already sat in the cache -- a client (or
    ///     a downstream cache) must never be told it may keep an answer longer than the upstream
    ///     allowed from the moment it was first sent.
    /// </summary>
    /// <param name="request">The client's request being answered.</param>
    /// <param name="now">The current time on the cache's clock.</param>
    /// <returns>A new message, safe for the caller to change.</returns>
    public DnsMessage BuildResponse(DnsMessage request, DateTimeOffset now)
    {
        uint elapsedSeconds = (uint)Math.Clamp((now - StoredAtUtc).TotalSeconds, 0, uint.MaxValue);

        return Copy(
            _response,
            request,
            record => record.TimeToLive > elapsedSeconds ? record.TimeToLive - elapsedSeconds : 0
        );
    }

    /// <summary>
    ///     This method builds a stale answer (RFC 8767) to <paramref name="request" />, with every
    ///     TTL set to <paramref name="staleTtlSeconds" /> so a client re-asks soon rather than
    ///     holding on to data this server already knows is out of date.
    /// </summary>
    /// <param name="request">The client's request being answered.</param>
    /// <param name="staleTtlSeconds">The TTL every record is given.</param>
    /// <returns>A new message, safe for the caller to change.</returns>
    public DnsMessage BuildStaleResponse(DnsMessage request, uint staleTtlSeconds) =>
        Copy(_response, request, _ => staleTtlSeconds);

    /// <summary>
    ///     This method copies <paramref name="source" /> into a new message addressed to
    ///     <paramref name="request" /> (its ID and its question, so a client using 0x20 case
    ///     randomization gets its own spelling back), optionally rewriting every record's TTL.
    /// </summary>
    /// <param name="source">The message to copy.</param>
    /// <param name="request">The request the copy answers.</param>
    /// <param name="newTtl">Returns a record's new TTL, or <see langword="null" /> to keep it.</param>
    private static DnsMessage Copy(
        DnsMessage source,
        DnsMessage request,
        Func<DnsResourceRecord, uint?> newTtl
    )
    {
        DnsMessage copy = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = source.Opcode,
            IsAuthoritativeAnswer = source.IsAuthoritativeAnswer,
            RecursionDesired = request.RecursionDesired,
            RecursionAvailable = source.RecursionAvailable,
            AuthenticData = source.AuthenticData,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = source.ResponseCode,
            Questions = [.. request.Questions],
            Edns = source.Edns,
        };

        copy.Answers.AddRange(source.Answers.Select(record => WithTtl(record, newTtl(record))));
        copy.AuthorityRecords.AddRange(
            source.AuthorityRecords.Select(record => WithTtl(record, newTtl(record)))
        );
        copy.AdditionalRecords.AddRange(
            source.AdditionalRecords.Select(record => WithTtl(record, newTtl(record)))
        );

        return copy;
    }

    private static DnsResourceRecord WithTtl(DnsResourceRecord record, uint? ttl) =>
        ttl is { } value && value != record.TimeToLive
            ? new DnsResourceRecord(record.Name, record.Type, record.Class, value, record.Data)
            : record;
}
