// Define our namespace.
namespace Ninlil.Dns.Identity.Options;

/// <summary>
///     This class binds to the <c>Identity:EmailChange</c> section of the application's
///     configuration and holds the policy for a signed-in user changing their own e-mail address.
///     It is registered with <c>Configure</c>, so a reader that takes
///     <c>IOptionsMonitor{EmailChangeOptions}</c> sees a change in the configuration file without a
///     restart. No endpoint offers the self-service change yet, so nothing reads it today.
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
    ///     mailbox first; <see langword="false" /> means the change may apply at once.
    /// </summary>
    public bool RequireConfirmation { get; set; } = true;
}
