using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a person or service account that can sign in to Ninlil. Its fields
///     follow what ASP.NET Core Identity expects of a user, so <c>NinlilDnsUserStore</c> in
///     <c>Ninlil.Dns.Identity</c> can expose it through <c>IUserStore&lt;TUser&gt;</c> and the
///     related interfaces without a second copy of the data.
/// </summary>
public class UserEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the sign-in name, as the user typed it.
    /// </summary>
    public string UserName { get; set; }

    /// <summary>
    ///     This property contains <see cref="UserName" />, upper-invariant, so sign-in lookups can be
    ///     indexed and compared without depending on the database collation.
    /// </summary>
    public string NormalizedUserName { get; set; }

    /// <summary>
    ///     This property contains the display name shown for the user in the admin console,
    ///     separate from the sign-in name.
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    ///     This property contains the user's e-mail address, used for notifications and for
    ///     e-mail-based two-factor authentication.
    /// </summary>
    public string Email { get; set; }

    /// <summary>
    ///     This property contains <see cref="Email" />, upper-invariant, for the same reason as
    ///     <see cref="NormalizedUserName" />.
    /// </summary>
    public string NormalizedEmail { get; set; }

    /// <summary>
    ///     This property indicates whether the user has confirmed <see cref="Email" /> with a link or
    ///     code.
    /// </summary>
    public bool EmailConfirmed { get; set; }

    /// <summary>
    ///     This property contains the salted password hash. The plain-text password is never
    ///     stored.
    /// </summary>
    public string PasswordHash { get; set; }

    /// <summary>
    ///     This property contains a random value that changes whenever the user's credentials change
    ///     (password, e-mail, and so on), so a token that embeds the old stamp fails its next
    ///     check.
    /// </summary>
    public string SecurityStamp { get; set; }

    /// <summary>
    ///     This property contains a random value used to detect two concurrent updates of the same
    ///     user, so the second write is rejected instead of overwriting the first.
    /// </summary>
    public string ConcurrencyStamp { get; set; }

    /// <summary>
    ///     This property contains the user's phone number. It is part of the Identity user shape;
    ///     Ninlil does not send SMS.
    /// </summary>
    public string PhoneNumber { get; set; }

    /// <summary>
    ///     This property indicates whether <see cref="PhoneNumber" /> has been verified.
    /// </summary>
    public bool PhoneNumberConfirmed { get; set; }

    /// <summary>
    ///     This property indicates whether the user must supply a second authentication factor
    ///     (TOTP or e-mail) in addition to their password.
    /// </summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>
    ///     This property contains the encrypted TOTP (RFC 6238) shared secret that validates codes
    ///     from the user's authenticator app. It is <see langword="null" /> until the user enrolls an
    ///     authenticator.
    /// </summary>
    public string TwoFactorTotpSecretEncrypted { get; set; }

    /// <summary>
    ///     This property contains the user's single-use two-factor recovery codes, joined with
    ///     semicolons and encrypted as one value, as the default ASP.NET Core Identity
    ///     <c>UserStore</c> does (Ninlil's store has no general token store to put them in).
    ///     <c>NinlilDnsUserStore.ReplaceCodesAsync</c> replaces it when a new batch is generated, and
    ///     it is rewritten without the redeemed code each time one is used. It is
    ///     <see langword="null" /> until a batch has been generated.
    /// </summary>
    public string TwoFactorRecoveryCodesEncrypted { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time at which an active lockout ends, or
    ///     <see langword="null" /> if the user is not currently locked out.
    /// </summary>
    public DateTimeOffset? LockoutEndUtc { get; set; }

    /// <summary>
    ///     This property indicates whether this user account can be locked out after too many
    ///     failed sign-in attempts.
    /// </summary>
    public bool LockoutEnabled { get; set; }

    /// <summary>
    ///     This property contains the number of consecutive failed sign-in attempts since the
    ///     last successful one, used to decide when to trigger a lockout.
    /// </summary>
    public int AccessFailedCount { get; set; }

    /// <summary>
    ///     This property indicates whether an administrator has disabled this account. A disabled
    ///     account is rejected at sign-in even with correct credentials.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains this user's color palette for the admin console.
    ///     <see langword="null" /> (the default) uses the host default,
    ///     <c>AppearanceOptions.Palette</c>.
    /// </summary>
    public UiThemePaletteEnum? PreferredPalette { get; set; }

    /// <summary>
    ///     This property contains this user's light/dark mode for the admin console.
    ///     <see langword="null" /> (the default) uses the host default, <c>AppearanceOptions.Mode</c>.
    /// </summary>
    public UiThemeModeEnum? PreferredMode { get; set; }

    /// <summary>
    ///     This property names the external identity provider this account was provisioned
    ///     through. The only value is <c>"Oidc"</c>, which <c>OidcUserProvisioningService</c> writes
    ///     on the account's first OpenID Connect sign-in. It is <see langword="null" /> for local
    ///     accounts. It is separate from <see cref="ExternalSubjectId" /> so another provider kind can
    ///     be added without changing what either field means.
    /// </summary>
    public string ExternalIdentityProvider { get; set; }

    /// <summary>
    ///     This property contains the external identity provider's immutable <c>sub</c> (subject)
    ///     claim for this account. <c>OidcUserProvisioningService</c> finds the account by it on
    ///     every later sign-in, never by e-mail address, which a provider can reassign.
    ///     <see langword="null" /> for local accounts, like <see cref="ExternalIdentityProvider" />.
    /// </summary>
    public string ExternalSubjectId { get; set; }

    /// <summary>
    ///     This property is <see langword="null" /> for an ordinary account. Cluster identity
    ///     federation sets it when two nodes created an account with the same
    ///     <see cref="UserName" /> during a network partition and this account lost the
    ///     last-write-wins tie-break (<c>ClusterSyncConflictResolver.IncomingWins</c>). The losing
    ///     account is not dropped or merged: it is renamed with a
    ///     <c>-conflict-&lt;shortnodeid&gt;</c> suffix and this field explains what happened and
    ///     when. The REST API and the console's user list show it as
    ///     <c>UserSummary.ClusterConflictNote</c>. Only an administrator editing the account clears
    ///     it.
    /// </summary>
    public string ClusterConflictNote { get; set; }
}
