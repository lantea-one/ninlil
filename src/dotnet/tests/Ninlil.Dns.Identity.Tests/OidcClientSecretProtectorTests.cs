using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Ninlil.Dns.Identity.Sso;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests the encrypt and decrypt round trip of <see cref="OidcClientSecretProtector" />
///     in memory. Round trips share one <see cref="IDataProtectionProvider" />, as a persistent
///     key ring would. Two separate <see cref="EphemeralDataProtectionProvider" /> instances each
///     generate a random key and cannot read each other's payloads;
///     <see cref="TwoIndependentProviderInstancesCannotDecryptEachOthersPayloads" /> checks that.
/// </summary>
public sealed class OidcClientSecretProtectorTests
{
    [Fact]
    public void ProtectThenUnprotectRecoversTheOriginalSecret()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        OidcClientSecretProtector protector = new(provider);

        string encrypted = protector.Protect("correct-horse-battery-staple");

        Assert.Equal("correct-horse-battery-staple", protector.Unprotect(encrypted));
    }

    [Fact]
    public void EncryptedPayloadIsNotThePlaintextItself()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        OidcClientSecretProtector protector = new(provider);

        string encrypted = protector.Protect("super-secret-client-secret");

        Assert.DoesNotContain("super-secret-client-secret", encrypted);
    }

    [Fact]
    public void TwoIndependentProviderInstancesCannotDecryptEachOthersPayloads()
    {
        OidcClientSecretProtector protectorA = new(new EphemeralDataProtectionProvider());
        OidcClientSecretProtector protectorB = new(new EphemeralDataProtectionProvider());

        string encrypted = protectorA.Protect("value");

        Assert.ThrowsAny<Exception>(() => protectorB.Unprotect(encrypted));
    }

    [Fact]
    public void UnprotectOfABlankValueReturnsEmptyRatherThanThrowing()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        OidcClientSecretProtector protector = new(provider);

        // With no secret configured, OidcSsoOptions.ClientSecretEncrypted is an empty string,
        // which must decrypt to empty without Protect ever having been called.
        Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
    }
}
