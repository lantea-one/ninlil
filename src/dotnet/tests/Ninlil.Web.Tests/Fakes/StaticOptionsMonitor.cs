using Microsoft.Extensions.Options;

namespace Ninlil.Web.Tests.Fakes;

/// <summary>
///     This class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />: it
///     always reports the same, fixed <see cref="CurrentValue" /> and never actually fires
///     <see cref="OnChange" />, which is all this project's own unit tests need to construct a
///     service directly against a fixed options value without spinning up the real
///     configuration-reload machinery. Duplicated at project scope here (rather than reused from
///     <c>AcmeSettingsControllerDirectTests</c>'s own private nested version) so more than one
///     test class in this project can share it.
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
