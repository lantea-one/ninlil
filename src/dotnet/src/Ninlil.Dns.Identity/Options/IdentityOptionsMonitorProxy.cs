using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class stands in for the ordinary, framework-registered <see cref="IOptions{TOptions}" />
///     of <see cref="IdentityOptions" />, delegating every read straight through to a live
///     <see cref="IOptionsMonitor{TOptions}" /> instead of returning a value snapshotted once.
///     <para>
///         <b>Why this exists.</b> <see cref="UserManager{TUser}" />'s own constructor takes an
///         <see cref="IOptions{TOptions}" /> of <see cref="IdentityOptions" /> (a fixed, framework
///         signature this project cannot change) and reads <c>.Value</c> exactly once, caching the
///         result in its own <c>Options</c> property for that instance's lifetime. That would be
///         harmless on its own, since <see cref="UserManager{TUser}" /> is registered scoped (a
///         fresh instance per request) -- except the plain <see cref="IOptions{TOptions}" /> the
///         framework would otherwise inject is itself backed by a *singleton*
///         <c>OptionsManager</c> whose own <c>.Value</c> is computed once, lazily, on first
///         access, and cached for the life of the process regardless of how many times or how
///         many different scopes later it is read again -- confirmed directly against a real
///         reload-on-change configuration file before relying on it (a freshly re-read
///         <see cref="IOptions{TOptions}" /> instance never observed a later file change at all,
///         while <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> did). A
///         configuration-file write changing, say, <see cref="PasswordOptions.RequiredLength" />
///         would therefore never have been observed by any <see cref="UserManager{TUser}" />
///         instance constructed after the very first one anywhere in the process, restart or not.
///     </para>
///     <para>
///         Registering this class as the process's own <see cref="IOptions{TOptions}" /> of
///         <see cref="IdentityOptions" /> (overriding the framework's own default registration,
///         which uses <c>TryAdd</c> and therefore loses to a later explicit registration for the
///         exact same closed generic type) makes every *newly constructed*
///         <see cref="UserManager{TUser}" /> -- which, being scoped, means every new HTTP request
///         -- capture the live value at that moment instead. This is not full mid-instance hot
///         reload (an already-constructed, long-lived <see cref="UserManager{TUser}" /> from
///         before a write would still hold its own, now-stale <c>Options</c> snapshot), but since
///         a fresh instance is constructed on the very next request regardless, this is
///         functionally equivalent to immediate, no-restart-required hot reload for every real
///         caller -- the same "re-read fresh per call" shape <c>ForwarderQueryResolver</c> already
///         establishes for a different reason.
///     </para>
/// </summary>
/// <param name="monitor">The live monitor this proxy's own <see cref="Value" /> always delegates to.</param>
public sealed class IdentityOptionsMonitorProxy(IOptionsMonitor<IdentityOptions> monitor)
    : IOptions<IdentityOptions>
{
    /// <inheritdoc />
    public IdentityOptions Value => monitor.CurrentValue;
}
