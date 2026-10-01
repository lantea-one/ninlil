using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a DNS rewrite
///     (<c>POST /api/dns-rewrites</c>, <c>PUT /api/dns-rewrites/{id}</c>).
/// </summary>
/// <param name="Domain">
///     The queried domain the rewrite matches: an exact name, or a wildcard such as
///     <c>*.example.com</c>. It must be a valid domain name.
/// </param>
/// <param name="Target">
///     An IP address (answered as an A or AAAA record) or another domain name (answered as a
///     CNAME). It is checked with <c>DnsRewriteTargetClassifier.TryClassify</c>, the classifier
///     used at query time, much as <see cref="RecordsController" /> checks a record's
///     <c>data</c> with the record codec.
/// </param>
/// <param name="IsDisabled">Whether the rewrite is disabled.</param>
/// <param name="Comments">Free-form operator notes about the rewrite.</param>
public record DnsRewriteWriteRequest(
    string Domain,
    string Target,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a DNS rewrite, as returned by the list, get and write endpoints of
///     <c>/api/dns-rewrites</c>. Nothing in it is secret, so there is no reveal-once handling as
///     with <c>TsigKeyDetail</c> or <c>WebhookSubscriptionDetail</c>.
/// </summary>
/// <param name="Id">The rewrite's identifier.</param>
/// <param name="Domain">The rewrite's domain pattern, as stored.</param>
/// <param name="Target">The rewrite's target, as stored.</param>
/// <param name="TargetKind">
///     The record kind <see cref="Target" /> classifies to, computed when the response is built
///     with the classifier that validated it (as <c>ForwarderDetail.Protocol</c> is), or
///     <see langword="null" /> if it no longer classifies.
/// </param>
/// <param name="IsDisabled">Whether the rewrite is disabled.</param>
/// <param name="Comments">Free-form operator notes about the rewrite.</param>
/// <param name="CreatedAtUtc">When the rewrite was created.</param>
/// <param name="UpdatedAtUtc">When the rewrite was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node the rewrite was last created or modified on, used by cluster config sync,
///     or <see langword="null" /> if it was never written with clustering enabled.
/// </param>
/// <param name="ClientId">
///     The one known client (<c>Ninlil.Dns.Data.Abstractions.Entity.DnsClientEntity</c>) the
///     rewrite applies to, or <see langword="null" /> if it is global or scoped to a client
///     group. It is read-only here: this controller never sets it; only
///     <c>SafeSearchController.Enable</c> with a target does. It is shown so the Rewrites page can
///     tell a targeted Safe Search row from a global one that would otherwise look like a
///     duplicate.
/// </param>
/// <param name="ClientGroupId">
///     The one client group the rewrite applies to, or <see langword="null" /> if it is global or
///     scoped to a client. Read-only, like <see cref="ClientId" />.
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
