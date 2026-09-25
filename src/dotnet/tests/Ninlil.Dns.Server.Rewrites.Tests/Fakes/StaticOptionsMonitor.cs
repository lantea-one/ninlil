using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Rewrites.Tests.Fakes;

/// <summary>
///     This class is a fixed <see cref="IOptionsMonitor{TOptions}" />: it always reports the same
///     <see cref="CurrentValue" /> and never fires <see cref="OnChange" />. Tests use it to build a
///     <see cref="DnsRewriteEngine" /> with fixed <c>DnsRewriteOptions</c> without the
///     configuration system.
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
