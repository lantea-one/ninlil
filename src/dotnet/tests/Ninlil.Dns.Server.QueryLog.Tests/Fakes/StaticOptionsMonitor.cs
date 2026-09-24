using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.QueryLog.Tests.Fakes;

/// <summary>
///     This class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />: it
///     always reports the same, fixed <see cref="CurrentValue" /> and never actually fires
///     <see cref="OnChange" />, which is all this project's own unit tests need to construct a
///     <see cref="DnsQueryLogSubscriber" /> against a fixed <see cref="QueryLoggingOptions" />
///     value without spinning up the real configuration-reload machinery -- mirrors
///     <c>Ninlil.Dns.Server.Rewrites.Tests</c>'s own identical fake.
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
