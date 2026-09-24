using Microsoft.Extensions.Options;

namespace Ninlil.Dns.Server.Webhooks.Tests.Fakes;

/// <summary>
///     This class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />: it
///     always reports the same, fixed <see cref="CurrentValue" /> and never actually fires
///     <see cref="OnChange" />, which is all this project's own unit tests need to construct a
///     <see cref="Ninlil.Dns.Server.Webhooks.WebhookDeliveryService" /> against a fixed
///     <c>WebhookDeliveryOptions</c> value without spinning up the real configuration-reload
///     machinery.
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
