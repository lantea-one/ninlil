using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This class implements <see cref="IDhcpv4OptionOverlayService" /> against the real
///     repositories: every one of a scope's own enabled custom options, applied first (and
///     already winning over any standard option the same tag per
///     <see cref="DhcpScopeCustomOptionEntity" />'s own doc comment, simply by being applied after
///     <see cref="Dhcpv4OptionDeliveryService" /> already ran), then every enabled tag rule
///     eligible for the scope whose own match criteria the request satisfies, evaluated and
///     applied in <see cref="DhcpTagRuleEntity" />'s own documented priority order -- the last
///     matching rule to set a given tag is the one a client actually receives.
///     <para>
///         An option whose own <see cref="DhcpOptionValueSourceEnum" /> is
///         <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> ignores its stored
///         <c>ValueHex</c> entirely and instead delivers <paramref name="serverIdentifierProvider" />'s
///         own resolved IPv4 address -- the exact same address this server already presents as its
///         own Server Identifier option (RFC 2132 Section 9.7) in every reply, so "this server's
///         own address" always means the one address a DHCPv4 client already trusts this server to
///         identify itself by.
///     </para>
/// </summary>
/// <param name="customOptionRepository">Used to read a scope's own configured custom options.</param>
/// <param name="tagRuleRepository">Used to read the tag rules eligible for a scope.</param>
/// <param name="tagRuleOptionRepository">Used to read the options a matching rule contributes.</param>
/// <param name="serverIdentifierProvider">
///     Resolves this server's own IPv4 address for a <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///     option.
/// </param>
public sealed class Dhcpv4OptionOverlayService(
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IDhcpServerIdentifierProvider serverIdentifierProvider
) : IDhcpv4OptionOverlayService
{
    /// <inheritdoc />
    public async Task ApplyOverlaysAsync(
        Dhcpv4Message reply,
        DhcpScopeEntity scope,
        Dhcpv4Message request,
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<DhcpScopeCustomOptionEntity> customOptions =
            await customOptionRepository.ListEnabledByScopeIdAsync(scope.Id, cancellationToken);

        foreach (DhcpScopeCustomOptionEntity customOption in customOptions)
            ApplyRawOption(
                reply,
                customOption.OptionTag,
                customOption.ValueHex,
                customOption.ValueSource
            );

        IReadOnlyList<DhcpTagRuleEntity> candidateRules =
            await tagRuleRepository.ListEnabledForScopeAsync(scope.Id, cancellationToken);

        // Evaluate every eligible rule's own match criteria, then apply the matching ones' own
        // options in DhcpTagRuleEntity's own documented "last-applied-wins" evaluation order --
        // ascending Priority, ties broken by creation time then identifier, for a fully
        // deterministic total order.
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
    ///     This method reports whether a rule's own match criteria are all satisfied by a request,
    ///     per <see cref="DhcpTagRuleEntity" />'s own "combined with AND" doc comment -- a
    ///     criterion the rule leaves unset (null/empty) is simply not checked at all, including,
    ///     correctly, a rule with no criteria set that matches every request.
    /// </summary>
    private static bool Matches(DhcpTagRuleEntity rule, Dhcpv4Message request)
    {
        if (!string.IsNullOrEmpty(rule.VendorClassIdentifierMatch))
        {
            if (
                !request.TryGetVendorClassIdentifier(out string vendorClassIdentifier)
                || !MatchesText(
                    rule.VendorClassIdentifierMatch,
                    vendorClassIdentifier,
                    rule.VendorClassIdentifierMatchMode
                )
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

        if (!string.IsNullOrEmpty(rule.RelayAgentCircuitIdHex))
        {
            if (
                !request.TryGetRelayAgentCircuitId(out byte[] circuitId)
                || !string.Equals(
                    Convert.ToHexString(circuitId),
                    rule.RelayAgentCircuitIdHex,
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return false;
        }

        if (!string.IsNullOrEmpty(rule.RelayAgentRemoteIdHex))
        {
            if (
                !request.TryGetRelayAgentRemoteId(out byte[] remoteId)
                || !string.Equals(
                    Convert.ToHexString(remoteId),
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
    ///     configured value, per <see cref="Dhcpv4TagMatchModeEnum" />'s own doc comment -- always
    ///     an ordinal, case-sensitive comparison, since both the Vendor Class Identifier and User
    ///     Class options are opaque byte strings a client chooses for itself, not
    ///     culture-sensitive human text.
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
    ///     This method resolves an option's own value bytes and applies them to the reply. A
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> option ignores
    ///     <paramref name="valueHex" /> entirely and instead encodes this server's own resolved
    ///     IPv4 address (four raw bytes, the same wire shape any single-address DHCPv4 option
    ///     already uses -- see <see cref="System.Net.IPAddress.GetAddressBytes" />); a
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" /> option decodes its stored hexadecimal
    ///     value as before, silently skipping a value that fails to decode, since write-time
    ///     validation (a REST layer concern) is what should prevent a malformed value from ever
    ///     being persisted in the first place; this engine's own job is delivering already-valid
    ///     configuration, not re-validating it on every single request.
    /// </summary>
    private void ApplyRawOption(
        Dhcpv4Message reply,
        byte tag,
        string valueHex,
        DhcpOptionValueSourceEnum valueSource
    )
    {
        if (valueSource == DhcpOptionValueSourceEnum.ServerAddress)
        {
            reply.SetRawOption(tag, serverIdentifierProvider.Value.GetAddressBytes());
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
