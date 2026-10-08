// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This exception reports that <see cref="IOidcUserProvisioningService" /> refuses an OpenID
///     Connect sign-in even though the provider authenticated the user, for example because the
///     matching local account is disabled or locked out, or because a new account would reuse an
///     e-mail address that belongs to another account. The <c>OnTokenValidated</c> handler in
///     <c>ServiceCollectionAuthenticationExtension</c> logs it and sends the browser back to the
///     login page with the same error flag as any other failed provider flow.
/// </summary>
/// <param name="message">The reason, for the server log only. It is never shown to the user.</param>
public sealed class OidcSignInRefusedException(string message) : Exception(message);
