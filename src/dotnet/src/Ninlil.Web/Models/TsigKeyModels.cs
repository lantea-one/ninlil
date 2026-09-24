// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new TSIG key
///     (<c>POST /api/tsig-keys</c>).
/// </summary>
/// <param name="Name">The key's name, matched against a signed message's own key name.</param>
/// <param name="AlgorithmName">
///     The key's MAC algorithm. Ninlil implements only HMAC-SHA256; omitted or left blank,
///     this defaults to that algorithm.
/// </param>
/// <param name="Secret">
///     The key's own shared secret, base64-encoded. When omitted, a cryptographically random
///     32-byte secret is generated instead -- either way, the plaintext secret is only ever
///     shown once, in this endpoint's own response; every later request for this key omits it.
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
///     (<c>PUT /api/tsig-keys/{id}</c>). The key's own shared secret cannot be rotated this way --
///     delete and recreate the key instead -- since the reveal-once-on-creation exposure policy
///     has no later point at which a rotated secret could be shown again through a <c>PUT</c>'s
///     own no-body <c>204</c> response.
/// </summary>
/// <param name="Name">The key's name.</param>
/// <param name="AlgorithmName">The key's MAC algorithm.</param>
/// <param name="IsDisabled">Whether the key is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about the key.</param>
public record TsigKeyWriteRequest(
    string Name,
    string AlgorithmName,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a TSIG key's own metadata, for list, single-item, and (nested inside
///     <see cref="TsigKeyCreateResponse" />) creation responses. The key's own shared secret is
///     never included here -- see <see cref="TsigKeyCreateResponse" /> for the one place it ever
///     appears.
/// </summary>
public record TsigKeyDetail(
    Guid Id,
    string Name,
    string AlgorithmName,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries a newly created TSIG key's own metadata alongside its plaintext shared
///     secret -- the only response that will ever include <see cref="Secret" />, per this
///     project's reveal-once-then-omitted exposure policy for every secret-bearing entity type.
/// </summary>
/// <param name="Key">The newly created key's own metadata.</param>
/// <param name="Secret">The key's shared secret, base64-encoded.</param>
public record TsigKeyCreateResponse(TsigKeyDetail Key, string Secret);
