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
///     This class implements <see cref="IDnsRewriteEngine" />: it matches a query's first
///     question against every enabled <see cref="DnsRewriteEntity" /> and, on a match, builds the
///     answer. <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> calls it
///     before any resolver (see that class for where rewrites sit in the query path). The
///     rewrite list is read from the repository on every query that passes the cheap checks.
///     <para>
///         <strong>Matching.</strong> A <see cref="DnsRewriteEntity.Domain" /> without <c>*</c>
///         matches only that exact name, not its subdomains (unlike the domain filter), because a
///         rewrite overrides one host. A domain with <c>*</c> is a shell-style glob, turned into
///         an anchored regex with a match timeout. This is the same technique as
///         <c>DnsFilterEngine.BuildWildcardRegexPattern</c>, repeated here so this project need
///         not reference the filtering project. An exact match beats a wildcard match; ties go
///         to the narrower audience (see below), then to the earliest-created entry.
///     </para>
///     <para>
///         <strong>Record types.</strong> An A target answers only <c>A</c> questions and an
///         AAAA target only <c>AAAA</c>. A CNAME target answers <c>A</c>, <c>AAAA</c> and
///         <c>CNAME</c> questions with the CNAME record alone. The target is not resolved into the
///         same response (unlike the authoritative resolver's in-zone CNAME chase); the client
///         follows it with its own query. Any other type, <c>ANY</c> included, is not intercepted.
///     </para>
///     <para>
///         <strong>Client scoping.</strong> A rewrite with neither
///         <see cref="DnsRewriteEntity.ClientId" /> nor <see cref="DnsRewriteEntity.ClientGroupId" />
///         is global. A scoped rewrite matches only when the query's source address resolves,
///         through <see cref="IClientIdentityResolver" />, to that client or to a client in that
///         group; a disabled client resolves to nothing, as in <c>DnsFilterEngine</c>. Global
///         rewrites apply to every query either way. Between two equally specific matches (both
///         exact or both wildcard), a client-scoped rewrite beats a group-scoped one, which beats
///         a global one (see <see cref="FindBestMatch" />). The resolver and
///         <see cref="IDnsClientRepository" /> are optional; without either, or without a
///         <c>remoteAddress</c>, only global rewrites are considered.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach the scoped rewrite repository, once per check.</param>
/// <param name="options">
///     The <see cref="DnsRewriteOptions.Enable" /> switch and answer TTL, read live through
///     <see cref="IOptionsMonitor{TOptions}" /> rather than <see cref="IOptions{TOptions}" />, so
///     that a <c>RewriteSettingsController.Patch</c> takes effect on the next query without a
///     restart.
/// </param>
/// <param name="logger">The logger this engine records a malformed target/pattern to.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver that maps a query's source address to a client identifier, for
///     client scoping (see the class summary). <see langword="null" /> (the default) means only
///     global rewrites apply.
/// </param>
public sealed class DnsRewriteEngine(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsRewriteOptions> options,
    ILogger<DnsRewriteEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsRewriteEngine
{
    /// <summary>
    ///     This field bounds how long one wildcard match may run (the same timeout as
    ///     <c>DnsFilterEngine</c>), so a pathological pattern cannot stall query answering.
    /// </summary>
    private static readonly TimeSpan WildcardMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     This field caches each wildcard domain's compiled <see cref="Regex" />, keyed by the
    ///     stored domain string, so a pattern is compiled once. The engine is a singleton, so the
    ///     cache is shared by every query. Entries are never removed.
    /// </summary>
    private readonly ConcurrentDictionary<string, Regex> _compiledWildcardCache = new();

    /// <inheritdoc />
    public async ValueTask<DnsMessage> TryBuildResponseAsync(
        DnsMessage request,
        IPAddress remoteAddress = null,
        CancellationToken stoppingToken = default
    )
    {
        // Checked first so a disabled engine costs one field read per query. CurrentValue is
        // read on every call, so a settings change applies to the next query.
        if (!options.CurrentValue.Enable)
            return null;

        if (request.Questions.Count == 0)
            return null;

        DnsQuestion question = request.Questions[0];

        // Rewrites are IN-class only; CHAOS and HESIOD queries (such as "version.bind") pass
        // through.
        if (question.Class != DnsClassEnum.Internet)
            return null;

        // Only A, AAAA and CNAME can be answered by a rewrite (see the class summary), so other
        // types skip the repository read below.
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

        // Resolved once per query. A null client means no scoped rewrite can match (see
        // AppliesToClient).
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
            // The REST controllers reject unclassifiable targets on write, so this only guards
            // against rows written some other way.
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
    ///     This method picks the rewrite, if any, that answers a query. An exact match beats a
    ///     wildcard match. Among equally specific matches, the narrower audience wins (see
    ///     <see cref="ScopeSpecificityRank" />), then the earliest-created entry. Rewrites that do
    ///     not apply to <paramref name="resolvedClient" /> (see <see cref="AppliesToClient" />) are
    ///     dropped first.
    /// </summary>
    private DnsRewriteEntity FindBestMatch(
        IReadOnlyList<DnsRewriteEntity> rewrites,
        string queriedPresentation,
        DnsResourceRecordTypeEnum questionType,
        DnsClientEntity resolvedClient
    )
    {
        // Sorted by scope, then creation time, so the first hit of either scan below is the
        // tie-break winner.
        List<DnsRewriteEntity> ordered =
        [
            .. rewrites
                .Where(rewrite => AppliesToClient(rewrite, resolvedClient))
                .OrderByDescending(ScopeSpecificityRank)
                .ThenBy(rewrite => rewrite.CreatedAtUtc),
        ];

        DnsRewriteEntity exact = ordered.FirstOrDefault(rewrite =>
            !ContainsWildcard(rewrite.Domain)
            && MatchesExact(queriedPresentation, rewrite.Domain)
            && AnswersQuestionType(rewrite, questionType)
        );

        return exact
            ?? ordered.FirstOrDefault(rewrite =>
                ContainsWildcard(rewrite.Domain)
                && MatchesWildcard(queriedPresentation, rewrite.Domain)
                && AnswersQuestionType(rewrite, questionType)
            );
    }

    /// <summary>
    ///     This method ranks a rewrite's audience for <see cref="FindBestMatch" />; higher is
    ///     narrower. Client-scoped (<see cref="DnsRewriteEntity.ClientId" />) outranks
    ///     group-scoped (<see cref="DnsRewriteEntity.ClientGroupId" />), which outranks global. A
    ///     row with both set (which the write paths never produce) counts as client-scoped.
    /// </summary>
    private static int ScopeSpecificityRank(DnsRewriteEntity rewrite) =>
        rewrite.ClientId is not null ? 2
        : rewrite.ClientGroupId is not null ? 1
        : 0;

    /// <summary>
    ///     This method checks whether a rewrite applies to the query's resolved client. A global
    ///     rewrite always applies. A scoped one applies only when <paramref name="resolvedClient" />
    ///     is not <see langword="null" /> and its <c>Id</c> matches
    ///     <see cref="DnsRewriteEntity.ClientId" />, or (when no client is named) its
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
    ///     This method resolves a query's source address to its registered, enabled
    ///     <see cref="DnsClientEntity" />. <c>DnsFilterEngine</c> and <c>DnsClientScheduleEngine</c>
    ///     have their own copies of this helper. Returns <see langword="null" /> when the resolver
    ///     or client repository is missing, no address was given, the address has no mapping, no
    ///     client is registered for it, or that client is disabled.
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
    ///     This method checks whether a rewrite's target kind answers a question type (see the
    ///     class summary).
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

        // A CNAME target goes through the shared codec registry, like any stored CNAME record;
        // an A/AAAA target is the address bytes. Both rely on the classification above.
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
            // Rewrites run before any resolver, so this code cannot know whether recursion is
            // available (the same reasoning as DnsFilterEngine.BuildDeniedResponse).
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
    ///     This method checks a queried name against a non-wildcard domain. Subdomains do not match
    ///     (see the class summary).
    /// </summary>
    private static bool MatchesExact(string queriedPresentation, string domain) =>
        queriedPresentation == NormalizeDomain(domain);

    /// <summary>
    ///     This method checks a queried name against a wildcard domain, compiling and caching its
    ///     regex on first use (see <see cref="_compiledWildcardCache" />). A compile failure or a
    ///     match timeout counts as no match and is logged; neither throws.
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
    ///     This method compiles a regex pattern with the wildcard match timeout, case-insensitively,
    ///     and returns <see langword="null" /> instead of throwing when the pattern is malformed.
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
    ///     This method turns a shell-style glob into an anchored regex pattern. <c>*</c> matches
    ///     any run of characters, dots included, and everything else is escaped. Case is handled by
    ///     <see cref="CompileOrNull" />.
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
