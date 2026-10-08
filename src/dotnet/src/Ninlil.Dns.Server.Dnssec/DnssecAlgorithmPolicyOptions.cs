using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Dnssec</c> section of the application's
///     configuration and sets which algorithms <see cref="DnssecKeyManagementService" /> may
///     generate keys for. It is edited through the DNSSEC policy settings endpoint and the
///     configuration presets.
/// </summary>
public sealed class DnssecAlgorithmPolicyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Dnssec";

    /// <summary>
    ///     This property contains the only algorithms <see cref="DnssecKeyManagementService.GenerateKeyAsync" />
    ///     will generate a new key for. Empty (the default) means every algorithm in
    ///     <see cref="Algorithms.DnssecAlgorithmRegistry" /> is allowed except the ones
    ///     <see cref="Algorithms.DnssecAlgorithmRegistry.IsSigningProhibited" /> names (1, 3, 5, 6,
    ///     7 and 12), which no policy can allow. The <c>FIPS</c> preset sets a FIPS-approved subset.
    ///     <para>
    ///         This limits key generation only. It does not affect validation of zones signed
    ///         elsewhere, whose algorithm this server does not choose.
    ///     </para>
    /// </summary>
    public List<DnssecAlgorithmEnum> AllowedAlgorithms { get; set; } = [];
}
