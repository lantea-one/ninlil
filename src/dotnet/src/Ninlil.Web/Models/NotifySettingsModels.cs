// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>NotifyOptions</c> -- how this server sends RFC 1996 NOTIFY messages to a zone's
///     configured secondaries. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record NotifySettingsDetail(
    int MaxAttempts,
    TimeSpan ResponseTimeout,
    TimeSpan RetryDelay,
    int SecondaryPort
);
