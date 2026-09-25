using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Recursive.Tests.Fakes;

/// <summary>
///     This class is a fixed IOptionsMonitor: it always reports the same CurrentValue and never
///     fires OnChange. Tests use it to build classes that read options live, such as
///     RecursiveQueryResolver and RecursiveCache, without the configuration system; mutating the
///     options object changes what it reports.
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
