// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single pre-shared TSIG (RFC 8945) key, used to authenticate zone
///     transfers and dynamic updates. An operator configures these out-of-band, through the
///     admin UI/API, for each secondary server or update client that should be trusted.
/// </summary>
public class TsigKeyEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the key's name -- not necessarily a real, resolvable domain
    ///     name, but written in domain-name presentation form, matched against the key name a
    ///     signed message identifies itself with.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the MAC algorithm's name, in domain-name presentation form.
    ///     Always <c>hmac-sha256.</c> for now -- Ninlil implements only HMAC-SHA256 -- but
    ///     kept as its own column rather than assumed, so a future algorithm can be added without
    ///     a schema change.
    /// </summary>
    public string AlgorithmName { get; set; }

    /// <summary>
    ///     This property contains the shared secret, encrypted at rest. Unlike a password or API
    ///     token (see <see cref="ApiTokenEntity.TokenHash" />), this cannot be stored as a
    ///     one-way hash: computing a MAC requires the literal secret bytes back, not just a
    ///     means of comparing against them.
    /// </summary>
    public string SecretEncrypted { get; set; }

    /// <summary>
    ///     This property indicates whether the key is currently disabled. A disabled key is kept
    ///     on record but rejected as though it did not exist -- signing with it, or verifying
    ///     against it, both fail with a TSIG BADKEY error.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the key -- for example, which
    ///     secondary server or update client it was issued to.
    /// </summary>
    public string Comments { get; set; }
}
