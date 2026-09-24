using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Dnssec</c> section of the application's
///     configuration and controls <see cref="DnssecKeyManagementService" />'s own algorithm
///     policy.
/// </summary>
public sealed class DnssecAlgorithmPolicyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Dnssec";

    /// <summary>
    ///     This property contains the only algorithms <see cref="DnssecKeyManagementService.GenerateKeyAsync" />
    ///     will generate a new key for. Empty (the default) means no restriction at all --
    ///     every algorithm <see cref="Algorithms.DnssecAlgorithmRegistry" /> itself supports may
    ///     be used; a non-empty list is how the <c>FIPS</c> configuration preset narrows key
    ///     generation to a FIPS-approved subset.
    ///     <para>
    ///         This restricts only the *generation* of new signing keys, not validation of an
    ///         already-signed, externally-hosted zone's own algorithm choice -- a deliberate
    ///         scope cut, since a validator has no control over which algorithm a remote zone
    ///         chose to sign with.
    ///     </para>
    /// </summary>
    public List<DnssecAlgorithmEnum> AllowedAlgorithms { get; set; } = [];
}
