using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This class implements <see cref="IDhcpv6OptionOverlayService" /> against the real
///     repositories -- the DHCPv6 analogue of <see cref="Dhcpv4OptionOverlayService" />. Every
///     enabled tag rule eligible for the scope whose own DHCPv6-relevant match criteria the
///     request satisfies is evaluated and applied in <see cref="DhcpTagRuleEntity" />'s own
///     documented priority order -- the last matching rule to set a given tag is the one a client
///     actually receives. A rule's own DHCPv4-only criteria
///     (<see cref="DhcpTagRuleEntity.RelayAgentCircuitIdHex" />) are never consulted here; only a
///     rule's <see cref="DhcpTagRuleEntity.VendorClassIdentifierMatch" />/
///     <see cref="DhcpTagRuleEntity.UserClassMatch" /> (both reused, unchanged in shape, from
///     their DHCPv4 meaning) and its DHCPv6-specific
///     <see cref="DhcpTagRuleEntity.VendorClassEnterpriseNumber" />/
///     <see cref="DhcpTagRuleEntity.RelayInterfaceIdHex" />/
///     <see cref="DhcpTagRuleEntity.RelayAgentRemoteIdHex" /> (the latter doing double duty as this
///     protocol's own RFC 4649 Remote-ID criterion too) are ever considered.
///     <para>
///         An option whose own <see cref="DhcpOptionValueSourceEnum" /> is
///         <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> ignores its stored
///         <c>ValueHex</c> entirely and instead delivers <paramref name="serverAddressProvider" />'s
///         own resolved IPv6 address -- deliberately <b>not</b> this server's own DHCPv6 DUID
///         (<see cref="IDhcpv6ServerIdentifierProvider" />), which is an opaque protocol
///         identifier with no usable-address meaning at all; see
///         <see cref="Identity.Dhcpv6ServerAddressProvider" />'s own doc comment for exactly how
///         this server's own real IPv6 address is resolved.
///     </para>
/// </summary>
/// <param name="tagRuleRepository">Used to read the tag rules eligible for a scope.</param>
/// <param name="tagRuleOptionRepository">Used to read the options a matching rule contributes.</param>
/// <param name="serverAddressProvider">
///     Resolves this server's own IPv6 address for a <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///     option.
/// </param>
public sealed class Dhcpv6OptionOverlayService(
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IDhcpv6ServerAddressProvider serverAddressProvider
) : IDhcpv6OptionOverlayService
{
    /// <inheritdoc />
    public async Task ApplyOverlaysAsync(
        Dhcpv6Message reply,
        DhcpScopeEntity scope,
        Dhcpv6Message request,
        CancellationToken cancellationToken = default
    )
    {
        // No scope resolved for this exchange at all -- nothing scope-eligible to look up, the
        // same tolerance Dhcpv6OptionDeliveryService's own scope-dependent options apply.
        if (scope is null)
            return;

        IReadOnlyList<DhcpTagRuleEntity> candidateRules =
            await tagRuleRepository.ListEnabledForScopeAsync(scope.Id, cancellationToken);

        IEnumerable<DhcpTagRuleEntity> matchingRulesInOrder = candidateRules
            .Where(rule => Matches(rule, request))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.CreatedAtUtc)
            .ThenBy(rule => rule.Id);

        foreach (DhcpTagRuleEntity rule in matchingRulesInOrder)
        {
            IReadOnlyList<DhcpTagRuleOptionEntity> ruleOptions =
                await tagRuleOptionRepository.ListByTagRuleIdAsync(rule.Id, cancellationToken);

            foreach (DhcpTagRuleOptionEntity ruleOption in ruleOptions)
                ApplyRawOption(
                    reply,
                    ruleOption.OptionTag,
                    ruleOption.ValueHex,
                    ruleOption.ValueSource
                );
        }
    }

    /// <summary>
    ///     This method reports whether a rule's own DHCPv6-relevant match criteria are all
    ///     satisfied by a request -- combined with AND, per <see cref="DhcpTagRuleEntity" />'s own
    ///     doc comment, exactly mirroring <see cref="Dhcpv4OptionOverlayService" />'s own
    ///     <c>Matches</c> method in shape. A criterion the rule leaves unset (null/empty) is simply
    ///     not checked at all -- including, correctly, a rule with no DHCPv6-relevant criteria set
    ///     (whether because it configures only DHCPv4-only criteria, or none at all), which matches
    ///     every DHCPv6 request eligible for the rule's own scope.
    /// </summary>
    private static bool Matches(DhcpTagRuleEntity rule, Dhcpv6Message request)
    {
        if (!string.IsNullOrEmpty(rule.VendorClassIdentifierMatch))
        {
            if (
                !request.TryGetVendorClass(
                    out uint enterpriseNumber,
                    out IReadOnlyList<string> vendorClassData
                )
                || (
                    rule.VendorClassEnterpriseNumber is { } requiredEnterpriseNumber
                    && requiredEnterpriseNumber != enterpriseNumber
                )
                || !vendorClassData.Any(instance =>
                    MatchesText(
                        rule.VendorClassIdentifierMatch,
                        instance,
                        rule.VendorClassIdentifierMatchMode
                    )
                )
            )
                return false;
        }
        else if (rule.VendorClassEnterpriseNumber is { } enterpriseNumberOnly)
        {
            // An operator naming only an enterprise number, with no vendor-class-data text to
            // match, is still a real, meaningful criterion -- "any vendor class from this
            // enterprise" -- so this is checked independently of VendorClassIdentifierMatch above.
            if (
                !request.TryGetVendorClass(out uint actualEnterpriseNumber, out _)
                || actualEnterpriseNumber != enterpriseNumberOnly
            )
                return false;
        }

        if (!string.IsNullOrEmpty(rule.UserClassMatch))
        {
            if (
                !request.TryGetUserClasses(out IReadOnlyList<string> userClasses)
                || !userClasses.Any(userClass =>
                    MatchesText(rule.UserClassMatch, userClass, rule.UserClassMatchMode)
                )
            )
                return false;
        }

        if (!string.IsNullOrEmpty(rule.RelayInterfaceIdHex))
        {
            if (
                request.RelayInterfaceId is not { Length: > 0 } actualInterfaceId
                || !string.Equals(
                    Convert.ToHexString(actualInterfaceId),
                    rule.RelayInterfaceIdHex,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return false;
        }

        if (!string.IsNullOrEmpty(rule.RelayAgentRemoteIdHex))
        {
            if (
                request.RelayRemoteId is not { Length: > 0 } actualRemoteId
                || !string.Equals(
                    Convert.ToHexString(actualRemoteId),
                    rule.RelayAgentRemoteIdHex,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return false;
        }

        return true;
    }

    /// <summary>
    ///     This method compares an opaque, client-supplied text value against a rule's own
    ///     configured value -- byte-for-byte identical logic to
    ///     <see cref="Dhcpv4OptionOverlayService" />'s own private <c>MatchesText</c> method,
    ///     duplicated here rather than shared since both are small, stable, and each is more
    ///     naturally read alongside its own protocol-specific <c>Matches</c> method.
    /// </summary>
    private static bool MatchesText(
        string configured,
        string actual,
        Dhcpv4TagMatchModeEnum mode
    ) =>
        mode switch
        {
            Dhcpv4TagMatchModeEnum.Prefix => actual.StartsWith(
                configured,
                StringComparison.Ordinal
            ),
            _ => string.Equals(actual, configured, StringComparison.Ordinal),
        };

    /// <summary>
    ///     This method resolves an option's own value bytes and applies them to the reply -- the
    ///     DHCPv6 analogue of <see cref="Dhcpv4OptionOverlayService" />'s own identical helper,
    ///     substituting this server's own resolved IPv6 address (sixteen raw bytes) for a
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> option instead of a
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" /> option's own decoded hexadecimal value,
    ///     silently skipping a value that fails to decode, the same reasoning that helper
    ///     documents.
    /// </summary>
    private void ApplyRawOption(
        Dhcpv6Message reply,
        byte tag,
        string valueHex,
        DhcpOptionValueSourceEnum valueSource
    )
    {
        if (valueSource == DhcpOptionValueSourceEnum.ServerAddress)
        {
            reply.SetRawOption(tag, serverAddressProvider.Value.GetAddressBytes());
            return;
        }

        byte[] value;

        try
        {
            value = Convert.FromHexString(valueHex ?? string.Empty);
        }
        catch (FormatException)
        {
            return;
        }

        reply.SetRawOption(tag, value);
    }
}
