// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new TSIG key
///     (<c>POST /api/tsig-keys</c>).
/// </summary>
/// <param name="Name">
///     The key's name, matched against the key name in a signed message. It is stored and returned
///     in lowercase, because DNS names compare without case and BIND lowercases key names on the
///     wire.
/// </param>
/// <param name="AlgorithmName">
///     The key's MAC algorithm: <c>hmac-sha1</c>, <c>hmac-sha256</c>, <c>hmac-sha384</c> or
///     <c>hmac-sha512</c>, with or without the trailing dot. It is stored without the dot, and
///     <c>hmac-sha256</c> is used when this is omitted or blank.
/// </param>
/// <param name="Secret">
///     The shared secret, base64-encoded. When omitted, a random secret as long as the
///     algorithm's MAC is generated (32 bytes for HMAC-SHA256).
///     Either way the plaintext secret is shown once, in this endpoint's response, and never
///     again.
/// </param>
/// <param name="IsDisabled">Whether the key starts out disabled.</param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record TsigKeyCreateRequest(
    string Name,
    string AlgorithmName = null,
    string Secret = null,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries the fields used to fully replace an existing TSIG key's metadata
///     (<c>PUT /api/tsig-keys/{id}</c>). The shared secret cannot be changed this way: a secret is
///     shown only when a key is created, and a <c>PUT</c> answers <c>204</c> with no body. To
///     rotate the secret, delete the key and create it again.
/// </summary>
/// <param name="Name">The key's name; stored in lowercase.</param>
/// <param name="AlgorithmName">
///     The key's MAC algorithm; required here. One of <c>hmac-sha1</c>, <c>hmac-sha256</c>,
///     <c>hmac-sha384</c> or <c>hmac-sha512</c>.
/// </param>
/// <param name="IsDisabled">Whether the key is disabled.</param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record TsigKeyWriteRequest(
    string Name,
    string AlgorithmName,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a TSIG key without its secret, as returned by the list and get
///     endpoints and nested in <see cref="TsigKeyCreateResponse" />, the one response that
///     includes the secret.
/// </summary>
/// <param name="Id">The key's identifier.</param>
/// <param name="Name">The key's name, in lowercase.</param>
/// <param name="AlgorithmName">The key's MAC algorithm, such as <c>hmac-sha256</c>.</param>
/// <param name="IsDisabled">Whether the key is disabled.</param>
/// <param name="IsClusterKey">
///     Whether clustering created the key (a peer's channel key, or an invite key not yet used).
///     Such a key can be disabled and commented but not renamed or deleted, and it never
///     signs or verifies zone transfers or updates.
/// </param>
/// <param name="Comments">Free-form operator notes about the key.</param>
/// <param name="CreatedAtUtc">When the key was created.</param>
/// <param name="UpdatedAtUtc">When the key was last changed, or null if never.</param>
public record TsigKeyDetail(
    Guid Id,
    string Name,
    string AlgorithmName,
    bool IsDisabled,
    bool IsClusterKey,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries a newly created TSIG key with its plaintext shared secret. It is the
///     only response that includes <see cref="Secret" />: like every secret-bearing entity, a
///     secret is revealed once at creation and omitted afterwards.
/// </summary>
/// <param name="Key">The new key, without its secret.</param>
/// <param name="Secret">The key's shared secret, base64-encoded.</param>
public record TsigKeyCreateResponse(TsigKeyDetail Key, string Secret);
