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
///     This class implements <see cref="IDhcpv6OptionOverlayService" /> on the tag rule
///     repositories, the counterpart of <see cref="Dhcpv4OptionOverlayService" />. It applies the
///     scope's enabled custom options first (the option code is <see cref="DhcpScopeCustomOptionEntity.OptionTag" />;
///     a custom option is sent whether or not the client's Option Request Option names it, as
///     RFC 8415 Section 18.3 permits a server to send options the client did not request, and
///     it replaces a standard option with the same code, such as 23), then the options of every
///     enabled, eligible tag rule whose criteria the request meets, in the order described on
///     <see cref="DhcpTagRuleEntity" />, so the last rule to set a code wins and a tag rule beats
///     a custom option. Disabled custom options are not read. Only these criteria are checked:
///     <see cref="DhcpTagRuleEntity.VendorClassIdentifierMatch" /> and
///     <see cref="DhcpTagRuleEntity.UserClassMatch" /> (against the DHCPv6 options),
///     <see cref="DhcpTagRuleEntity.VendorClassEnterpriseNumber" />,
///     <see cref="DhcpTagRuleEntity.RelayInterfaceIdHex" /> and
///     <see cref="DhcpTagRuleEntity.RelayAgentRemoteIdHex" /> (against the RFC 4649 Remote-ID).
///     A rule's tag is used as the DHCPv6 option code.
///     <para>
///         Only rules of the scope itself apply. A global rule (no scope) is a DHCPv4 rule and
///         is ignored here, because its option tags are DHCPv4 tags and would be written as
///         DHCPv6 option codes. A rule that sets
///         <see cref="DhcpTagRuleEntity.RelayAgentCircuitIdHex" />, a DHCPv4-only criterion,
///         never matches.
///     </para>
///     <para>
///         An option with value source <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///         ignores <c>ValueHex</c> and carries the IPv6 address (16 bytes unless the catalog makes the
///         code a text option) from
///         <paramref name="serverAddressProvider" /> (see
///         <see cref="Identity.Dhcpv6ServerAddressProvider" />), not the DUID from
///         <see cref="IDhcpv6ServerIdentifierProvider" />, which is not an address.
///     </para>
/// </summary>
/// <param name="customOptionRepository">Used to read a scope's enabled custom options.</param>
/// <param name="tagRuleRepository">Used to read the tag rules eligible for a scope.</param>
/// <param name="tagRuleOptionRepository">Used to read the options a matching rule contributes.</param>
/// <param name="serverAddressProvider">
///     Supplies the server's IPv6 address for a <see cref="DhcpOptionValueSourceEnum.ServerAddress" />
///     option.
/// </param>
/// <param name="logger">Reports a stored option that fails <see cref="DhcpRawOptionRules" />.</param>
public sealed class Dhcpv6OptionOverlayService(
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IDhcpv6ServerAddressProvider serverAddressProvider,
    ILogger<Dhcpv6OptionOverlayService> logger
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
        // No scope for this exchange: no custom option or rule is eligible, as
        // Dhcpv6OptionDeliveryService also skips scope options.
        if (scope is null)
            return;

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

        // The repository also returns global rules; those belong to DHCPv4 (see the class
        // summary), so only the scope's own rules are evaluated here.
        IEnumerable<DhcpTagRuleEntity> matchingRulesInOrder = candidateRules
            .Where(rule => rule.ScopeId == scope.Id && Matches(rule, request))
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
    ///     This method reports whether a request meets all of a rule's DHCPv6 criteria (combined
    ///     with AND), like the <c>Matches</c> method of <see cref="Dhcpv4OptionOverlayService" />.
    ///     Unset criteria are not checked, so a rule with no criteria matches every eligible
    ///     DHCPv6 request. The DHCPv4-only <see cref="DhcpTagRuleEntity.RelayAgentCircuitIdHex" />
    ///     cannot be evaluated for a DHCPv6 request, so a rule that sets it never matches.
    ///     Ignoring it would widen the rule to every client.
    /// </summary>
    private static bool Matches(DhcpTagRuleEntity rule, Dhcpv6Message request)
    {
        // The API refuses this on a DHCPv6 rule; this guard covers rows stored before it did.
        if (!string.IsNullOrEmpty(rule.RelayAgentCircuitIdHex))
            return false;

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
            // An enterprise number alone means "any vendor class from this enterprise", so it is
            // checked even without VendorClassIdentifierMatch.
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
    ///     This method compares a client-supplied value with a configured one, the same logic as
    ///     <c>MatchesText</c> in <see cref="Dhcpv4OptionOverlayService" />, kept here so it reads
    ///     next to this class's <c>Matches</c>.
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
    ///     This method puts one option on the reply, like the helper of the same name in
    ///     <see cref="Dhcpv4OptionOverlayService" />, with the 16-byte IPv6 address for a
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> option (in the form
    ///     <see cref="Dhcpv6OptionCatalog.EncodeServerAddress" /> gives the code) and invalid hex
    ///     skipped. An option that fails <see cref="DhcpRawOptionRules.TryValidateDhcpv6" /> (this
    ///     includes the codes the engine writes itself, such as 2 Server Identifier) or
    ///     <see cref="DhcpRawOptionRules.TryValidateDhcpv6Value" /> is skipped with one warning.
    /// </summary>
    private void ApplyRawOption(
        Dhcpv6Message reply,
        string source,
        byte tag,
        string valueHex,
        DhcpOptionValueSourceEnum valueSource
    )
    {
        byte[] value;

        if (valueSource == DhcpOptionValueSourceEnum.ServerAddress)
            value = Dhcpv6OptionCatalog.EncodeServerAddress(tag, serverAddressProvider.Value);
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
            !DhcpRawOptionRules.TryValidateDhcpv6(tag, value.Length, out string error)
            || !DhcpRawOptionRules.TryValidateDhcpv6Value(tag, value, out error)
        )
        {
            logger.LogWarning("Skipping {Source} option: {Error}", source, error);
            return;
        }

        reply.SetRawOption(tag, value);
    }
}
