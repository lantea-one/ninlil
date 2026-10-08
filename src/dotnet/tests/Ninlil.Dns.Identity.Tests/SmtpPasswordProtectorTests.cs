using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests <see cref="SmtpPasswordProtector" /> in memory: the encrypt and decrypt
///     round trip, the precedence <see cref="SmtpPasswordProtector.ResolvePassword" /> applies
///     between the plaintext and encrypted password, and that a different key ring cannot read
///     the ciphertext. Each test shares one <see cref="EphemeralDataProtectionProvider" />, as a
///     persistent key ring would.
/// </summary>
public sealed class SmtpPasswordProtectorTests
{
    [Fact]
    public void ProtectThenUnprotectRecoversTheOriginalPassword()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        Assert.Equal("hunter2", protector.Unprotect(protector.Protect("hunter2")));
    }

    [Fact]
    public void EncryptedPayloadIsNotThePlaintextItself()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        Assert.DoesNotContain("hunter2-secret", protector.Protect("hunter2-secret"));
    }

    [Fact]
    public void UnprotectOfABlankValueReturnsEmptyRatherThanThrowing()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
        Assert.Equal(string.Empty, protector.Unprotect(null));
    }

    [Fact]
    public void ResolvePasswordPrefersAPlaintextPasswordOverAnEncryptedOne()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        EmailSenderOptions options = new()
        {
            Password = "from-environment",
            PasswordEncrypted = protector.Protect("from-file"),
        };

        Assert.Equal("from-environment", protector.ResolvePassword(options));
    }

    [Fact]
    public void ResolvePasswordDecryptsTheEncryptedPasswordWhenThereIsNoPlaintextOne()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        EmailSenderOptions options = new()
        {
            Password = "",
            PasswordEncrypted = protector.Protect("from-file"),
        };

        Assert.Equal("from-file", protector.ResolvePassword(options));
    }

    [Fact]
    public void ResolvePasswordReturnsNullWhenNeitherPasswordIsSet()
    {
        SmtpPasswordProtector protector = new(new EphemeralDataProtectionProvider());

        Assert.Null(protector.ResolvePassword(new EmailSenderOptions()));
        Assert.Null(
            protector.ResolvePassword(
                new EmailSenderOptions { Password = "", PasswordEncrypted = "" }
            )
        );
    }

    [Fact]
    public void AProviderWithADifferentKeyRingCannotDecryptTheCiphertext()
    {
        SmtpPasswordProtector protectorA = new(new EphemeralDataProtectionProvider());
        SmtpPasswordProtector protectorB = new(new EphemeralDataProtectionProvider());

        string encrypted = protectorA.Protect("hunter2");

        Assert.ThrowsAny<CryptographicException>(() => protectorB.Unprotect(encrypted));
    }
}
