// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a shared TSIG (RFC 8945) key used to authenticate zone transfers and
///     dynamic updates. An operator creates one through the admin console or API for each
///     trusted secondary server or update client.
/// </summary>
public class TsigKeyEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the key's name, in domain-name presentation form (it need not
    ///     resolve). It is matched against the key name in a signed message.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the MAC algorithm's name, in domain-name presentation form.
    ///     It is always <c>hmac-sha256.</c>, since Ninlil implements only HMAC-SHA256, but it is a
    ///     column so another algorithm can be added without a schema change.
    /// </summary>
    public string AlgorithmName { get; set; }

    /// <summary>
    ///     This property contains the shared secret, encrypted at rest. Unlike a password or API
    ///     token (see <see cref="ApiTokenEntity.TokenHash" />) it cannot be a one-way hash, because
    ///     computing a MAC needs the secret itself.
    /// </summary>
    public string SecretEncrypted { get; set; }

    /// <summary>
    ///     This property indicates whether the key is disabled. A disabled key is kept but treated as
    ///     unknown: signing or verifying with it fails with a TSIG BADKEY error.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the key, such as which secondary
    ///     server or update client it was issued to.
    /// </summary>
    public string Comments { get; set; }
}
