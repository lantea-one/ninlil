using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents one option a <see cref="DhcpTagRuleEntity" /> sends when it
///     matches: a raw tag and value, applied like a <see cref="DhcpScopeCustomOptionEntity" />.
///     See <see cref="DhcpTagRuleEntity" /> for how options from several matching rules combine.
///     For a DHCPv6 request the tag is used as the DHCPv6 option code, so only codes 0-255 can
///     be set.
/// </summary>
public class DhcpTagRuleOptionEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DhcpTagRuleEntity" /> this
    ///     option belongs to.
    /// </summary>
    public Guid TagRuleId { get; set; }

    /// <summary>
    ///     This property contains the option tag (0-255), as in
    ///     <see cref="DhcpScopeCustomOptionEntity.OptionTag" />.
    /// </summary>
    public byte OptionTag { get; set; }

    /// <summary>
    ///     This property contains the option value as an uppercase hexadecimal string, as in
    ///     <see cref="DhcpScopeCustomOptionEntity.ValueHex" />; it is ignored (and may be blank)
    ///     when <see cref="ValueSource" /> is <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
    /// </summary>
    public string ValueHex { get; set; }

    /// <summary>
    ///     This property indicates where the delivered value comes from, as in
    ///     <see cref="DhcpScopeCustomOptionEntity.ValueSource" />. The default is
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" />.
    /// </summary>
    public DhcpOptionValueSourceEnum ValueSource { get; set; } = DhcpOptionValueSourceEnum.Custom;
}
