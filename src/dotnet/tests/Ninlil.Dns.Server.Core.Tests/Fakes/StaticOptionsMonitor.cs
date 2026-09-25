using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Core.Tests.Fakes;

/// <summary>
///     This class is a test <see cref="IOptionsMonitor{TOptions}" /> that always returns the same
///     <see cref="CurrentValue" /> and never raises <see cref="OnChange" />. Tests use it to build
///     <see cref="Ninlil.Dns.Server.Core.Pipeline.DnsQueryProcessingPipeline" /> and other
///     services with fixed options.
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
