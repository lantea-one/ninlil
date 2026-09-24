using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Ninlil.Dns.Identity.Sso;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class covers <see cref="OidcClientSecretProtector" />'s own encrypt/decrypt round
///     trip, using a single shared <see cref="IDataProtectionProvider" /> instance the way a
///     real, persistent Data Protection key ring would behave -- deliberately not two separate
///     <see cref="EphemeralDataProtectionProvider" /> instances, since each one generates its own
///     fresh random key (the codebase's own bug catalog, entry 10), which
///     <see cref="TwoIndependentProviderInstancesCannotDecryptEachOthersPayloads" /> below relies
///     on directly, rather than working around it.
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

        // No client secret has ever been configured -- OidcSsoOptions.ClientSecretEncrypted
        // defaults to an empty string, not null, and this method must tolerate reading that
        // compiled-in default without ever having called Protect first.
        Assert.Equal(string.Empty, protector.Unprotect(string.Empty));
    }
}
