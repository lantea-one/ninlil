using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Forwarding.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IOptionsMonitor{TOptions}" /> whose <see cref="CurrentValue" />
///     a test replaces with a new object, the way the configuration system hands out a new options
///     instance after a settings file changes. It never fires <see cref="OnChange" />.
/// </summary>
/// <param name="value">The first value.</param>
public sealed class SwappableOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    /// <inheritdoc />
    public TOptions CurrentValue { get; set; } = value;

    /// <inheritdoc />
    public TOptions Get(string name) => CurrentValue;

    /// <inheritdoc />
    public IDisposable OnChange(Action<TOptions, string> listener) => null;
}
