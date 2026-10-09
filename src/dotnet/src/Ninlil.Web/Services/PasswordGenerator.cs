using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class generates a strong random password for the bootstrap administrator
///     (<c>AdministratorBootstrapper</c>) and for the <c>AutoGenerate</c> mode of an
///     administrator's password reset (<c>UsersController.ResetPassword</c>). The password meets
///     the live <see cref="PasswordOptions" /> policy that
///     <c>IdentitySettingsController</c> edits: the length grows with <c>RequiredLength</c> and
///     the number of distinct characters with <c>RequiredUniqueChars</c>. The settings controller
///     caps those two values (128 and 64) and rejects a unique count above the length, so a
///     policy it accepts is always satisfiable from the 68-character alphabet used here.
/// </summary>
public static class PasswordGenerator
{
    /// <summary>
    ///     This constant contains the length of a generated password when the policy asks for
    ///     less.
    /// </summary>
    public const int MinimumGeneratedLength = 24;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Special = "!@#$%^&*-_=+";
    private const string All = Upper + Lower + Digits + Special;

    /// <summary>
    ///     This method generates a random password for the default password policy: 24 characters
    ///     with at least one uppercase letter, lowercase letter, digit and symbol.
    /// </summary>
    public static string GenerateStrongRandomPassword() =>
        GenerateStrongRandomPassword(new PasswordOptions());

    /// <summary>
    ///     This method generates a random password that satisfies a password policy. It has at
    ///     least one uppercase letter, lowercase letter, digit and symbol whatever the policy
    ///     requires, is at least 24 characters long and at least <c>RequiredLength</c> long, and
    ///     holds at least <c>RequiredUniqueChars</c> distinct characters. Look-alike characters
    ///     (I, l, O, 0, 1) are left out.
    /// </summary>
    /// <param name="options">The policy to meet; null means the default policy.</param>
    public static string GenerateStrongRandomPassword(PasswordOptions options)
    {
        options ??= new PasswordOptions();

        int length = Math.Max(MinimumGeneratedLength, options.RequiredLength);

        // A unique count above the length or the alphabet cannot be met; the settings controller
        // never lets one in, but a hand-edited file could.
        int unique = Math.Clamp(options.RequiredUniqueChars, 1, Math.Min(length, All.Length));

        // Distinct characters come first: one from each of the four classes (so any class
        // requirement is met; plain random bytes can miss a class), then draws without
        // replacement from the unused alphabet until the unique count is met. The rest is drawn
        // freely. A final shuffle hides the structure.
        List<char> pool = [.. All];
        List<char> password = new(length);

        foreach (string alphabet in new[] { Upper, Lower, Digits, Special })
        {
            char picked = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            pool.Remove(picked);
            password.Add(picked);
        }

        while (password.Count < unique)
        {
            int index = RandomNumberGenerator.GetInt32(pool.Count);
            password.Add(pool[index]);
            pool.RemoveAt(index);
        }

        while (password.Count < length)
            password.Add(All[RandomNumberGenerator.GetInt32(All.Length)]);

        for (int i = password.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (password[i], password[j]) = (password[j], password[i]);
        }

        return new string([.. password]);
    }
}
