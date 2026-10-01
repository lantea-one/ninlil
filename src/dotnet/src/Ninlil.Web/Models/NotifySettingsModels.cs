// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the NOTIFY settings (<c>NotifyOptions</c>) that
///     <c>GET /api/notify-settings</c> returns: how the server sends RFC 1996 NOTIFY messages to a
///     zone's secondaries. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="MaxAttempts">How many times one NOTIFY is sent before giving up. Defaults to 5.</param>
/// <param name="ResponseTimeout">How long to wait for a secondary's reply to one attempt. Defaults to 3 seconds.</param>
/// <param name="RetryDelay">How long to wait between attempts. Defaults to 3 seconds.</param>
/// <param name="SecondaryPort">The port NOTIFY messages are sent to. Defaults to 53.</param>
public record NotifySettingsDetail(
    int MaxAttempts,
    TimeSpan ResponseTimeout,
    TimeSpan RetryDelay,
    int SecondaryPort
);
