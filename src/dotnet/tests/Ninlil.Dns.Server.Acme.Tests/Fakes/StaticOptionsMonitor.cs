using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests.Fakes;

/// <summary>
///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always reports the same
///     <see cref="CurrentValue" /> and never calls <see cref="OnChange" /> listeners, like the
///     fakes of the same name in other test projects. <c>CaaRecordReconciliationServiceTests</c>
///     uses it to give the service fixed settings without the configuration system.
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
