using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Authoritative.Tests.Fakes;

/// <summary>
///     This class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />: it
///     always reports the same, fixed <see cref="CurrentValue" /> and never actually fires
///     <see cref="OnChange" />, mirroring every sibling test project's own identically-named fake
///     (for example <c>Ninlil.Dns.Server.Acme.Tests.Fakes.StaticOptionsMonitor{TOptions}</c>)
///     -- the minimal double a unit test needs to exercise a class that reads live configuration
///     without spinning up the real configuration-reload machinery.
/// </summary>
/// <param name="value">The fixed value this monitor always reports.</param>
public sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    /// <inheritdoc />
    public TOptions CurrentValue { get; } = value;

    /// <inheritdoc />
    public TOptions Get(string name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable OnChange(Action<TOptions, string> listener) => null;
}
