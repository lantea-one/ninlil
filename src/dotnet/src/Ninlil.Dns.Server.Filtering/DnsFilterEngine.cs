using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
///     several points in the query path (see that class). Groups, assignments, lists, entries
///     and registered clients come from in-memory copies that
///     <see cref="DnsFilterConfigurationCache" /> and <see cref="DnsClientDirectory" /> keep
///     current, so a check opens no database scope while those copies are fresh (see those classes
///     for how a change reaches them). Compiled patterns are cached here.
///     <para>
///         <strong>Which lists apply.</strong> A client that matches one or more non-default
///         <see cref="DnsClientGroupEntity" /> groups gets the union of those groups' lists; the
///         default group's lists apply only when no group matches. A group matches by a CIDR
///         row containing the client address, by a DNS-over-HTTPS ClientID row, or through the
///         client registry: when an <see cref="IClientIdentityResolver" /> is available, the
///         address is resolved to a client identifier and looked up in
///         <see cref="DnsClientDirectory" />, and an enabled <see cref="DnsClientEntity" />
///         adds its enabled <see cref="DnsClientEntity.ClientGroupId" /> group. That client's
///         direct <see cref="DnsClientListAssignmentEntity" /> rows are added as well, whether or
///         not it contributed a group (see <see cref="ResolveApplicableListsAsync" />). Every list
///         marked <see cref="DnsFilterListEntity.AppliesToAllClients" /> applies to all clients
///         on top of that. The resolver and the directory are optional; without them only CIDR
///         and ClientID matching apply.
///     </para>
///     <para>
///         <strong>Domain entries.</strong> A <see cref="DnsFilterEntryKindEnum.Domain" /> entry
///         covers the name and every subdomain (<c>example.com</c> also matches
///         <c>www.example.com</c>), the usual ad-blocker convention. A domain list may also hold
///         <see cref="DnsFilterEntryKindEnum.Wildcard" /> globs and
///         <see cref="DnsFilterEntryKindEnum.Regex" /> .NET regular expressions, matched against
///         the full queried name. Plain domain entries are looked up in a hash set per list (see
///         <see cref="DnsFilterListIndex" />), which gives the same answer as comparing every
///         entry. Globs are turned into anchored regexes, so one engine with one
///         timeout handles both. The index compiles each pattern once, when the list is loaded
///         (see <see cref="DnsFilterListIndex.Build" />), and every query reuses the compiled
///         <see cref="Regex" />. Every match runs with <see cref="RegexMatchTimeout" />, so a
///         pathological pattern cannot stall query answering; a timeout counts as no match and is
///         logged.
///     </para>
///     <para>
///         <strong>Exceptions and precedence.</strong> An adblock <c>@@</c> exception only cancels
///         matches in its own list (see <see cref="ListCoversDomain" />). When a block list and an
///         allow list both match, <see cref="DnsFilterOptions.Precedence" /> decides.
///     </para>
/// </summary>
/// <param name="cache">The in-memory groups, assignments, lists and list entries.</param>
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
/// <param name="clientDirectory">
///     The optional in-memory lookup from a client identifier to the registered client.
///     <see langword="null" /> (the default) skips the client-registry path.
/// </param>
public sealed class DnsFilterEngine(
    DnsFilterConfigurationCache cache,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null,
    DnsClientDirectory clientDirectory = null
) : IDnsFilterEngine
{
    /// <summary>
    ///     This field bounds how long one wildcard or regex match may run (see the class summary).
    /// </summary>
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <inheritdoc />
    public async ValueTask<DnsFilterDecision> EvaluateClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default,
        string clientId = null
    )
    {
        if (!options.CurrentValue.Enable)
            return DnsFilterDecision.Allowed;

        IReadOnlyList<DnsFilterList> lists = await ResolveApplicableListsAsync(
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.ClientSource,
            stoppingToken
        );

        return await EvaluateNetworkListsAsync(lists, remoteAddress, stoppingToken);
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

        IReadOnlyList<DnsFilterList> lists = await ResolveApplicableListsAsync(
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.Domain,
            stoppingToken
        );

        string queriedPresentation = NormalizeDomain(queriedName.ToString());
        bool blockMatch = false;
        bool allowMatch = false;

        foreach (DnsFilterList list in lists)
        {
            DnsFilterListIndex entries = await cache.GetEntriesAsync(list, stoppingToken);

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
    private bool ListCoversDomain(string queriedPresentation, DnsFilterListIndex entries) =>
        MatchesAnyDomainEntry(queriedPresentation, entries, isException: false)
        && !MatchesAnyDomainEntry(queriedPresentation, entries, isException: true);

    /// <summary>
    ///     This method checks whether any ordinary entry (or, with <paramref name="isException" />,
    ///     any exception entry) of a list covers a name: first the hashed plain entries, then the
    ///     wildcard and regex ones the list's trigram index offers
    ///     (<see cref="DnsFilterListIndex.MatchesAnyPattern{TState}" />). An offered pattern whose
    ///     necessary literal the name lacks is skipped without running its regex
    ///     (<see cref="DnsFilterPatternEntry.CanMatch" />); the regex still decides every other
    ///     offered pattern, and a pattern the index does not offer cannot match, so the answer is
    ///     the one a full scan gives.
    /// </summary>
    private bool MatchesAnyDomainEntry(
        string queriedPresentation,
        DnsFilterListIndex entries,
        bool isException
    )
    {
        if (entries.ContainsDomain(queriedPresentation, isException))
            return true;

        // The index offers only the patterns whose trigram the name contains, plus those with
        // no literal; the literal test and the regex still decide each one offered.
        return entries.MatchesAnyPattern(
            queriedPresentation,
            isException,
            (Engine: this, Name: queriedPresentation),
            static (pattern, nameIsAscii, state) =>
                pattern.CanMatch(state.Name, nameIsAscii)
                && state.Engine.MatchesPatternEntry(state.Name, pattern)
        );
    }

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

        DnsFilterDecision cnameDecision = await EvaluateCnameTargetsAsync(
            response,
            remoteAddress,
            clientId,
            stoppingToken
        );

        if (cnameDecision.IsDenied)
            return cnameDecision;

        IReadOnlyList<DnsFilterList> lists = await ResolveApplicableListsAsync(
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
    ///         synthesized CNAME (RFC 6672 §3.2), so it is covered too.
    ///     </para>
    /// </summary>
    private async Task<DnsFilterDecision> EvaluateCnameTargetsAsync(
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

        IReadOnlyList<DnsFilterList> lists = await ResolveApplicableListsAsync(
            remoteAddress,
            clientId,
            DnsFilterListKindEnum.Domain,
            stoppingToken
        );

        if (lists.Count == 0)
            return DnsFilterDecision.Allowed;

        List<(DnsFilterList List, DnsFilterListIndex Entries)> loaded = [];

        // Fetch each list's entries once for the whole chain, not once per target.
        foreach (DnsFilterList list in lists)
            loaded.Add((list, await cache.GetEntriesAsync(list, stoppingToken)));

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

            foreach ((DnsFilterList list, DnsFilterListIndex entries) in loaded)
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
    ///     <see cref="DnsClientListAssignmentEntity" /> rows are always added, and so is every
    ///     list marked <see cref="DnsFilterListEntity.AppliesToAllClients" />.
    /// </summary>
    private async ValueTask<IReadOnlyList<DnsFilterList>> ResolveApplicableListsAsync(
        IPAddress remoteAddress,
        string clientId,
        DnsFilterListKindEnum kind,
        CancellationToken stoppingToken
    )
    {
        DnsFilterConfiguration configuration = await cache.GetConfigurationAsync(stoppingToken);

        List<DnsFilterGroup> matchedGroups = [];

        foreach (DnsFilterGroup group in configuration.MatchableGroups)
        {
            // A group matches by a Cidr row containing the source address, or by a ClientId row
            // matching the ClientID from a DNS-over-HTTPS URL path (see
            // DnsClientGroupNetworkEntity.ClientId).
            bool matchesNetwork = group.Networks.Any(network =>
                network.Network?.Contains(remoteAddress) == true
            );
            bool matchesClientId =
                clientId is not null
                && group.Networks.Any(network => MatchesClientId(network.ClientId, clientId));

            if (matchesNetwork || matchesClientId)
                matchedGroups.Add(group);
        }

        // The client registry is a third way into a group, added to the matches above. A
        // disabled client contributes neither its group nor its direct list assignments.
        DnsClientInfo resolvedClient = await ResolveClientAsync(remoteAddress, stoppingToken);

        if (
            resolvedClient?.ClientGroupId is { } clientGroupId
            && configuration.EnabledGroups.TryGetValue(
                clientGroupId,
                out DnsFilterGroup clientGroup
            )
            && matchedGroups.All(group => group.Id != clientGroup.Id)
        )
            matchedGroups.Add(clientGroup);

        // The default group applies only when no group matched by any of the three means.
        if (matchedGroups.Count == 0 && configuration.DefaultGroup is { } defaultGroup)
            matchedGroups.Add(defaultGroup);

        HashSet<Guid> listIds = [];

        foreach (DnsFilterGroup group in matchedGroups)
            listIds.UnionWith(group.ListIds);

        // A list marked for every client applies to everyone, whether or not a group or client
        // matched, and no assignment can take it away.
        listIds.UnionWith(configuration.AllClientListIds);

        // The resolved client's direct list assignments apply whether or not it matched a group.
        if (
            resolvedClient is not null
            && configuration.ClientListIds.TryGetValue(resolvedClient.ClientId, out Guid[] direct)
        )
            listIds.UnionWith(direct);

        List<DnsFilterList> lists = [];

        foreach (Guid listId in listIds)
        {
            if (
                configuration.Lists.TryGetValue(listId, out DnsFilterList list)
                && list.Kind == kind
            )
                lists.Add(list);
        }

        return lists;
    }

    /// <summary>
    ///     This method resolves a query's source address to its registered, enabled client.
    ///     Returns <see langword="null" /> when the resolver or the client directory is missing,
    ///     the address has no mapping, no client is registered for it, or that client is disabled.
    /// </summary>
    private async ValueTask<DnsClientInfo> ResolveClientAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken
    )
    {
        if (clientIdentityResolver is null || clientDirectory is null)
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
    ///     This method checks one IP address against already-resolved network lists
    ///     (<see cref="DnsFilterListKindEnum.ClientSource" /> or <see cref="DnsFilterListKindEnum.ResponseIp" />).
    ///     Entries are matched with <see cref="NetworkEntry.Contains" />, which treats
    ///     an IPv4-mapped IPv6 address as its IPv4 form.
    /// </summary>
    private async Task<DnsFilterDecision> EvaluateNetworkListsAsync(
        IReadOnlyList<DnsFilterList> lists,
        IPAddress address,
        CancellationToken stoppingToken
    )
    {
        bool blockMatch = false;
        bool allowMatch = false;

        foreach (DnsFilterList list in lists)
        {
            DnsFilterListIndex entries = await cache.GetEntriesAsync(list, stoppingToken);

            if (!entries.ContainsAddress(address))
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
    ///     This method matches a wildcard or regex entry against the queried name with the regex
    ///     the index compiled for it. An entry whose pattern did not compile (the cache logs
    ///     that when it loads the list) or is blank counts as no match, and so does a match
    ///     timeout, which is logged; neither throws.
    /// </summary>
    private bool MatchesPatternEntry(string queriedPresentation, DnsFilterPatternEntry entry)
    {
        if (entry.Compiled is null)
            return false;

        try
        {
            return entry.Compiled.IsMatch(queriedPresentation);
        }
        catch (RegexMatchTimeoutException)
        {
            // A backtracking pattern must not stall or fail the query, so a timeout is no match.
            logger.LogWarning(
                "A {Kind} filter list entry pattern timed out matching a query name; treating as a non-match. "
                    + "Pattern: {Pattern}",
                entry.Kind,
                entry.Value
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
    private static string NormalizeDomain(string value) =>
        DnsFilterListIndex.NormalizeDomain(value);
}
