using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Authoritative.Tests.Fakes;

/// <summary>
///     This class is a test <see cref="IOptionsMonitor{TOptions}" /> that always returns the same
///     <see cref="CurrentValue" /> and never raises <see cref="OnChange" />, for tests that need
///     fixed options without configuration reloading.
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
