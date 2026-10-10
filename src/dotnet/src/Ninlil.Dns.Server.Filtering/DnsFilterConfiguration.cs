using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Filtering;

/// <summary>
///     This class is one immutable copy of the filter configuration that decides which lists
///     apply to a client: the enabled groups with their networks and list assignments, the
///     clients' direct list assignments and the enabled lists. <see cref="DnsFilterConfigurationCache" />
///     builds it from the database and <see cref="DnsFilterEngine" /> reads it on every query.
///     It holds no list entries; those are cached per list as <see cref="DnsFilterListIndex" />.
/// </summary>
public sealed class DnsFilterConfiguration
{
    /// <summary>
    ///     This field is the configuration used before the first load succeeds: no groups, no lists.
    /// </summary>
    public static readonly DnsFilterConfiguration Empty = new(
        [],
        null,
        new Dictionary<Guid, DnsFilterGroup>(),
        new Dictionary<Guid, Guid[]>(),
        new Dictionary<Guid, DnsFilterList>()
    );

    /// <summary>
    ///     This constructor wraps the already built parts.
    /// </summary>
    public DnsFilterConfiguration(
        IReadOnlyList<DnsFilterGroup> matchableGroups,
        DnsFilterGroup defaultGroup,
        IReadOnlyDictionary<Guid, DnsFilterGroup> enabledGroups,
        IReadOnlyDictionary<Guid, Guid[]> clientListIds,
        IReadOnlyDictionary<Guid, DnsFilterList> lists
    )
    {
        MatchableGroups = matchableGroups;
        DefaultGroup = defaultGroup;
        EnabledGroups = enabledGroups;
        ClientListIds = clientListIds;
        Lists = lists;
    }

    /// <summary>
    ///     This property is the enabled groups that are not the default group; a client joins one
    ///     by network, ClientID label or registered client.
    /// </summary>
    public IReadOnlyList<DnsFilterGroup> MatchableGroups { get; }

    /// <summary>
    ///     This property is the enabled default group, or <see langword="null" /> when there is
    ///     none or it is disabled. Its lists apply when no other group matches.
    /// </summary>
    public DnsFilterGroup DefaultGroup { get; }

    /// <summary>
    ///     This property is every enabled group by identifier, the default group included, for
    ///     the group a registered client belongs to.
    /// </summary>
    public IReadOnlyDictionary<Guid, DnsFilterGroup> EnabledGroups { get; }

    /// <summary>
    ///     This property is the list identifiers assigned directly to a registered client, by
    ///     client identifier.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid[]> ClientListIds { get; }

    /// <summary>
    ///     This property is every enabled list by identifier.
    /// </summary>
    public IReadOnlyDictionary<Guid, DnsFilterList> Lists { get; }
}

/// <summary>
///     This record is one enabled client group as the engine needs it.
/// </summary>
/// <param name="Id">The group's identifier.</param>
/// <param name="Networks">The group's CIDR and ClientID rows.</param>
/// <param name="ListIds">The lists assigned to the group.</param>
public sealed record DnsFilterGroup(
    Guid Id,
    IReadOnlyList<DnsFilterGroupNetwork> Networks,
    IReadOnlyList<Guid> ListIds
);

/// <summary>
///     This record is one network row of a group.
/// </summary>
/// <param name="Cidr">The address or CIDR network, or <see langword="null" /> on a ClientID row.</param>
/// <param name="ClientId">The DNS-over-HTTPS ClientID label, or <see langword="null" /> on a CIDR row.</param>
public sealed record DnsFilterGroupNetwork(string Cidr, string ClientId);

/// <summary>
///     This record is one enabled filter list without its entries.
/// </summary>
/// <param name="Id">The list's identifier.</param>
/// <param name="Kind">What the list's entries are (domains, client sources or response addresses).</param>
/// <param name="Action">Whether a match blocks or allows.</param>
public sealed record DnsFilterList(
    Guid Id,
    DnsFilterListKindEnum Kind,
    DnsFilterListActionEnum Action
);
