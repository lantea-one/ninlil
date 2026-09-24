using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Rewrites;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class implements <see cref="IDnsRewriteEngine" />: matches a query's own first
///     question against every currently enabled <see cref="DnsRewriteEntity" /> and, on a match,
///     synthesizes the complete answer.
///     <para>
///         <strong>Matching convention:</strong> a <see cref="DnsRewriteEntity.Domain" /> with no
///         <c>*</c> character matches only that exact name -- deliberately <b>not</b> also every
///         subdomain, a real, documented difference from Phase 3h's own
///         <c>DnsFilterEngine.MatchesDomain</c> convention (a rewrite is meant to override one
///         specific host, not silently redirect its whole subtree). A <c>*</c>-bearing domain is
///         matched as a shell-style glob, translated to an anchored, timeout-guarded regex --
///         the same technique Phase 5h-2's own <c>DnsFilterEngine.BuildWildcardRegexPattern</c>
///         already established, independently re-implemented here (rather than referenced
///         cross-project) to keep this project's own dependency graph exactly as small as its
///         own <c>.csproj</c> already declares. An exact match always wins over a wildcard match;
///         among equally-specific matches, the earliest-created entry wins, a deterministic
///         tie-break.
///     </para>
///     <para>
///         <strong>Record-type convention:</strong> an A-classified target only ever answers an
///         <c>A</c> question; an AAAA-classified target only ever answers an <c>AAAA</c>
///         question; a CNAME-classified target answers <c>A</c>, <c>AAAA</c>, <em>and</em>
///         <c>CNAME</c> questions alike, matching real CNAME semantics (a CNAME record is the
///         answer regardless of the query's own requested type). This engine deliberately does
///         <b>not</b> chase a CNAME-classified target's own further resolution within the same
///         response, unlike <c>AuthoritativeQueryResolver.BuildCnameChaseResponse</c>'s own
///         same-zone chase -- the target is, by definition, outside any zone this engine has
///         direct knowledge of, so the querying client (or, if this server is itself asked
///         recursively, a follow-up query cycle through the normal pipeline) resolves it with an
///         ordinary subsequent query, per standard CNAME-following behavior every real DNS
///         client and resolver already implements. A documented, deliberate scope cut, not a
///         bug. A query of any other type (<c>ANY</c> included) is never intercepted at all,
///         falling through to normal resolution unchanged.
///     </para>
///     <para>
///         <strong>Client-/client-group-scoped rewrites (see <see cref="DnsRewriteEntity.ClientId" />/
///         <see cref="DnsRewriteEntity.ClientGroupId" />):</strong> a rewrite with both properties
///         <see langword="null" /> is unconditionally global, exactly as it always has been. A
///         rewrite naming a specific client or client group only ever matches a query whose own
///         source address resolves (via <see cref="IClientIdentityResolver" />, mirroring
///         <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own identical client-resolution
///         approach, including its "a disabled client resolves to nothing" rule) to that exact
///         client, or to a client belonging to that exact group. This is additive, never
///         subtractive: every unscoped rewrite still applies to every query regardless of whether
///         its client identity resolves at all. When two otherwise-equally-specific (both exact,
///         or both wildcard) rewrites both match the same query, the more specific audience wins --
///         a rewrite scoped to the resolved client itself beats one scoped to its group, which
///         beats an unscoped one -- with the pre-existing "earliest-created entry wins" rule still
///         breaking a tie within the same scope tier (see <see cref="FindBestMatch" />). Both
///         <see cref="IClientIdentityResolver" /> and <see cref="IDnsClientRepository" /> are
///         optional, the identical "cross-cutting infrastructure, singleton-safe, every
///         pre-existing caller/test keeps compiling and behaving unchanged" shape
///         <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />'s own optional
///         dependencies already establish -- absent either one (or a <see langword="null" />
///         <c>remoteAddress</c>, see <see cref="TryBuildResponseAsync" />), only unscoped rewrites
///         are ever considered, byte-for-byte identical to this engine's own behavior before
///         client scoping existed.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach the scoped rewrite repository, once per check.</param>
/// <param name="options">
///     This engine's own configured <see cref="DnsRewriteOptions.Enable" /> gate and answer TTL --
///     read live via <see cref="IOptionsMonitor{TOptions}" /> rather than the older, snapshotted
///     <see cref="IOptions{TOptions}" />, since this engine is now always registered as a DI
///     singleton (<c>ServiceCollectionRewritesExtension</c>) regardless of
///     <see cref="DnsRewriteOptions.Enable" />'s own value at process startup -- a
///     <c>RewriteSettingsController.Patch</c> write toggling it on or off must be observed on this
///     same, already-constructed instance's very next check, with no restart.
/// </param>
/// <param name="logger">The logger this engine records a malformed target/pattern to.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver used to derive a query's own persistent client identifier from its
///     source address, for the client-/client-group-scoped rewrite resolution this class's own
///     doc comment describes. <see langword="null" /> (the default) skips that whole path --
///     every pre-existing caller/test constructing this engine without one keeps compiling and
///     behaving unchanged.
/// </param>
public sealed class DnsRewriteEngine(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsRewriteOptions> options,
    ILogger<DnsRewriteEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsRewriteEngine
{
    /// <summary>
    ///     This constant bounds how long a single wildcard-domain match attempt may run,
    ///     mirroring <c>DnsFilterEngine</c>'s own identical timeout -- a malformed or
    ///     catastrophic-backtracking pattern can never stall query answering (architectural
    ///     invariant #3).
    /// </summary>
    private static readonly TimeSpan WildcardMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     This field caches every wildcard-domain's own compiled <see cref="Regex" /> instance,
    ///     keyed by the domain's own literal, as-stored value, so a query never pays a fresh
    ///     compilation cost. This engine is registered as a DI singleton, so the cache is shared
    ///     across every query this server ever answers, not rebuilt per check.
    /// </summary>
    private readonly ConcurrentDictionary<string, Regex> _compiledWildcardCache = new();

    /// <inheritdoc />
    public async ValueTask<DnsMessage> TryBuildResponseAsync(
        DnsMessage request,
        IPAddress remoteAddress = null,
        CancellationToken stoppingToken = default
    )
    {
        // What/why: the live Enable flag is checked first, before any of the cheap in-memory
        // guards below, so a disabled engine costs exactly one field read per query -- how:
        // IOptionsMonitor.CurrentValue is re-read fresh on every call, never a startup snapshot,
        // so a settings-controller write toggling this flag takes effect on the very next query
        // with no restart.
        if (!options.CurrentValue.Enable)
            return null;

        if (request.Questions.Count == 0)
            return null;

        DnsQuestion question = request.Questions[0];

        // What/why: a rewrite is an Internet-class feature only -- a CHAOS/HESIOD query (used for
        // diagnostic queries like "version.bind") is never intercepted, since this engine has
        // nothing meaningful to answer for either class.
        if (question.Class != DnsClassEnum.Internet)
            return null;

        // What/why: only the three question types a rewrite can ever answer (per this class's
        // own "record-type convention" doc comment above) are worth the repository round trip
        // below -- any other type, ANY included, always falls through to normal resolution
        // unchanged, so this guard runs before the (comparatively expensive) database read.
        if (
            question.Type
            is not (
                DnsResourceRecordTypeEnum.A
                or DnsResourceRecordTypeEnum.Aaaa
                or DnsResourceRecordTypeEnum.Cname
            )
        )
            return null;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsRewriteRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsRewriteRepository>();
        IReadOnlyList<DnsRewriteEntity> rewrites = await repository.ListEnabledAsync(stoppingToken);

        if (rewrites.Count == 0)
            return null;

        string queriedPresentation = NormalizeDomain(question.Name.ToString());

        // Resolved once per query, before matching: a null result (no resolver wired up, no
        // address given, no mapping for this address, or a disabled client) simply means no
        // scoped rewrite can ever match this query -- see AppliesToClient.
        DnsClientEntity resolvedClient = await ResolveClientAsync(
            scope,
            remoteAddress,
            stoppingToken
        );

        DnsRewriteEntity match = FindBestMatch(
            rewrites,
            queriedPresentation,
            question.Type,
            resolvedClient
        );

        if (match is null)
            return null;

        if (
            !DnsRewriteTargetClassifier.TryClassify(
                match.Target,
                out DnsRewriteTargetKindEnum kind,
                out _
            )
        )
        {
            // Defensive only -- DnsRewritesController's own write-time validation already
            // rejects an unclassifiable target before it is ever persisted, so this should only
            // ever trigger for a row some other, non-REST write path inserted without that
            // validation.
            logger.LogWarning(
                "DNS rewrite '{Domain}' ({Id}) has an unclassifiable target; skipping.",
                match.Domain,
                match.Id
            );
            return null;
        }

        return BuildResponse(request, question, match, kind);
    }

    /// <summary>
    ///     This method resolves which single rewrite, if any, answers a query -- an exact
    ///     (non-wildcard) match always wins over a wildcard match; among equally-specific matches,
    ///     the more specific audience wins (a rewrite scoped to <paramref name="resolvedClient" />
    ///     itself beats one scoped to its own client group, which beats an unscoped/global one --
    ///     see <see cref="ScopeSpecificityRank" />); among matches equally specific in <em>both</em>
    ///     respects, the earliest-created entry wins. A rewrite this query's own resolved client
    ///     identity does not satisfy (see <see cref="AppliesToClient" />) is excluded from
    ///     consideration entirely, before either scan below ever runs.
    /// </summary>
    private DnsRewriteEntity FindBestMatch(
        IReadOnlyList<DnsRewriteEntity> rewrites,
        string queriedPresentation,
        DnsResourceRecordTypeEnum questionType,
        DnsClientEntity resolvedClient
    )
    {
        // How: both scans below share this single ordered list -- most-specific-scope first, then
        // earliest-created -- so both tie-break rules described in this method's own doc comment
        // just fall out of "first in this list" for either scan, no separate comparison needed.
        List<DnsRewriteEntity> ordered =
        [
            .. rewrites
                .Where(rewrite => AppliesToClient(rewrite, resolvedClient))
                .OrderByDescending(ScopeSpecificityRank)
                .ThenBy(rewrite => rewrite.CreatedAtUtc),
        ];

        // What/why: the non-wildcard scan always runs first and, if it finds anything, its result
        // is returned immediately without ever consulting the wildcard scan below -- an exact
        // match always wins over a wildcard match, per this class's own doc comment.
        DnsRewriteEntity exact = ordered.FirstOrDefault(rewrite =>
            !ContainsWildcard(rewrite.Domain)
            && MatchesExact(queriedPresentation, rewrite.Domain)
            && AnswersQuestionType(rewrite, questionType)
        );

        // How: the wildcard scan only ever runs as a fallback, via the null-coalescing operator --
        // never wastefully evaluated when an exact match already won.
        return exact
            ?? ordered.FirstOrDefault(rewrite =>
                ContainsWildcard(rewrite.Domain)
                && MatchesWildcard(queriedPresentation, rewrite.Domain)
                && AnswersQuestionType(rewrite, questionType)
            );
    }

    /// <summary>
    ///     This method ranks a rewrite's own scope specificity for <see cref="FindBestMatch" />'s
    ///     own tie-break -- higher is more specific. A rewrite scoped directly to a client
    ///     (<see cref="DnsRewriteEntity.ClientId" />) outranks one scoped to a client group
    ///     (<see cref="DnsRewriteEntity.ClientGroupId" />), which outranks an unscoped/global one.
    ///     A row with both set (never produced by a real write path, see
    ///     <see cref="DnsRewriteEntity.ClientId" />'s own doc comment) is defensively treated as
    ///     client-scoped, the more specific of the two.
    /// </summary>
    private static int ScopeSpecificityRank(DnsRewriteEntity rewrite) =>
        rewrite.ClientId is not null ? 2
        : rewrite.ClientGroupId is not null ? 1
        : 0;

    /// <summary>
    ///     This method checks whether a rewrite applies to a query given its own already-resolved
    ///     client identity -- an unscoped (both <see cref="DnsRewriteEntity.ClientId" /> and
    ///     <see cref="DnsRewriteEntity.ClientGroupId" /> <see langword="null" />) rewrite always
    ///     applies; a scoped one applies only when <paramref name="resolvedClient" /> is not
    ///     <see langword="null" /> and either its own <c>Id</c> matches
    ///     <see cref="DnsRewriteEntity.ClientId" /> or its own
    ///     <see cref="DnsClientEntity.ClientGroupId" /> matches
    ///     <see cref="DnsRewriteEntity.ClientGroupId" />.
    /// </summary>
    private static bool AppliesToClient(DnsRewriteEntity rewrite, DnsClientEntity resolvedClient)
    {
        if (rewrite.ClientId is null && rewrite.ClientGroupId is null)
            return true;

        if (resolvedClient is null)
            return false;

        if (rewrite.ClientId is { } clientId)
            return resolvedClient.Id == clientId;

        return resolvedClient.ClientGroupId == rewrite.ClientGroupId;
    }

    /// <summary>
    ///     This method resolves a query's own source address to its known, non-disabled
    ///     <see cref="DnsClientEntity" /> registry row, mirroring
    ///     <c>Ninlil.Dns.Server.Filtering.DnsFilterEngine</c>'s own identically shaped private
    ///     helper (and <c>Ninlil.Dns.Server.ClientSchedule.DnsClientScheduleEngine</c>'s own second,
    ///     independent copy of it) -- a third, independent implementation of the same handful of
    ///     lines rather than a shared extraction, the same deliberate choice those two classes'
    ///     own doc comments already explain (avoiding a risk of regressing an already-shipped
    ///     engine's own hot path just to remove a small amount of duplication). Returns
    ///     <see langword="null" /> the moment the resolver is unavailable, no address was given,
    ///     the resolver has no mapping for it, the client repository is unavailable, no matching
    ///     row is registered, or the matching row is itself disabled.
    /// </summary>
    private async Task<DnsClientEntity> ResolveClientAsync(
        AsyncServiceScope scope,
        IPAddress remoteAddress,
        CancellationToken stoppingToken
    )
    {
        if (clientIdentityResolver is null || remoteAddress is null)
            return null;

        ClientIdentityResolution resolution = await clientIdentityResolver.ResolveAsync(
            remoteAddress,
            stoppingToken
        );

        if (resolution is null)
            return null;

        IDnsClientRepository clientRepository =
            scope.ServiceProvider.GetService<IDnsClientRepository>();

        if (clientRepository is null)
            return null;

        DnsClientEntity client = await clientRepository.GetByClientIdentifierAsync(
            resolution.ClientIdentifier,
            resolution.ProtocolVersion,
            stoppingToken
        );

        return client is { IsDisabled: false } ? client : null;
    }

    /// <summary>
    ///     This method checks whether a rewrite's own classified target answers a given
    ///     question type -- see this class's own doc comment for the full record-type
    ///     convention.
    /// </summary>
    private static bool AnswersQuestionType(
        DnsRewriteEntity rewrite,
        DnsResourceRecordTypeEnum questionType
    )
    {
        if (
            !DnsRewriteTargetClassifier.TryClassify(
                rewrite.Target,
                out DnsRewriteTargetKindEnum kind,
                out _
            )
        )
            return false;

        return kind switch
        {
            DnsRewriteTargetKindEnum.A => questionType == DnsResourceRecordTypeEnum.A,
            DnsRewriteTargetKindEnum.Aaaa => questionType == DnsResourceRecordTypeEnum.Aaaa,
            DnsRewriteTargetKindEnum.Cname => questionType
                is DnsResourceRecordTypeEnum.A
                    or DnsResourceRecordTypeEnum.Aaaa
                    or DnsResourceRecordTypeEnum.Cname,
            _ => false,
        };
    }

    /// <summary>
    ///     This method builds the complete answer response for a matched rewrite.
    /// </summary>
    private DnsMessage BuildResponse(
        DnsMessage request,
        DnsQuestion question,
        DnsRewriteEntity match,
        DnsRewriteTargetKindEnum kind
    )
    {
        DnsResourceRecordTypeEnum answerType = kind switch
        {
            DnsRewriteTargetKindEnum.A => DnsResourceRecordTypeEnum.A,
            DnsRewriteTargetKindEnum.Aaaa => DnsResourceRecordTypeEnum.Aaaa,
            DnsRewriteTargetKindEnum.Cname => DnsResourceRecordTypeEnum.Cname,
            _ => throw new InvalidOperationException(
                $"Unhandled {nameof(DnsRewriteTargetKindEnum)} value."
            ),
        };

        // How: a CNAME-classified target is wire-encoded through the same shared codec registry
        // every real CNAME record on this server uses (so a synthesized rewrite answer is
        // byte-for-byte indistinguishable from an ordinary authoritative CNAME record); an A/AAAA-
        // classified target is instead just the address's own raw bytes -- both branches trust
        // DnsRewriteTargetClassifier's own prior classification rather than re-validating the
        // target string here.
        byte[] data =
            kind == DnsRewriteTargetKindEnum.Cname
                ? DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Cname,
                    new CnameRecordData(DnsName.Parse(match.Target))
                )
                : IPAddress.Parse(match.Target).GetAddressBytes();

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            // This check runs before any resolver ever sees the query, so there is no way to
            // know here whether recursion is actually enabled elsewhere in this server --
            // mirrors DnsFilterEngine.BuildDeniedResponse's own identical reasoning.
            RecursionAvailable = false,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = [.. request.Questions],
        };

        response.Answers.Add(
            new DnsResourceRecord(
                question.Name,
                answerType,
                DnsClassEnum.Internet,
                options.CurrentValue.TimeToLiveSeconds,
                data
            )
        );

        return response;
    }

    /// <summary>
    ///     This method checks whether a domain pattern contains a wildcard character.
    /// </summary>
    private static bool ContainsWildcard(string domain) => domain?.Contains('*') == true;

    /// <summary>
    ///     This method checks a queried name against a non-wildcard domain, exactly (never also
    ///     matching a subdomain -- see this class's own doc comment for why).
    /// </summary>
    private static bool MatchesExact(string queriedPresentation, string domain) =>
        queriedPresentation == NormalizeDomain(domain);

    /// <summary>
    ///     This method checks a queried name against a wildcard domain, compiling (and caching,
    ///     per <see cref="_compiledWildcardCache" />) its own anchored regex on first use, never
    ///     letting a compile failure or a match timeout escape as an exception -- both are
    ///     treated as "no match," logged once each time they occur.
    /// </summary>
    private bool MatchesWildcard(string queriedPresentation, string domain)
    {
        Regex regex = _compiledWildcardCache.GetOrAdd(
            domain,
            key =>
            {
                Regex compiled = CompileOrNull(BuildWildcardRegexPattern(NormalizeDomain(key)));

                if (compiled is null)
                    logger.LogWarning(
                        "A DNS rewrite has a wildcard domain that failed to compile; treating as a non-match. Domain: {Domain}",
                        key
                    );

                return compiled;
            }
        );

        if (regex is null)
            return false;

        try
        {
            return regex.IsMatch(queriedPresentation);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.LogWarning(
                "A DNS rewrite wildcard domain timed out matching a query name; treating as a non-match. Domain: {Domain}",
                domain
            );

            return false;
        }
    }

    /// <summary>
    ///     This method compiles a wildcard-glob-derived regex pattern with this engine's own
    ///     shared timeout, returning <see langword="null" /> (logged by the caller) instead of
    ///     throwing when the pattern is malformed.
    /// </summary>
    public static Regex CompileOrNull(string pattern)
    {
        try
        {
            return new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                WildcardMatchTimeout
            );
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method translates a shell-style glob (<c>*</c> matches any run of zero or more
    ///     characters) into an anchored, case-insensitive regex pattern -- every other regex
    ///     metacharacter in the glob is escaped literally first, mirroring
    ///     <c>DnsFilterEngine.BuildWildcardRegexPattern</c>'s own identical technique.
    /// </summary>
    public static string BuildWildcardRegexPattern(string glob)
    {
        string escaped = Regex.Escape(glob).Replace(@"\*", ".*");

        return "^" + escaped + "$";
    }

    /// <summary>
    ///     This method normalizes a domain to lowercase with no trailing dot, so
    ///     <see cref="DnsName.ToString" />'s always-fully-qualified form compares correctly
    ///     against an operator-entered value that may or may not have one.
    /// </summary>
    private static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();
}
