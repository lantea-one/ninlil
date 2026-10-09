using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Caching;

/// <summary>
///     This class holds one cached upstream answer for <see cref="ForwardedResponseCache" />: a
///     private copy of the whole response as the upstream sent it (unshaped, with its DNSSEC
///     records, OPT record and AD bit untouched), whether this server validated it as Secure, and
///     when it was stored. The copy is never handed out; every hit builds a new message from it
///     with <see cref="ForwardedResponseShaper" />, for the client that asked: the client's
///     DO bit decides which DNSSEC records it sees, its EDNS decides whether it gets an OPT
///     record, and AD is set only for a Secure answer to a client that asked for it. The reply
///     is a new message because the query pipeline changes the message it is given (it attaches
///     a DNS cookie and truncates an oversized UDP answer in place), and one client's reply must
///     not leak into the next.
/// </summary>
public sealed class ForwardedResponseCacheEntry : IDnsCacheEntry
{
    private readonly DnsMessage _response;
    private readonly bool _isSecure;

    /// <summary>
    ///     This constructor snapshots <paramref name="response" />. The resource records
    ///     themselves are immutable, so copying the section lists is enough to make the snapshot
    ///     independent of the original message.
    /// </summary>
    /// <param name="response">The upstream answer to cache.</param>
    /// <param name="storedAtUtc">When it was stored.</param>
    /// <param name="expiresAtUtc">When it stops being fresh.</param>
    /// <param name="isSecure">
    ///     Whether this server validated the answer as Secure when it stored it. A fresh hit then
    ///     sets AD for a client that asked for it; a stale hit never does.
    /// </param>
    public ForwardedResponseCacheEntry(
        DnsMessage response,
        DateTimeOffset storedAtUtc,
        DateTimeOffset expiresAtUtc,
        bool isSecure = false
    )
    {
        _response = new DnsMessage
        {
            Id = response.Id,
            IsResponse = true,
            Opcode = response.Opcode,
            IsAuthoritativeAnswer = response.IsAuthoritativeAnswer,
            IsTruncated = response.IsTruncated,
            RecursionDesired = response.RecursionDesired,
            RecursionAvailable = response.RecursionAvailable,
            AuthenticData = response.AuthenticData,
            CheckingDisabled = response.CheckingDisabled,
            ResponseCode = response.ResponseCode,
            Questions = [.. response.Questions],
            Edns = response.Edns,
        };
        _response.Answers.AddRange(response.Answers);
        _response.AuthorityRecords.AddRange(response.AuthorityRecords);
        _response.AdditionalRecords.AddRange(response.AdditionalRecords);
        _isSecure = isSecure;
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
    ///     every TTL reduced by how long the answer has been cached, so no client or downstream
    ///     cache keeps it longer than the upstream allowed.
    /// </summary>
    /// <param name="request">The client's request being answered.</param>
    /// <param name="now">The current time on the cache's clock.</param>
    /// <returns>A new message, safe for the caller to change.</returns>
    public DnsMessage BuildResponse(DnsMessage request, DateTimeOffset now)
    {
        uint elapsedSeconds = (uint)Math.Clamp((now - StoredAtUtc).TotalSeconds, 0, uint.MaxValue);

        return ForwardedResponseShaper.Shape(
            _response,
            request,
            _isSecure,
            record => record.TimeToLive > elapsedSeconds ? record.TimeToLive - elapsedSeconds : 0
        );
    }

    /// <summary>
    ///     This method builds a stale answer (RFC 8767) to <paramref name="request" />, with every
    ///     TTL set to <paramref name="staleTtlSeconds" /> so a client re-asks soon rather than
    ///     holding on to data this server already knows is out of date. AD is never set: a stale
    ///     answer is not re-validated, so it is not authenticated.
    /// </summary>
    /// <param name="request">The client's request being answered.</param>
    /// <param name="staleTtlSeconds">The TTL every record is given.</param>
    /// <returns>A new message, safe for the caller to change.</returns>
    public DnsMessage BuildStaleResponse(DnsMessage request, uint staleTtlSeconds) =>
        ForwardedResponseShaper.Shape(_response, request, isSecure: false, _ => staleTtlSeconds);
}
