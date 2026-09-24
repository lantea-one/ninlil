// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>EdnsClientSubnetOptions</c>. No secret material is involved, so this record's own
///     fields are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record ClientSubnetSettingsDetail(
    bool Enable,
    byte SourcePrefixLengthIPv4,
    byte SourcePrefixLengthIPv6,
    string OverrideAddress
);
