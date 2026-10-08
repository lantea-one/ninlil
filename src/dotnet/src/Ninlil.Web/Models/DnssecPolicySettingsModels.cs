using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the DNSSEC algorithm policy
///     (<c>DnssecAlgorithmPolicyOptions</c>) that <c>GET /api/dnssec-policy-settings</c> returns:
///     which algorithms <c>IDnssecKeyManagementService.GenerateKeyAsync</c> may generate a new key
///     for. A <c>PATCH</c> to the same endpoint accepts the same field.
/// </summary>
/// <param name="AllowedAlgorithms">
///     The only algorithms a new key may use. An empty list (the default) allows every algorithm
///     <c>DnssecAlgorithmRegistry</c> supports except RSAMD5, DSA, RSASHA1, DSA-NSEC3-SHA1,
///     RSASHA1-NSEC3-SHA1 and ECC-GOST, which this server never signs with; a PATCH naming one
///     of them is rejected with 400. It limits only key generation, not which
///     algorithms are accepted when validating zones signed elsewhere.
/// </param>
public record DnssecPolicySettingsDetail(List<DnssecAlgorithmEnum> AllowedAlgorithms);
