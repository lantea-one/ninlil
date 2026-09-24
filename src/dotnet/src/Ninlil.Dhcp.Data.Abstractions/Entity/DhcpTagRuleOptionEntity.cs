using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single option a <see cref="DhcpTagRuleEntity" /> contributes when
///     it matches a request -- a raw option tag plus its raw value bytes, applied onto the reply
///     the same way <see cref="DhcpScopeCustomOptionEntity" /> is, but only when the owning rule's
///     own match criteria are satisfied. See <see cref="DhcpTagRuleEntity" />'s own doc comment for
///     the full evaluation-order/conflict-resolution rule governing how several rules' own options
///     combine when more than one matches the same client.
/// </summary>
public class DhcpTagRuleOptionEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="DhcpTagRuleEntity" /> this
    ///     option belongs to.
    /// </summary>
    public Guid TagRuleId { get; set; }

    /// <summary>
    ///     This property contains the option's own raw numeric tag (0-255), the same meaning
    ///     <see cref="DhcpScopeCustomOptionEntity.OptionTag" /> already documents.
    /// </summary>
    public byte OptionTag { get; set; }

    /// <summary>
    ///     This property contains the option's own raw value bytes, encoded as an uppercase
    ///     hexadecimal string, the same convention <see cref="DhcpScopeCustomOptionEntity.ValueHex" />
    ///     already documents, including that it is ignored entirely (and may be left blank) when
    ///     <see cref="ValueSource" /> is <see cref="DhcpOptionValueSourceEnum.ServerAddress" />.
    /// </summary>
    public string ValueHex { get; set; }

    /// <summary>
    ///     This property indicates where this option's own delivered value actually comes from --
    ///     the same meaning <see cref="DhcpScopeCustomOptionEntity.ValueSource" /> already
    ///     documents, defaulting the same way to <see cref="DhcpOptionValueSourceEnum.Custom" /> so
    ///     every row created before this property existed keeps delivering its own
    ///     already-configured <see cref="ValueHex" /> unchanged.
    /// </summary>
    public DhcpOptionValueSourceEnum ValueSource { get; set; } = DhcpOptionValueSourceEnum.Custom;
}
