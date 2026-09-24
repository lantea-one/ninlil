using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a DNS rewrite
///     (<c>POST</c>/<c>PUT /api/dns-rewrites</c>).
/// </summary>
/// <param name="Domain">
///     The queried domain this rewrite matches -- an exact name with no <c>*</c> character, or a
///     shell-style wildcard glob (<c>*.example.com</c>) with one. Validated as a syntactically
///     valid domain name before this rewrite is ever persisted.
/// </param>
/// <param name="Target">
///     Either a literal IP address (an A/AAAA-style rewrite) or another domain name (a
///     CNAME-style rewrite) -- validated against the real
///     <c>DnsRewriteTargetClassifier.TryClassify</c> before this rewrite is ever persisted, the
///     same "validate against the real codec/parser" discipline <see cref="RecordsController" />
///     already applies to a record's own <c>data</c> field.
/// </param>
/// <param name="IsDisabled">Whether the rewrite starts out (or is currently) disabled.</param>
/// <param name="Comments">Free-form operator notes about this rewrite.</param>
public record DnsRewriteWriteRequest(
    string Domain,
    string Target,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a single DNS rewrite's own fields, for list, single-item, and creation
///     responses. No secret material is involved anywhere in this entity, so, unlike
///     <c>TsigKeyDetail</c>/<c>DnssecKeyDetail</c>/<c>CloudProviderCredentialDetail</c>/
///     <c>WebhookSubscriptionDetail</c>, there is no reveal-once policy to apply here at all.
/// </summary>
/// <param name="Id">This rewrite's own identifier.</param>
/// <param name="Domain">This rewrite's own domain pattern, exactly as stored.</param>
/// <param name="Target">This rewrite's own target, exactly as stored.</param>
/// <param name="TargetKind">
///     The record kind <see cref="Target" /> classifies to -- computed fresh at read time via
///     the same classifier used to validate it at write time, purely as a read-only convenience,
///     the same "compute a read-only classification fresh at read time" convention
///     <c>ForwarderDetail.Protocol</c> already establishes.
/// </param>
/// <param name="IsDisabled">Whether this rewrite is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about this rewrite.</param>
/// <param name="CreatedAtUtc">When this rewrite was created.</param>
/// <param name="UpdatedAtUtc">When this rewrite was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node this rewrite was last created or modified on (the cluster's own
///     operational config sync), or <see langword="null" /> on a deployment that has never had clustering
///     enabled.
/// </param>
/// <param name="ClientId">
///     The single known client (<c>Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity</c>) this
///     rewrite is scoped to, or <see langword="null" /> if it is unscoped (global) or scoped to a
///     client group instead -- read-only here: the ordinary create/replace/patch actions on this
///     controller never set it, since the only real write path that ever scopes a rewrite is
///     <c>SafeSearchController.Enable</c>'s own optional target. Surfaced so an operator browsing
///     the ordinary Rewrites page can tell a Safe-Search-scoped row apart from a global one, rather
///     than seeing what would otherwise look like an unexplained, untargetable duplicate.
/// </param>
/// <param name="ClientGroupId">
///     The single client group this rewrite is scoped to, or <see langword="null" /> if it is
///     unscoped or scoped to one specific client instead -- the same read-only, informational
///     purpose <see cref="ClientId" /> serves.
/// </param>
public record DnsRewriteDetail(
    Guid Id,
    string Domain,
    string Target,
    DnsRewriteTargetKindEnum? TargetKind,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? OriginatingNodeId = null,
    Guid? ClientId = null,
    Guid? ClientGroupId = null
);
