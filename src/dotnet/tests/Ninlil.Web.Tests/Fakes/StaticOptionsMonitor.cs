using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Web.Tests.Fakes;

/// <summary>
///     This class is a fake <see cref="IOptionsMonitor{TOptions}" /> that always reports the same
///     <see cref="CurrentValue" /> and never fires <see cref="OnChange" />. Tests that build a
///     service directly use it to pass a fixed options value without the configuration reload
///     machinery. <c>AcmeSettingsControllerDirectTests</c> keeps a private copy of its own.
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
