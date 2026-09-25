using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class replaces the framework's <see cref="IOptions{TOptions}" /> of
///     <see cref="IdentityOptions" /> and reads through a live <see cref="IOptionsMonitor{TOptions}" />
///     instead of returning a value fixed at first use.
///     <para>
///         <see cref="UserManager{TUser}" /> takes <see cref="IOptions{TOptions}" /> in its
///         constructor and reads <c>.Value</c> once. It is scoped, so that alone would be fine,
///         but the default <see cref="IOptions{TOptions}" /> is backed by a singleton
///         <c>OptionsManager</c> that computes its value once per process. A configuration-file
///         change, for example to <see cref="PasswordOptions.RequiredLength" />, would then never
///         reach any <see cref="UserManager{TUser}" /> until a restart, while
///         <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> does see it.
///     </para>
///     <para>
///         This class is registered after the framework's default (which uses <c>TryAdd</c>), so
///         it wins. Every new <see cref="UserManager{TUser}" />, which means every request, then
///         captures the current value. An instance created before a change keeps its snapshot,
///         but the next request gets a new one, so changes apply without a restart.
///     </para>
/// </summary>
/// <param name="monitor">The live monitor <see cref="Value" /> reads from.</param>
public sealed class IdentityOptionsMonitorProxy(IOptionsMonitor<IdentityOptions> monitor)
    : IOptions<IdentityOptions>
{
    /// <inheritdoc />
    public IdentityOptions Value => monitor.CurrentValue;
}
