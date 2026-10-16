using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Rewrites;

// Define our namespace.
namespace Ninlil.Dns.Server.Rewrites;

/// <summary>
///     This class implements <see cref="IDnsRewriteEngine" />: it matches a query's first
///     question against every enabled <see cref="DnsRewriteEntity" />
///     and, on a match, builds the answer.
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> calls it before
///     any resolver (see that class for where rewrites sit in the query path). The rewrites come
///     from <see cref="DnsRewriteCache">an in-memory copy</see> that a saved change refreshes at
///     once, and the client from <see cref="DnsClientDirectory" />, so a query that passes the
///     cheap checks reads no database rows while the copies are fresh and opens no scope.
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
///         <c>CNAME</c> questions with the CNAME record alone. The engine does not resolve the target;
///         for an <c>A</c> or <c>AAAA</c> question the pipeline completes the answer with the
///         target's records (<c>RewriteTargetChaser</c>), and a <c>CNAME</c> question keeps the
///         CNAME alone. Any other type, <c>ANY</c> included, is not intercepted.
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
///         <see cref="DnsClientDirectory" /> are optional; without either, or without a
///         <c>remoteAddress</c>, only global rewrites are considered.
///     </para>
/// </summary>
/// <param name="cache">The in-memory copy of the enabled rewrites, already sorted and compiled.</param>
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
/// <param name="clientDirectory">
///     The optional in-memory client lookup that turns the resolved identifier into a registered
///     client. <see langword="null" /> (the default) means only global rewrites apply.
/// </param>
public sealed class DnsRewriteEngine(
    DnsRewriteCache cache,
    IOptionsMonitor<DnsRewriteOptions> options,
    ILogger<DnsRewriteEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null,
    DnsClientDirectory clientDirectory = null
) : IDnsRewriteEngine
{
    /// <summary>
    ///     This field bounds how long one wildcard match may run (the same timeout as
    ///     <c>DnsFilterEngine</c>), so a pathological pattern cannot stall query answering.
    /// </summary>
    private static readonly TimeSpan WildcardMatchTimeout = TimeSpan.FromMilliseconds(250);

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
        // types skip the matching below.
        if (
            question.Type
            is not (
                DnsResourceRecordTypeEnum.A
                or DnsResourceRecordTypeEnum.Aaaa
                or DnsResourceRecordTypeEnum.Cname
            )
        )
            return null;

        DnsRewriteSet rewrites = await cache.GetAsync(stoppingToken);

        if (rewrites.IsEmpty)
            return null;

        string queriedPresentation = NormalizeDomain(question.Name.ToString());

        // Resolved once per query. A null client means no scoped rewrite can match (see
        // AppliesToClient).
        DnsClientInfo resolvedClient = await ResolveClientAsync(remoteAddress, stoppingToken);

        DnsCompiledRewrite match = FindBestMatch(
            rewrites,
            queriedPresentation,
            question.Type,
            resolvedClient
        );

        if (match is null)
            return null;

        return BuildResponse(request, question, match);
    }

    /// <summary>
    ///     This method picks the rewrite, if any, that answers a query. An exact match beats a
    ///     wildcard match. The set is already in tie-break order (narrower audience, then the
    ///     earliest-created entry), so the first hit of each scan wins. Rewrites that do not apply
    ///     to <paramref name="resolvedClient" /> (see <see cref="AppliesToClient" />) are skipped.
    /// </summary>
    private DnsCompiledRewrite FindBestMatch(
        DnsRewriteSet rewrites,
        string queriedPresentation,
        DnsResourceRecordTypeEnum questionType,
        DnsClientInfo resolvedClient
    )
    {
        if (rewrites.Exact.TryGetValue(queriedPresentation, out var candidates))
        {
            foreach (DnsCompiledRewrite rewrite in candidates)
            {
                if (
                    AppliesToClient(rewrite, resolvedClient)
                    && AnswersQuestionType(rewrite.Kind, questionType)
                )
                    return rewrite;
            }
        }

        foreach (DnsCompiledRewrite rewrite in rewrites.Wildcards)
        {
            if (
                AppliesToClient(rewrite, resolvedClient)
                && AnswersQuestionType(rewrite.Kind, questionType)
                && MatchesWildcard(queriedPresentation, rewrite)
            )
                return rewrite;
        }

        return null;
    }

    /// <summary>
    ///     This method checks whether a rewrite applies to the query's resolved client. A global
    ///     rewrite always applies. A scoped one applies only when <paramref name="resolvedClient" />
    ///     is not <see langword="null" /> and its <c>ClientId</c> matches
    ///     <see cref="DnsCompiledRewrite.ClientId" />, or (when no client is named) its
    ///     <see cref="DnsClientInfo.ClientGroupId" /> matches
    ///     <see cref="DnsCompiledRewrite.ClientGroupId" />.
    /// </summary>
    private static bool AppliesToClient(DnsCompiledRewrite rewrite, DnsClientInfo resolvedClient)
    {
        if (rewrite.ClientId is null && rewrite.ClientGroupId is null)
            return true;

        if (resolvedClient is null)
            return false;

        if (rewrite.ClientId is { } clientId)
            return resolvedClient.ClientId == clientId;

        return resolvedClient.ClientGroupId == rewrite.ClientGroupId;
    }

    /// <summary>
    ///     This method resolves a query's source address to its registered, enabled client.
    ///     <c>DnsFilterEngine</c> and <c>DnsClientScheduleEngine</c> have their own copies of this
    ///     helper. Returns <see langword="null" /> when the resolver or directory is missing, no
    ///     address was given, the address has no mapping, no client is registered for it, or that
    ///     client is disabled.
    /// </summary>
    private async Task<DnsClientInfo> ResolveClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken
    )
    {
        if (clientIdentityResolver is null || clientDirectory is null || remoteAddress is null)
            return null;

        ClientIdentityResolution resolution = await clientIdentityResolver.ResolveAsync(
            remoteAddress,
            stoppingToken
        );

        if (resolution is null)
            return null;

        DnsClientInfo client = await clientDirectory.FindAsync(
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
        DnsRewriteTargetKindEnum kind,
        DnsResourceRecordTypeEnum questionType
    ) =>
        kind switch
        {
            DnsRewriteTargetKindEnum.A => questionType == DnsResourceRecordTypeEnum.A,
            DnsRewriteTargetKindEnum.Aaaa => questionType == DnsResourceRecordTypeEnum.Aaaa,
            DnsRewriteTargetKindEnum.Cname => questionType
                is DnsResourceRecordTypeEnum.A
                    or DnsResourceRecordTypeEnum.Aaaa
                    or DnsResourceRecordTypeEnum.Cname,
            _ => false,
        };

    /// <summary>
    ///     This method builds the complete answer response for a matched rewrite.
    /// </summary>
    private DnsMessage BuildResponse(
        DnsMessage request,
        DnsQuestion question,
        DnsCompiledRewrite match
    )
    {
        DnsRewriteTargetKindEnum kind = match.Kind;
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
    ///     This method checks a queried name against a wildcard rewrite's compiled regex. A match
    ///     timeout counts as no match and is logged; it does not throw.
    /// </summary>
    private bool MatchesWildcard(string queriedPresentation, DnsCompiledRewrite rewrite)
    {
        try
        {
            return rewrite.Wildcard.IsMatch(queriedPresentation);
        }
        catch (RegexMatchTimeoutException)
        {
            logger.LogWarning(
                "A DNS rewrite wildcard domain timed out matching a query name; treating as a non-match. Domain: {Domain}",
                rewrite.Domain
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
    internal static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();
}
