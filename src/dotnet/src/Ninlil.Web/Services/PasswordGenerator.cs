using System.Security.Cryptography;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class generates a strong random password for the bootstrap administrator
///     (<c>AdministratorBootstrapper</c>) and for the <c>AutoGenerate</c> mode of an
///     administrator's password reset (<c>UsersController.ResetPassword</c>). The password meets
///     the default password policy; a policy raised past 24 characters through the identity
///     settings would reject it.
/// </summary>
public static class PasswordGenerator
{
    /// <summary>
    ///     This method generates a random 24-character password with at least one uppercase
    ///     letter, lowercase letter, digit and symbol. Look-alike characters (I, l, O, 0, 1) are
    ///     left out.
    /// </summary>
    public static string GenerateStrongRandomPassword()
    {
        // One character from each of the four classes is placed first so the default policy (12+
        // characters, upper, lower, digit, symbol) is always met; plain random bytes, base64 or
        // otherwise, can miss a class.
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
