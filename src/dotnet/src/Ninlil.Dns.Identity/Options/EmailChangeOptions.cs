// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Identity:EmailChange</c> section of the application's
///     configuration and holds the setting the self-service e-mail change reads when a signed-in
///     user asks to change their own e-mail address on the profile page. It is read through
///     <c>IOptionsMonitor{EmailChangeOptions}</c>, so a change in the configuration file applies to
///     the next request without a restart.
/// </summary>
public sealed class EmailChangeOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Identity:EmailChange";

    /// <summary>
    ///     This property controls whether a new address must be confirmed before it applies. When
    ///     <see langword="true" /> (the default), a link goes to the new address and the change
    ///     applies when the user opens it, which proves the user controls that mailbox. When
    ///     <see langword="false" />, the change applies at once and no mail is sent.
    /// </summary>
    public bool RequireConfirmation { get; set; } = true;
}
