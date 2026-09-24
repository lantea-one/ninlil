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
using Ninlil.Dns.Server.Core.Filtering;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class implements <see cref="IDnsFilterEngine" />: resolves a client's own
///     <see cref="DnsClientGroupEntity" /> membership, gathers every <see cref="DnsFilterListEntity" />
///     assigned to that group (or the default group, if the client matches no other), and checks
///     the relevant kind of entry against it.
///     <para>
///         <strong>Group resolution convention (not explicitly specified in the design
///         decisions, chosen here):</strong> a client matching one or more non-default groups is
///         filtered by the <em>union</em> of those groups' own assigned lists only -- the default
///         group's own lists do not additionally apply on top. A client matching no explicit
///         group falls back to the default group's own lists alone.
///     </para>
///     <para>
///         <strong>Client-derived group membership (a third way into a group, alongside CIDR
///         containment and DNS-over-HTTPS ClientID matching):</strong> when a
///         <see cref="IClientIdentityResolver" /> is available, a query's own source address is
///         resolved to its persistent client identifier and looked up in
///         <see cref="IDnsClientRepository" />; a matching, non-disabled <see cref="DnsClientEntity" />
///         whose own <see cref="DnsClientEntity.ClientGroupId" /> names a non-disabled group adds
///         that group to the matched set -- <em>unioned</em> with whatever CIDR/ClientID matching
///         already found, never a replacement for it, and never itself triggering the "no group
///         matched, fall back to the default group" branch's own suppression once at least one
///         group has matched by any means. The same resolved, non-disabled client's own direct
///         <see cref="DnsClientListAssignmentEntity" /> rows are unioned into the final list set
///         too, independently of whether it contributed a group match at all -- see
///         <see cref="ResolveApplicableListsAsync" /> for the exact resolution order. Both
///         <see cref="IClientIdentityResolver" /> and <see cref="IDnsClientRepository" />/
///         <see cref="IDnsClientListAssignmentRepository" /> are optional: when unavailable (no
///         DHCP/client-identity infrastructure wired up, or a test constructing this engine
///         directly), this whole path is silently skipped and CIDR/DoH-ClientID resolution behaves
///         exactly as it always has.
///     </para>
///     <para>
///         <strong>Domain wildcard convention (not explicitly specified, chosen here):</strong> a
///         <see cref="DnsFilterEntryKindEnum.Domain" />-kind entry covers itself and every
///         subdomain -- an entry of <c>example.com</c> matches <c>example.com</c> and
///         <c>www.example.com</c> alike, the common ad-blocker convention, rather than requiring
///         a separate <c>*.example.com</c> wildcard entry for subdomains.
///     </para>
///     <para>
///         <strong>Domain entry kinds</strong>: a <see cref="DnsFilterListEntryEntity" /> in a
///         <see cref="DnsFilterListKindEnum.Domain" /> list may additionally be a
///         <see cref="DnsFilterEntryKindEnum.Wildcard" /> shell-style glob or a
///         <see cref="DnsFilterEntryKindEnum.Regex" /> .NET regular expression, matched against
///         the queried name's own full presentation form -- the union of what Pi-hole (regex) and
///         AdGuard Home (wildcard/extended syntax) each offer. A wildcard pattern is translated to
///         an anchored regex internally rather than a second, bespoke matching engine, since .NET
///         already ships a real regex engine with a real timeout mechanism this project needs for
///         the regex kind regardless -- reusing it for wildcards too is strictly simpler than
///         hand-rolling a second glob matcher with its own, separate worst-case-complexity story.
///         Every compiled pattern is cached (<see cref="_compiledPatternCache" />), keyed by its
///         own kind and literal value, so a query never pays a fresh <see cref="Regex" />
///         compilation cost -- this engine is registered as a DI singleton
///         (<c>ServiceCollectionFilteringExtension</c>), so the cache is shared across every
///         query this whole server ever answers, not rebuilt per check.
///         <strong>Regex/wildcard matching is always timeout-guarded</strong>
///         (<see cref="RegexMatchTimeout" />, via <see cref="Regex" />'s own <c>matchTimeout</c>
///         constructor parameter) -- a malformed or catastrophic-backtracking pattern can never
///         stall query answering (architectural invariant #3); a timeout is treated exactly like
///         "no match," logged once per occurrence, never thrown back into the caller.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach every scoped repository this engine needs, once per check.</param>
/// <param name="options">
///     This engine's own configured <see cref="DnsFilterOptions.Enable" /> gate, block-response
///     mode, and list precedence -- read live via <see cref="IOptionsMonitor{TOptions}" /> rather
///     than the older, snapshotted <see cref="IOptions{TOptions}" />, since this engine is now
///     always registered as a DI singleton (<c>ServiceCollectionFilteringExtension</c>) regardless
///     of <see cref="DnsFilterOptions.Enable" />'s own value at process startup -- a
///     <c>FilteringSettingsController.Patch</c> write toggling it on or off must be observed on
///     this same, already-constructed instance's very next check, with no restart.
/// </param>
/// <param name="logger">The logger this engine records a regex timeout/compile failure to.</param>
/// <param name="clientIdentityResolver">
///     The optional resolver used to derive a query's own persistent client identifier from its
///     source address, for the client-derived group-membership/direct-list-assignment path this
///     class's own doc comment describes -- cross-cutting infrastructure exactly like
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" />'s own optional <c>IDnsRateLimiter</c>/
///     <c>DnsCookieService</c>/<c>IDnsRewriteEngine</c> dependencies, so every existing
///     caller/test constructing this engine without one keeps compiling and behaving unchanged.
///     <see langword="null" /> (the default) skips that whole path.
/// </param>
public sealed class DnsFilterEngine(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DnsFilterOptions> options,
    ILogger<DnsFilterEngine> logger,
    IClientIdentityResolver clientIdentityResolver = null
) : IDnsFilterEngine
{
    /// <summary>
    ///     This constant bounds how long a single wildcard/regex-kind entry's own match attempt
    ///     may run -- see this class's own doc comment for the full reasoning.
    /// </summary>
    private static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     This field caches every wildcard/regex-kind entry's own compiled <see cref="Regex" />
    ///     instance, keyed by its own kind and literal pattern value, so a query never pays a
    ///     fresh compilation cost -- see this class's own doc comment for the full reasoning.
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
    ///     an adblock-syntax <c>@@</c> rule) does. An exception only ever cancels its own list's
    ///     match -- it is the list author saying "my broader rule was not meant to cover this
    ///     name" -- so it is applied here, before precedence, rather than treated like a match
    ///     against an operator's allow list, which would let one subscribed list silently
    ///     unblock names another list blocks.
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
    ///     Every CNAME target in the answer -- anywhere in a multi-step chain -- is checked
    ///     against the client's own domain lists exactly as if it had been queried directly: the
    ///     same client/group scoping, the same list exceptions, and the same allow-list
    ///     precedence, so an allowed target is let through.
    ///     <para>
    ///         When the name actually queried is itself on one of the client's allow lists, the
    ///         chain is not checked at all: the operator explicitly allowed that name, and
    ///         blocking it for where it happens to point would override that choice with no way
    ///         to undo it short of also allowing the target.
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
    ///     <see langword="null" /> when its data is malformed -- a bad record in someone else's
    ///     answer must never make filtering throw (architectural invariant #3).
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
            // This check runs before either engine ever sees the query, so there is no way to
            // know here whether recursion is actually enabled elsewhere in this server -- unlike
            // mirroring the request's own RD bit (which would just echo the client's own ask,
            // not this server's real capability), false is the one value that is never
            // misleading about a query this method itself denied.
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
    ///     This method resolves which lists, of a single kind, apply to a client -- every list
    ///     assigned to every explicit, non-disabled group whose own networks contain the
    ///     client's address, whose own <see cref="DnsClientGroupNetworkEntity.ClientId" /> label
    ///     matches <paramref name="clientId" />, or whose own resolved <see cref="DnsClientEntity" />
    ///     names it directly (see this class's own doc comment for the full client-derived
    ///     resolution this third path adds); when none match by any of those three means, every
    ///     list assigned to the default group. The resolved client's own direct
    ///     <see cref="DnsClientListAssignmentEntity" /> rows, if any, are unioned into the final
    ///     result independently of whether it contributed a group match at all.
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

            // A group matches by either kind of row -- a Cidr row containing this client's own
            // source address, or (only ever populated for a DNS-over-HTTPS client, see
            // DnsClientGroupNetworkEntity.ClientId's own doc comment) a ClientId row whose label
            // matches the ClientID this query's own URL path carried.
            bool matchesNetwork = networks.Any(network =>
                NetworkContains(network.Cidr, remoteAddress)
            );
            bool matchesClientId =
                clientId is not null
                && networks.Any(network => MatchesClientId(network.ClientId, clientId));

            if (matchesNetwork || matchesClientId)
                matchedGroups.Add(group);
        }

        // A third, independent way into a group: resolve the query's own source address to its
        // persistent client identifier, look that identifier up in the known-client registry, and
        // -- if it names a non-disabled group of its own -- union that group in too. This is
        // additive only: it never replaces the CIDR/DoH-ClientID matching above, and a disabled
        // client contributes neither this group match nor its own direct list assignments below.
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

        // Only fall back to the default group when nothing matched by any of the three means
        // above -- a client-derived match (or a CIDR/DoH-ClientID one) means this client already
        // has an explicit group, so the default group's own lists must not additionally apply.
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

        // Union in the resolved client's own direct list assignments, if any -- independent of
        // whether it contributed a group match above, per this class's own doc comment.
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
    ///     This method resolves a query's own source address to its known, non-disabled
    ///     <see cref="DnsClientEntity" /> registry row, if this engine has both the identity
    ///     resolver and the client repository available to do so -- <see langword="null" /> the
    ///     moment either is unavailable, the resolver has no mapping for this address, no matching
    ///     row is registered, or the matching row is itself disabled (a disabled client
    ///     contributes neither a client-derived group match nor its own direct list assignments,
    ///     per this class's own doc comment).
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
    ///     This method checks a single IP address against every already-resolved network-kind
    ///     (<see cref="DnsFilterListKindEnum.ClientSource" />/<see cref="DnsFilterListKindEnum.ResponseIp" />)
    ///     list.
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

            bool matches = entries.Any(entry => NetworkContains(entry.Value, address));

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
    ///     This method checks whether a network entry (a bare address or a CIDR network)
    ///     contains a single address.
    /// </summary>
    private static bool NetworkContains(string entry, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(entry))
            return false;

        try
        {
            if (entry.Contains('/'))
                return IPNetwork.Parse(entry).Contains(address);

            return IPAddress.Parse(entry).Equals(address);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method checks whether a <see cref="DnsClientGroupNetworkEntity.ClientId" />-kind
    ///     row's own label matches a query's own DNS-over-HTTPS ClientID -- compared
    ///     case-insensitively, the same convention the entity's own doc comment establishes.
    /// </summary>
    private static bool MatchesClientId(string entryClientId, string queryClientId) =>
        !string.IsNullOrWhiteSpace(entryClientId)
        && string.Equals(entryClientId, queryClientId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     This method checks whether a single entry covers a queried name, dispatching on the
    ///     entry's own <see cref="DnsFilterListEntryEntity.Kind" />.
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
    ///     This method resolves (compiling and caching on first use, per <see cref="_compiledPatternCache" />)
    ///     a wildcard/regex-kind entry's own pattern and matches it against the queried name,
    ///     never letting a compile failure or a match timeout escape as an exception -- both are
    ///     treated as "no match," logged once each time they occur.
    /// </summary>
    /// <param name="kind">The entry's own kind, used only as part of the cache key.</param>
    /// <param name="rawPattern">The entry's own, as-stored pattern text.</param>
    /// <param name="queriedPresentation">The already-normalized queried name to match against.</param>
    /// <param name="toRegexPattern">
    ///     The function that turns <paramref name="rawPattern" /> into the actual regex pattern
    ///     text to compile -- the identity function for a genuine <see cref="DnsFilterEntryKindEnum.Regex" />
    ///     entry, or <see cref="BuildWildcardRegexPattern" /> for a
    ///     <see cref="DnsFilterEntryKindEnum.Wildcard" /> one.
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
                    // Logged once (the GetOrAdd factory only ever runs on the first lookup for a
                    // given (kind, pattern) key, and the null result is cached exactly like a
                    // successful compile would be) -- expected only for a row written by some
                    // path other than FilterListEntriesController's own compile-before-persist
                    // validation, see this class's own doc comment.
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
            // A pathological/catastrophic-backtracking pattern must never be able to stall query
            // answering -- treated exactly like "no match," never thrown back into
            // DnsQueryProcessingPipeline (architectural invariant #3).
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
    ///     This method compiles a regex pattern with this engine's own shared timeout, returning
    ///     <see langword="null" /> (logged) instead of throwing when the pattern is malformed --
    ///     a defensive fallback for a row written by some path other than
    ///     <c>FilterListEntriesController</c>'s own compile-before-persist validation (a direct
    ///     repository call, an import tool), never expected to trigger for a REST-created entry.
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
    ///     This method translates a shell-style glob (<c>*</c> matches any run of zero or more
    ///     characters, <c>?</c> matches exactly one character) into an anchored, case-insensitive
    ///     regex pattern -- every other regex metacharacter in the glob is escaped literally
    ///     first, so a wildcard entry's own author never has to think about regex syntax at all.
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
