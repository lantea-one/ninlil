// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <c>TrustAnchorRolloverService</c> (Server.Recursive) changes
///     a root trust anchor's RFC 5011 state: a new key becomes <c>Valid</c> after its hold-down,
///     or an existing key is revoked. Webhooks can subscribe, so an operator hears about a root
///     KSK rollover that otherwise happens unattended. A new key entering <c>AddPend</c> does
///     not publish it, since nothing has changed yet.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the state change was recorded.</param>
/// <param name="KeyTag">The trust anchor's DNSKEY key tag.</param>
/// <param name="NewState">
///     The new RFC 5011 state as a name, such as <c>"Valid"</c> or <c>"Revoked"</c>. It is a
///     string because <c>Server.Core</c> must not reference <c>TrustAnchorStateEnum</c> in
///     <c>Server.Recursive</c>.
/// </param>
public record TrustAnchorRolloverEvent(DateTimeOffset OccurredAtUtc, ushort KeyTag, string NewState)
    : IDnsServerEvent;
