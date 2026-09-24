using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Ninlil.Dns.Server.Authoritative.CloudSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CloudSync;

/// <summary>
///     This class covers <see cref="CloudCredentialProtector" />'s own encrypt/decrypt round
///     trip, using a single shared <see cref="IDataProtectionProvider" /> instance the way a
///     real, persistent Data Protection key ring would behave -- deliberately not two separate
///     <see cref="EphemeralDataProtectionProvider" /> instances, since each one generates its own
///     fresh random key -- a known <c>EphemeralDataProtectionProvider</c> gotcha that applies
///     project-wide.
/// </summary>
public sealed class CloudCredentialProtectorTests
{
    [Fact]
    public void ProtectThenUnprotectRecoversTheOriginalCredentialFields()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CloudCredentialProtector protector = new(provider);

        Dictionary<string, string> credentials = new()
        {
            ["ApiToken"] = "test-token-value",
            ["AccessKeyId"] = "AKIAEXAMPLE",
        };

        string encrypted = protector.Protect(credentials);
        IReadOnlyDictionary<string, string> decrypted = protector.Unprotect(encrypted);

        Assert.Equal(credentials["ApiToken"], decrypted["ApiToken"]);
        Assert.Equal(credentials["AccessKeyId"], decrypted["AccessKeyId"]);
    }

    [Fact]
    public void EncryptedPayloadIsNotThePlaintextItself()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CloudCredentialProtector protector = new(provider);

        string encrypted = protector.Protect(
            new Dictionary<string, string> { ["ApiToken"] = "super-secret" }
        );

        Assert.DoesNotContain("super-secret", encrypted);
    }

    [Fact]
    public void TwoIndependentProviderInstancesCannotDecryptEachOthersPayloads()
    {
        CloudCredentialProtector protectorA = new(new EphemeralDataProtectionProvider());
        CloudCredentialProtector protectorB = new(new EphemeralDataProtectionProvider());

        string encrypted = protectorA.Protect(
            new Dictionary<string, string> { ["ApiToken"] = "value" }
        );

        Assert.ThrowsAny<Exception>(() => protectorB.Unprotect(encrypted));
    }
}
