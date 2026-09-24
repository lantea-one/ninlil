using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class generates a strong, random password guaranteed to satisfy this project's own
///     configured password-complexity policy. Originally written inline inside
///     <c>AdministratorBootstrapper</c> for the bootstrap-administrator account's own initial
///     password; extracted here so the admin-driven "auto-generate and reveal once" password-reset
///     delivery mode (see <c>UsersController.ResetPassword</c>) can reuse the exact same, already-
///     proven generation logic instead of duplicating it.
/// </summary>
public static class PasswordGenerator
{
    /// <summary>
    ///     This method generates a new, random, 24-character password.
    /// </summary>
    public static string GenerateStrongRandomPassword()
    {
        // Built explicitly from four character classes, one guaranteed character from each, so
        // this deterministically satisfies this project's own configured password policy
        // (12+ characters, upper/lower/digit/non-alphanumeric) -- a plain random byte sequence
        // (base64 or otherwise) has a real, non-negligible chance of never producing a
        // non-alphanumeric character at all, which would fail that policy silently.
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*-_=+";
        const string all = upper + lower + digits + special;

        char[] password = new char[24];
        password[0] = PickRandom(upper);
        password[1] = PickRandom(lower);
        password[2] = PickRandom(digits);
        password[3] = PickRandom(special);

        for (int i = 4; i < password.Length; i++)
            password[i] = PickRandom(all);

        // Shuffle so the guaranteed categories above aren't always sitting in the first four
        // positions.
        for (int i = password.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string(password);
    }

    private static char PickRandom(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
