using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnssecAlgorithmPolicyOptions</c> -- which DNSSEC algorithms
///     <c>IDnssecKeyManagementService.GenerateKeyAsync</c> is willing to generate a brand-new
///     signing key for. No secret material is involved, so this record's own field is the exact
///     same one a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="AllowedAlgorithms">
///     The only algorithms a new key may use. An empty list (the default) means no restriction at
///     all -- every algorithm this server's own <c>DnssecAlgorithmRegistry</c> supports may be
///     used. This restricts only the *generation* of new signing keys, never the validation of an
///     already-signed, externally-hosted zone's own algorithm choice -- a known, deliberate scope
///     cut.
/// </param>
public record DnssecPolicySettingsDetail(List<DnssecAlgorithmEnum> AllowedAlgorithms);
