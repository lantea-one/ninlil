using Microsoft.Extensions.Logging;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.OptionCatalog;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Options;

/// <summary>
///     This class implements <see cref="IDhcpv4OptionOverlayService" /> on the custom option and
///     tag rule repositories. It applies the scope's enabled custom options first (they replace
///     standard options with the same tag, since <see cref="Dhcpv4OptionDeliveryService" /> has
///     already run), then the options of every enabled, eligible tag rule whose criteria the
///     request meets, in the order described on <see cref="DhcpTagRuleEntity" />, so the last rule
///     to set a tag wins. Only the DHCPv4 criteria are checked (vendor class, user class, Agent
///     Circuit ID, Agent Remote ID).
///     <para>
///         An option with value source <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///         ignores <c>ValueHex</c> and carries the address (text or 4 bytes, see
///         <c>ApplyRawOption</c>) from
///         <paramref name="serverIdentifierProvider" />, the same address the reply's Server
///         Identifier option (RFC 2132 Section 9.7) carries.
///     </para>
/// </summary>
/// <param name="customOptionRepository">Used to read a scope's custom options.</param>
/// <param name="tagRuleRepository">Used to read the tag rules eligible for a scope.</param>
/// <param name="tagRuleOptionRepository">Used to read the options a matching rule contributes.</param>
/// <param name="serverIdentifierProvider">
///     Supplies the server's IPv4 address for a <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///     option.
/// </param>
/// <param name="logger">Reports a stored option that fails <see cref="DhcpRawOptionRules" />.</param>
public sealed class Dhcpv4OptionOverlayService(
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IDhcpServerIdentifierProvider serverIdentifierProvider,
    ILogger<Dhcpv4OptionOverlayService> logger
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
                $"scope custom option {customOption.Id}",
                customOption.OptionTag,
                customOption.ValueHex,
                customOption.ValueSource
            );

        IReadOnlyList<DhcpTagRuleEntity> candidateRules =
            await tagRuleRepository.ListEnabledForScopeAsync(scope.Id, cancellationToken);

        // Apply matching rules by ascending Priority, then creation time, then identifier, so
        // the order is always the same and the last rule to set a tag wins.
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
                    $"tag rule {rule.Id} option {ruleOption.Id}",
                    ruleOption.OptionTag,
                    ruleOption.ValueHex,
                    ruleOption.ValueSource
                );
        }
    }

    /// <summary>
    ///     This method reports whether a request meets all of a rule's DHCPv4 criteria (combined
    ///     with AND). Unset criteria are not checked, so a rule with none matches every request.
    ///     The DHCPv6-only <see cref="DhcpTagRuleEntity.RelayInterfaceIdHex" /> and
    ///     <see cref="DhcpTagRuleEntity.VendorClassEnterpriseNumber" /> are ignored here.
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
    ///     This method compares a client-supplied value with a configured one per
    ///     <see cref="Dhcpv4TagMatchModeEnum" />, ordinally and case-sensitively, since vendor and
    ///     user class values are opaque strings, not human text.
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
    ///     This method puts one option on the reply, replacing any option with the same tag. A
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> option carries the server's IPv4
    ///     address in the form the catalog gives the tag
    ///     (<see cref="Dhcpv4OptionCatalog.EncodeServerAddress" />: dotted-quad text for a
    ///     <c>String</c> option such as 66, 4 raw bytes otherwise); a
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" /> option carries its decoded
    ///     <paramref name="valueHex" />, and is skipped if that is not valid hex, since validation
    ///     belongs to the REST layer when the value is written. An option whose tag or length fails
    ///     <see cref="DhcpRawOptionRules.TryValidateDhcpv4" /> or whose value fails
    ///     <see cref="DhcpRawOptionRules.TryValidateDhcpv4Value" /> (a row stored before the REST
    ///     layer enforced the rules) is skipped with one warning, because writing it would corrupt
    ///     or abort the reply.
    /// </summary>
    private void ApplyRawOption(
        Dhcpv4Message reply,
        string source,
        byte tag,
        string valueHex,
        DhcpOptionValueSourceEnum valueSource
    )
    {
        byte[] value;

        if (valueSource == DhcpOptionValueSourceEnum.ServerAddress)
            value = Dhcpv4OptionCatalog.EncodeServerAddress(tag, serverIdentifierProvider.Value);
        else
            try
            {
                value = Convert.FromHexString(valueHex ?? string.Empty);
            }
            catch (FormatException)
            {
                return;
            }

        if (
            !DhcpRawOptionRules.TryValidateDhcpv4(tag, value.Length, out string error)
            || !DhcpRawOptionRules.TryValidateDhcpv4Value(tag, value, out error)
        )
        {
            logger.LogWarning("Skipping {Source} option: {Error}", source, error);
            return;
        }

        reply.SetRawOption(tag, value);
    }
}
