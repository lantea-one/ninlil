// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever <c>Server.Recursive</c>'s own <c>TrustAnchorRolloverService</c>
///     changes a root trust anchor's own RFC 5011 state -- a brand-new candidate key promoted to
///     <c>Valid</c> after completing its hold-down period, or an existing anchor announcing its
///     own revocation -- a genuinely noteworthy, autonomous trust-management moment, so an
///     outbound webhook subscriber can be notified of it: this project's own homelab/set-and-forget deployment target
///     means a real root KSK rollover could otherwise happen with no operator ever finding out it
///     occurred at all. This event is deliberately not published for a brand-new candidate merely
///     entering its own <c>AddPend</c> hold-down period -- that is an observation, not yet a
///     completed rollover.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the state change was recorded.</param>
/// <param name="KeyTag">The trust anchor's own DNSKEY key tag.</param>
/// <param name="NewState">
///     The trust anchor's own new RFC 5011 state, as a short, human-readable name (for example,
///     <c>"Valid"</c> or <c>"Revoked"</c>) -- a plain string rather than a typed enum reference,
///     since <c>Server.Core</c> must not depend on <c>Server.Recursive</c>'s own
///     <c>TrustAnchorStateEnum</c>.
/// </param>
public record TrustAnchorRolloverEvent(DateTimeOffset OccurredAtUtc, ushort KeyTag, string NewState)
    : IDnsServerEvent;
