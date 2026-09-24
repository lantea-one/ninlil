using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnsFilterOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record FilteringSettingsDetail(
    bool Enable,
    DnsFilterBlockResponseModeEnum BlockResponseMode,
    string SinkholeAddressV4,
    string SinkholeAddressV6,
    uint SinkholeTimeToLiveSeconds,
    DnsFilterListPrecedenceEnum Precedence,
    bool BlockCnameCloaking
);
