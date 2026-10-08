using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class implements <see cref="IDnsFilterEngine" />: it works out which
///     <see cref="DnsFilterListEntity" /> lists apply to a client and checks the client address,
///     the queried name, or the answer (CNAME targets and A/AAAA addresses) against them.
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> calls it at
///     several points in the query path (see that class). Lists, assignments and entries are
///     read from the repositories on every check; nothing is cached except compiled patterns.
///     <para>
///         <strong>Which lists apply.</strong> A client that matches one or more non-default
///         <see cref="DnsClientGroupEntity" /> groups gets the union of those groups' lists; the
///         default group's lists apply only when no group matches. A group matches by a CIDR
///         row containing the client address, by a DNS-over-HTTPS ClientID row, or through the
///         client registry: when an <see cref="IClientIdentityResolver" /> is available, the
///         address is resolved to a client identifier and looked up in
///         <see cref="IDnsClientRepository" />, and an enabled <see cref="DnsClientEntity" />
///         adds its enabled <see cref="DnsClientEntity.ClientGroupId" /> group. That client's
///         direct <see cref="DnsClientListAssignmentEntity" /> rows are added as well, whether or
///         not it contributed a group (see <see cref="ResolveApplicableListsAsync" />). The
///         resolver and the client repositories are optional; without them only CIDR and
///         ClientID matching apply.
///     </para>
///     <para>
///         <strong>Domain entries.</strong> A <see cref="DnsFilterEntryKindEnum.Domain" /> entry
///         covers the name and every subdomain (<c>example.com</c> also matches
///         <c>www.example.com</c>), the usual ad-blocker convention. A domain list may also hold
///         <see cref="DnsFilterEntryKindEnum.Wildcard" /> globs and
///         <see cref="DnsFilterEntryKindEnum.Regex" /> .NET regular expressions, matched against
///         the full queried name. Globs are turned into anchored regexes, so one engine with one
///         timeout handles both. Compiled patterns are cached in
///         <see cref="_compiledPatternCache" />, shared by every query since the engine is a
///         singleton. Every match runs with <see cref="RegexMatchTimeout" />, so a pathological
///         pattern cannot stall query answering; a timeout counts as no match and is logged.
///     </para>
///     <para>
///         <strong>Exceptions and precedence.</strong> An adblock <c>@@</c> exception only cancels
///         matches in its own list (see <see cref="ListCoversDomain" />). When a block list and an
///         allow list both match, <see cref="DnsFilterOptions.Precedence" /> decides.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach every scoped repository this engine needs, once per check.</param>
/// <param name="options">
///     The <see cref="DnsFilterOptions.Enable" /> switch, block-response mode, precedence and
///     CNAME-cloaking setting, read live through <see cref="IOptionsMonitor{TOptions}" /> rather
///     than <see cref="IOptions{TOptions}" />, so a <c>FilteringSettingsController.Patch</c>
///     applies to the next check without a restart.
/// </param>
/// <param name="logger">The logger this engine records regex timeouts and compile failures to.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver that maps a query's source address to a client identifier, for the
///     client-registry path described in the class summary. <see langword="null" /> (the
///     default) skips that path.
/// </param>
public sealed class DnsFilterEngine(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsFilterEngine
{
    /// <summary>
    ///     This field bounds how long one wildcard or regex match may run (see the class summary).
    /// </summary>
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     This field caches each wildcard or regex entry's compiled <see cref="Regex" /> (or
    ///     <see langword="null" /> if it failed to compile), keyed by kind and pattern text.
    ///     Entries are never removed.
    /// </summary>
    private readonly ConcurrentDictionary<
        (DnsFilterEntryKindEnum Kind, string Pattern),
        Regex
    > _compiledPatternCache = new();

    /// <inheritdoc />
    public async ValueTask<DnsFilterDecision> EvaluateClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    )
    {
        if (!options.CurrentValue.Enable)
            return DnsFilterDecision.Allowed;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IReadOnlyList<DnsFilterListEntity> lists = await ResolveApplicableListsAsync(
            scope,
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.ClientSource,
            stoppingToken
        );

        return await EvaluateNetworkListsAsync(scope, lists, remoteAddress, stoppingToken);
    }

    /// <inheritdoc />
    public async ValueTask<DnsFilterDecision> EvaluateDomainAsync(
        DnsName queriedName,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    )
    {
        if (!options.CurrentValue.Enable)
            return DnsFilterDecision.Allowed;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        IReadOnlyList<DnsFilterListEntity> lists = await ResolveApplicableListsAsync(
            scope,
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.Domain,
            stoppingToken
        );

        string queriedPresentation = NormalizeDomain(queriedName.ToString());
        bool blockMatch = false;
        bool allowMatch = false;

        foreach (DnsFilterListEntity list in lists)
        {
            IDnsFilterListEntryRepository entryRepository =
                scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
            IReadOnlyList<DnsFilterListEntryEntity> entries =
                await entryRepository.ListByListIdAsync(list.Id, stoppingToken);

            if (!ListCoversDomain(queriedPresentation, entries))
                continue;

            if (list.Action == DnsFilterListActionEnum.Block)
                blockMatch = true;
            else
                allowMatch = true;
        }

        return Resolve(blockMatch, allowMatch, "a domain filter list");
    }

    /// <summary>
    ///     This method checks whether one list covers a name: some ordinary entry matches it and
    ///     none of the same list's exception entries (<see cref="DnsFilterListEntryEntity.IsException" />,
    ///     an adblock <c>@@</c> rule) does. An exception is the list author saying "my broader rule
    ///     does not cover this name", so it only cancels its own list's match, before precedence.
    ///     Treating it like an allow list would let one subscribed list unblock names another
    ///     list blocks.
    /// </summary>
    private bool ListCoversDomain(
        string queriedPresentation,
        IReadOnlyList<DnsFilterListEntryEntity> entries
    ) =>
        entries.Any(entry => !entry.IsException && MatchesDomain(queriedPresentation, entry))
        && !entries.Any(entry => entry.IsException && MatchesDomain(queriedPresentation, entry));

    /// <summary>
    ///     This method resolves a block/allow match pair into a final decision, honoring
    ///     <see cref="DnsFilterOptions.Precedence" /> when both matched.
    /// </summary>
    private DnsFilterDecision Resolve(bool blockMatch, bool allowMatch, string reason)
    {
        if (!blockMatch)
            return DnsFilterDecision.Allowed;

        if (!allowMatch)
            return DnsFilterDecision.Denied(reason);

        return options.CurrentValue.Precedence == DnsFilterListPrecedenceEnum.BlockWins
            ? DnsFilterDecision.Denied(reason)
            : DnsFilterDecision.Allowed;
    }

    /// <inheritdoc />
    public async ValueTask<DnsFilterDecision> EvaluateResponseAsync(
        DnsMessage response,
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    )
    {
        if (!options.CurrentValue.Enable)
            return DnsFilterDecision.Allowed;

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        DnsFilterDecision cnameDecision = await EvaluateCnameTargetsAsync(
            scope,
            response,
            remoteAddress,
            clientId,
            stoppingToken
        );

        if (cnameDecision.IsDenied)
            return cnameDecision;

        IReadOnlyList<DnsFilterListEntity> lists = await ResolveApplicableListsAsync(
            scope,
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.ResponseIp,
            stoppingToken
        );

        if (lists.Count == 0)
            return DnsFilterDecision.Allowed;

        foreach (DnsResourceRecord answer in response.Answers)
        {
            if (answer.Type is not (DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa))
                continue;

            IPAddress address = new(answer.Data);
            DnsFilterDecision decision = await EvaluateNetworkListsAsync(
                scope,
                lists,
                address,
                stoppingToken
            );

            if (decision.IsDenied)
                return decision;
        }

        return DnsFilterDecision.Allowed;
    }

    /// <summary>
    ///     This method blocks CNAME cloaking: a tracker hidden behind a first-party name
    ///     (<c>metrics.shop.example</c> CNAME <c>shop.tracker.example</c>) is only visible in the
    ///     answer, never in the question, so the domain check before dispatch cannot see it.
    ///     Every CNAME target in the answer, anywhere in a chain, is checked against the client's
    ///     domain lists as if it had been queried: the same scoping, list exceptions and
    ///     precedence, so an allowed target is let through.
    ///     <para>
    ///         When the queried name is on one of the client's allow lists, the chain is not
    ///         checked: the operator allowed that name, and blocking it for where it points would
    ///         override that choice unless the target were allowed too.
    ///     </para>
    ///     <para>
    ///         Checking the targets is enough to cover the whole chain, since each later record
    ///         in a chain is owned by the previous record's target. A DNAME answer carries a
    ///         synthesized CNAME (RFC 6672 §3.4), so it is covered too.
    ///     </para>
    /// </summary>
    private async Task<DnsFilterDecision> EvaluateCnameTargetsAsync(
        AsyncServiceScope scope,
        DnsMessage response,
        IPAddress remoteAddress,
        string clientId,
        CancellationToken stoppingToken
    )
    {
        if (!options.CurrentValue.BlockCnameCloaking)
            return DnsFilterDecision.Allowed;

        List<string> targets = [];

        foreach (DnsResourceRecord answer in response.Answers)
        {
            if (answer.Type != DnsResourceRecordTypeEnum.Cname)
                continue;

            string target = TryReadCnameTarget(answer);

            if (target is not null && !targets.Contains(target))
                targets.Add(target);
        }

        // Most answers carry no CNAME at all, and those should cost nothing extra.
        if (targets.Count == 0)
            return DnsFilterDecision.Allowed;

        IReadOnlyList<DnsFilterListEntity> lists = await ResolveApplicableListsAsync(
            scope,
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.Domain,
            stoppingToken
        );

        if (lists.Count == 0)
            return DnsFilterDecision.Allowed;

        IDnsFilterListEntryRepository entryRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        List<(DnsFilterListEntity List, IReadOnlyList<DnsFilterListEntryEntity> Entries)> loaded =
        [];

        // Load each list's entries once for the whole chain, not once per target.
        foreach (DnsFilterListEntity list in lists)
            loaded.Add((list, await entryRepository.ListByListIdAsync(list.Id, stoppingToken)));

        if (response.Questions.Count > 0)
        {
            string queried = NormalizeDomain(response.Questions[0].Name.ToString());

            bool queriedNameAllowed = loaded.Any(item =>
                item.List.Action == DnsFilterListActionEnum.Allow
                && ListCoversDomain(queried, item.Entries)
            );

            if (queriedNameAllowed)
                return DnsFilterDecision.Allowed;
        }

        foreach (string target in targets)
        {
            bool blockMatch = false;
            bool allowMatch = false;

            foreach (
                (
                    DnsFilterListEntity list,
                    IReadOnlyList<DnsFilterListEntryEntity> entries
                ) in loaded
            )
            {
                if (!ListCoversDomain(target, entries))
                    continue;

                if (list.Action == DnsFilterListActionEnum.Block)
                    blockMatch = true;
                else
                    allowMatch = true;
            }

            DnsFilterDecision decision = Resolve(
                blockMatch,
                allowMatch,
                $"a domain filter list (via CNAME target {target})"
            );

            if (decision.IsDenied)
                return decision;
        }

        return DnsFilterDecision.Allowed;
    }

    /// <summary>
    ///     This method reads a CNAME record's target as a normalized name, or
    ///     <see langword="null" /> when its data is malformed, so a bad record in an upstream answer
    ///     cannot make filtering throw.
    /// </summary>
    private static string TryReadCnameTarget(DnsResourceRecord record)
    {
        try
        {
            CnameRecordData data = (CnameRecordData)
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Cname, record.Data);

            return NormalizeDomain(data.Target.ToString());
        }
        catch (Exception exception)
            when (exception
                    is FormatException
                        or ArgumentException
                        or IndexOutOfRangeException
                        or NotSupportedException
            )
        {
            return null;
        }
    }

    /// <inheritdoc />
    public DnsMessage BuildDeniedResponse(DnsMessage request)
    {
        DnsResourceRecordTypeEnum? questionType =
            request.Questions.Count > 0 ? request.Questions[0].Type : null;

        bool canSinkhole =
            options.CurrentValue.BlockResponseMode == DnsFilterBlockResponseModeEnum.SinkholeAddress
            && questionType is DnsResourceRecordTypeEnum.A or DnsResourceRecordTypeEnum.Aaaa;

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = request.Opcode,
            RecursionDesired = request.RecursionDesired,
            // This code cannot know whether recursion is available to this client, and echoing
            // RD would only repeat the client's request, so false is the value that never
            // misleads.
            RecursionAvailable = false,
            CheckingDisabled = request.CheckingDisabled,
            ResponseCode = canSinkhole ? DnsResponseCodeEnum.NoError : DnsResponseCodeEnum.NxDomain,
            Questions = [.. request.Questions],
        };

        if (!canSinkhole)
            return response;

        IPAddress sinkhole = IPAddress.Parse(
            questionType == DnsResourceRecordTypeEnum.A
                ? options.CurrentValue.SinkholeAddressV4
                : options.CurrentValue.SinkholeAddressV6
        );

        response.Answers.Add(
            new DnsResourceRecord(
                request.Questions[0].Name,
                questionType.Value,
                DnsClassEnum.Internet,
                options.CurrentValue.SinkholeTimeToLiveSeconds,
                sinkhole.GetAddressBytes()
            )
        );

        return response;
    }

    /// <summary>
    ///     This method returns the enabled lists of one kind that apply to a client: every list
    ///     assigned to each enabled non-default group whose networks contain the client address,
    ///     whose <see cref="DnsClientGroupNetworkEntity.ClientId" /> label matches
    ///     <paramref name="clientId" />, or that the resolved <see cref="DnsClientEntity" /> belongs
    ///     to. When no group matches, the default group's lists apply. The resolved client's direct
    ///     <see cref="DnsClientListAssignmentEntity" /> rows are always added.
    /// </summary>
    private async Task<IReadOnlyList<DnsFilterListEntity>> ResolveApplicableListsAsync(
        AsyncServiceScope scope,
        IPAddress remoteAddress,
        string clientId,
        DnsFilterListKindEnum kind,
        CancellationToken stoppingToken
    )
    {
        IDnsClientGroupRepository groupRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupRepository>();
        IDnsClientGroupNetworkRepository networkRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupNetworkRepository>();
        IDnsClientGroupListAssignmentRepository assignmentRepository =
            scope.ServiceProvider.GetRequiredService<IDnsClientGroupListAssignmentRepository>();
        IDnsFilterListRepository listRepository =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();

        IReadOnlyList<DnsClientGroupEntity> allGroups = await groupRepository.ListAsync(
            group => !group.IsDisabled,
            stoppingToken
        );

        List<DnsClientGroupEntity> matchedGroups = [];

        foreach (DnsClientGroupEntity group in allGroups.Where(group => !group.IsDefault))
        {
            IReadOnlyList<DnsClientGroupNetworkEntity> networks =
                await networkRepository.ListByGroupIdAsync(group.Id, stoppingToken);

            // A group matches by a Cidr row containing the source address, or by a ClientId row
            // matching the ClientID from a DNS-over-HTTPS URL path (see
            // DnsClientGroupNetworkEntity.ClientId).
            bool matchesNetwork = networks.Any(network =>
                ClientAddress.NetworkContains(network.Cidr, remoteAddress)
            );
            bool matchesClientId =
                clientId is not null
                && networks.Any(network => MatchesClientId(network.ClientId, clientId));

            if (matchesNetwork || matchesClientId)
                matchedGroups.Add(group);
        }

        // The client registry is a third way into a group, added to the matches above. A
        // disabled client contributes neither its group nor its direct list assignments.
        DnsClientEntity resolvedClient = await ResolveClientAsync(
            scope,
            remoteAddress,
            stoppingToken
        );

        if (resolvedClient?.ClientGroupId is { } clientGroupId)
        {
            DnsClientGroupEntity clientGroup = await groupRepository.GetByIdAsync(
                clientGroupId,
                stoppingToken
            );

            if (
                clientGroup is not null
                && !clientGroup.IsDisabled
                && matchedGroups.All(group => group.Id != clientGroup.Id)
            )
                matchedGroups.Add(clientGroup);
        }

        // The default group applies only when no group matched by any of the three means.
        if (matchedGroups.Count == 0)
        {
            DnsClientGroupEntity defaultGroup = await groupRepository.GetDefaultAsync(
                stoppingToken
            );

            if (defaultGroup is not null && !defaultGroup.IsDisabled)
                matchedGroups.Add(defaultGroup);
        }

        HashSet<Guid> listIds = [];

        foreach (DnsClientGroupEntity group in matchedGroups)
        {
            IReadOnlyList<DnsClientGroupListAssignmentEntity> assignments =
                await assignmentRepository.ListByGroupIdAsync(group.Id, stoppingToken);

            foreach (DnsClientGroupListAssignmentEntity assignment in assignments)
                listIds.Add(assignment.ListId);
        }

        // The resolved client's direct list assignments apply whether or not it matched a group.
        if (resolvedClient is not null)
        {
            IDnsClientListAssignmentRepository clientListAssignmentRepository =
                scope.ServiceProvider.GetService<IDnsClientListAssignmentRepository>();

            if (clientListAssignmentRepository is not null)
            {
                IReadOnlyList<DnsClientListAssignmentEntity> clientAssignments =
                    await clientListAssignmentRepository.ListByClientIdAsync(
                        resolvedClient.Id,
                        stoppingToken
                    );

                foreach (DnsClientListAssignmentEntity assignment in clientAssignments)
                    listIds.Add(assignment.ListId);
            }
        }

        List<DnsFilterListEntity> lists = [];

        foreach (Guid listId in listIds)
        {
            DnsFilterListEntity list = await listRepository.GetByIdAsync(listId, stoppingToken);

            if (list is not null && !list.IsDisabled && list.Kind == kind)
                lists.Add(list);
        }

        return lists;
    }

    /// <summary>
    ///     This method resolves a query's source address to its registered, enabled
    ///     <see cref="DnsClientEntity" />. Returns <see langword="null" /> when the resolver or
    ///     client repository is missing, the address has no mapping, no client is registered for
    ///     it, or that client is disabled.
    /// </summary>
    private async Task<DnsClientEntity> ResolveClientAsync(
        AsyncServiceScope scope,
        IPAddress remoteAddress,
        CancellationToken stoppingToken
    )
    {
        if (clientIdentityResolver is null)
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
    ///     This method checks one IP address against already-resolved network lists
    ///     (<see cref="DnsFilterListKindEnum.ClientSource" /> or <see cref="DnsFilterListKindEnum.ResponseIp" />).
    ///     Entries are matched with <see cref="ClientAddress.NetworkContains" />, which treats
    ///     an IPv4-mapped IPv6 address as its IPv4 form.
    /// </summary>
    private async Task<DnsFilterDecision> EvaluateNetworkListsAsync(
        AsyncServiceScope scope,
        IReadOnlyList<DnsFilterListEntity> lists,
        IPAddress address,
        CancellationToken stoppingToken
    )
    {
        bool blockMatch = false;
        bool allowMatch = false;

        foreach (DnsFilterListEntity list in lists)
        {
            IDnsFilterListEntryRepository entryRepository =
                scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
            IReadOnlyList<DnsFilterListEntryEntity> entries =
                await entryRepository.ListByListIdAsync(list.Id, stoppingToken);

            bool matches = entries.Any(entry =>
                ClientAddress.NetworkContains(entry.Value, address)
            );

            if (!matches)
                continue;

            if (list.Action == DnsFilterListActionEnum.Block)
                blockMatch = true;
            else
                allowMatch = true;
        }

        return Resolve(blockMatch, allowMatch, "a network filter list");
    }

    /// <summary>
    ///     This method checks whether a <see cref="DnsClientGroupNetworkEntity.ClientId" />-kind
    ///     row's label matches a query's DNS-over-HTTPS ClientID, case-insensitively.
    /// </summary>
    private static bool MatchesClientId(string entryClientId, string queryClientId) =>
        !string.IsNullOrWhiteSpace(entryClientId)
        && string.Equals(entryClientId, queryClientId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     This method checks whether a single entry covers a queried name, dispatching on the
    ///     entry's <see cref="DnsFilterListEntryEntity.Kind" />.
    /// </summary>
    private bool MatchesDomain(string queriedPresentation, DnsFilterListEntryEntity entry)
    {
        switch (entry.Kind)
        {
            case DnsFilterEntryKindEnum.Wildcard:
                return MatchesPattern(
                    DnsFilterEntryKindEnum.Wildcard,
                    entry.Value,
                    queriedPresentation,
                    BuildWildcardRegexPattern
                );

            case DnsFilterEntryKindEnum.Regex:
                return MatchesPattern(
                    DnsFilterEntryKindEnum.Regex,
                    entry.Value,
                    queriedPresentation,
                    static pattern => pattern
                );

            default:
                string normalizedEntry = NormalizeDomain(entry.Value);

                return queriedPresentation == normalizedEntry
                    || queriedPresentation.EndsWith(
                        "." + normalizedEntry,
                        StringComparison.Ordinal
                    );
        }
    }

    /// <summary>
    ///     This method matches a wildcard or regex entry against the queried name, compiling and
    ///     caching the pattern on first use (see <see cref="_compiledPatternCache" />). A compile
    ///     failure or a match timeout counts as no match and is logged; neither throws.
    /// </summary>
    /// <param name="kind">The entry's kind, used only as part of the cache key.</param>
    /// <param name="rawPattern">The entry's pattern text as stored.</param>
    /// <param name="queriedPresentation">The already-normalized queried name to match against.</param>
    /// <param name="toRegexPattern">
    ///     The function that turns <paramref name="rawPattern" /> into the regex to compile: the
    ///     identity for a <see cref="DnsFilterEntryKindEnum.Regex" /> entry, or
    ///     <see cref="BuildWildcardRegexPattern" /> for a <see cref="DnsFilterEntryKindEnum.Wildcard" />
    ///     one.
    /// </param>
    private bool MatchesPattern(
        DnsFilterEntryKindEnum kind,
        string rawPattern,
        string queriedPresentation,
        Func<string, string> toRegexPattern
    )
    {
        if (string.IsNullOrWhiteSpace(rawPattern))
            return false;

        Regex regex = _compiledPatternCache.GetOrAdd(
            (kind, rawPattern),
            key =>
            {
                Regex compiled = CompileOrNull(toRegexPattern(key.Pattern));

                if (compiled is null)
                {
                    // The null result is cached, so this is normally logged once per pattern.
                    // FilterListEntriesController compiles patterns before saving them, so this
                    // is expected only for rows written some other way.
                    logger.LogWarning(
                        "A {Kind} filter list entry has a pattern that failed to compile; treating as a non-match. Pattern: {Pattern}",
                        key.Kind,
                        key.Pattern
                    );
                }

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
            // A backtracking pattern must not stall or fail the query, so a timeout is no match.
            logger.LogWarning(
                "A {Kind} filter list entry pattern timed out matching a query name; treating as a non-match. "
                    + "Pattern: {Pattern}",
                kind,
                rawPattern
            );

            return false;
        }
    }

    /// <summary>
    ///     This method compiles a pattern case-insensitively with the engine's match timeout and
    ///     returns <see langword="null" /> instead of throwing when the pattern is malformed.
    ///     <c>FilterListEntriesController</c> and <c>FilterPatternPreviewController</c> also call
    ///     it to validate patterns.
    /// </summary>
    public static Regex CompileOrNull(string pattern)
    {
        try
        {
            return new Regex(
                pattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                RegexMatchTimeout
            );
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    ///     This method turns a shell-style glob into an anchored regex pattern: <c>*</c> matches any
    ///     run of characters (dots included), <c>?</c> matches one character, and everything else
    ///     is escaped, so a wildcard author never needs regex syntax. Case is handled by
    ///     <see cref="CompileOrNull" />.
    /// </summary>
    public static string BuildWildcardRegexPattern(string glob)
    {
        string escaped = Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".");

        return "^" + escaped + "$";
    }

    /// <summary>
    ///     This method normalizes a domain to lowercase with no trailing dot, so
    ///     <see cref="DnsName.ToString" />'s always-fully-qualified form compares correctly
    ///     against an operator-entered value that may or may not have one.
    /// </summary>
    private static string NormalizeDomain(string value) => value.TrimEnd('.').ToLowerInvariant();
}
