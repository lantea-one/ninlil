// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Identity:EmailChange</c> section of the application's
///     configuration and holds the policy for a signed-in user changing their own e-mail address.
///     It is registered with <c>Configure</c>, so a reader that takes
///     <c>IOptionsMonitor{EmailChangeOptions}</c> sees a change in the configuration file without a
///     restart. <c>POST /api/users/me/email</c> reads it on every request, and
///     <c>GET /api/users/me</c> and <c>GET /api/identity-settings</c> report it.
/// </summary>
public sealed class EmailChangeOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Identity:EmailChange";

    /// <summary>
    ///     This property states whether a new address must be confirmed before it applies:
    ///     <see langword="true" /> (the default) means the user must prove they control the new
    ///     mailbox first. Ninlil e-mails a link to the new address, which needs SMTP and the public
    ///     base URL, and the address changes only when the link is opened; the change also marks
    ///     the address confirmed. <see langword="false" /> changes the address at once and leaves
    ///     the account's confirmed state as it was, as an administrator's change does. A mistyped
    ///     address then receives the account's password-reset links, and e-mailed two-factor codes
    ///     stop reaching the user when the account's address was confirmed before.
    /// </summary>
    public bool RequireConfirmation { get; set; } = true;
}
